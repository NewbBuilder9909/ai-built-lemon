using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProgrammePulse.Services.Integrations.Aikido;

/// <summary>One page as captured for Bronze: already reduced to the allow-listed fields.</summary>
public sealed record AikidoPage(string Endpoint, int Page, IReadOnlyList<JsonElement> Items, string RedactedJson);

/// <summary>
/// Everything one run read. <see cref="IsComplete"/> is false when any list was
/// cut short (rate limit, failed page, page budget); the caller then keeps what
/// was read but does not claim a clean sync.
/// </summary>
public sealed record AikidoReadResult(
    IReadOnlyList<AikidoPage> Repositories,
    IReadOnlyList<AikidoPage> CheckConfigurations,
    IReadOnlyList<AikidoPage> Issues,
    IReadOnlyList<AikidoPage> CheckRuns,
    bool IsComplete,
    string? IncompleteReason);

/// <summary>
/// Read-only Aikido public API access. Bronze: the only layer that knows
/// Aikido's API shape. Scopes needed: issues:read, repositories:read,
/// reports:read; nothing here writes.
/// </summary>
public interface IAikidoClient
{
    /// <summary>
    /// True when the credential yields a token and can list repositories.
    /// Throws <see cref="AikidoAccessLostException"/> for rejected credentials.
    /// </summary>
    Task<bool> VerifyAsync(string region, string clientId, string clientSecret, CancellationToken cancellationToken);

    Task<AikidoReadResult> ReadAsync(string region, string clientId, string clientSecret, CancellationToken cancellationToken);
}

/// <summary>The credential was rejected or lost a scope. Retrying will not help; the admin must reconnect.</summary>
public sealed class AikidoAccessLostException(string reason) : InvalidOperationException(reason);

/// <summary>
/// The Aikido regions and their hosts. An allow-list, checked on every
/// request, so a stored region can never send a tenant's credential elsewhere.
/// </summary>
public static class AikidoRegions
{
    private static readonly IReadOnlyDictionary<string, string> Hosts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["eu"] = "app.aikido.dev",
        ["us"] = "app.us.aikido.dev",
        ["au"] = "app.au.aikido.dev",
        ["me"] = "app.me.aikido.dev",
    };

    public static IReadOnlyCollection<string> All => Hosts.Keys.ToList();

    /// <summary>The normalised region, or null if it is not one Aikido operates.</summary>
    public static string? Normalise(string? region)
    {
        var value = region?.Trim().ToLowerInvariant();
        return value is not null && Hosts.ContainsKey(value) ? value : null;
    }

    public static string HostFor(string region) =>
        Normalise(region) is { } known
            ? Hosts[known]
            : throw new InvalidOperationException("The Aikido region is not permitted.");
}

/// <summary>
/// Aikido client. The credential is supplied per call and exchanged for a
/// token per run, so a tenant's token never sits in pooled HttpClient state
/// (the Freshdesk and GitHub rule). Transient failures and 429s are retried by
/// <c>TransientHttpRetryHandler</c>, which honours Retry-After; a run that
/// still fails is reported partial rather than retried into Aikido's
/// 20-requests-a-minute limit.
///
/// Bronze is <b>allow-listed, not verbatim</b>: Aikido's issue payload carries
/// file paths and line numbers, which this product never stores. Each page is
/// reduced to <see cref="IssueFields"/> (and the equivalents) before anything
/// keeps it.
/// </summary>
public sealed class AikidoClient(HttpClient client) : IAikidoClient
{
    /// <summary>Page budget per list, per run. Exhausting it is reported as partial, never as done.</summary>
    public const int MaxPagesPerList = 10;

    public static readonly IReadOnlyList<string> RepositoryFields =
        ["id", "name", "external_repo_id", "provider", "url", "branch", "active", "connectivity", "last_scanned_at"];

    public static readonly IReadOnlyList<string> CheckConfigurationFields =
        ["code_repo_id", "is_enabled", "minimum_severity", "fail_on_dependency_scan", "fail_on_sast_scan", "fail_on_secrets_scan",
         "fail_on_iac_scan", "fail_on_malware_scan", "fail_on_license_scan"];

    public static readonly IReadOnlyList<string> IssueFields =
        ["id", "group_id", "type", "severity", "severity_score", "status", "code_repo_id", "cve_id", "rule_id",
         "affected_package", "first_detected_at", "closed_at", "sla_remediate_by"];

    public static readonly IReadOnlyList<string> CheckRunFields =
        ["scan_id", "code_repo_id", "gate_status", "started_at", "completed_at", "related_commit_sha", "pull_request_url"];

    private const string ApiPath = "/api/public/v1";

    public async Task<bool> VerifyAsync(string region, string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(region, clientId, clientSecret, cancellationToken);
        using var document = await GetAsync(region, token, "/repositories/code?per_page=1&page=0", cancellationToken);
        return document.RootElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object;
    }

    public async Task<AikidoReadResult> ReadAsync(string region, string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(region, clientId, clientSecret, cancellationToken);
        var reasons = new List<string>();

        var repositories = await ReadListAsync(region, token, "/repositories/code", "per_page=200", 200, RepositoryFields, "id", reasons, cancellationToken);
        var configurations = await ReadListAsync(region, token, "/repositories/code/continuous_integration/checks", "per_page=100", 100, CheckConfigurationFields, "code_repo_id", reasons, cancellationToken);

        // Every status, so a finding that closes in Aikido closes here too.
        var issues = await ReadListAsync(region, token, "/issues/export", "format=json&filter_status=all&per_page=1000", 1000, IssueFields, "id", reasons, cancellationToken);

        // Recent runs only, one page: the check-run fields are not yet confirmed live.
        var runs = await ReadListAsync(region, token, "/report/ciScans", "filter_gate_status=all&per_page=100", 100, CheckRunFields, "scan_id", reasons, cancellationToken, maxPages: 1, budgetIsIncomplete: false);

        return new AikidoReadResult(repositories, configurations, issues, runs, reasons.Count == 0,
            reasons.Count == 0 ? null : string.Join("; ", reasons));
    }

    private async Task<IReadOnlyList<AikidoPage>> ReadListAsync(
        string region, string token, string endpoint, string query, int pageSize, IReadOnlyList<string> fields, string idField,
        List<string> reasons, CancellationToken cancellationToken, int maxPages = MaxPagesPerList, bool budgetIsIncomplete = true)
    {
        var pages = new List<AikidoPage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 0; page < maxPages; page++)
        {
            List<JsonElement> items;
            try
            {
                using var document = await GetAsync(region, token, $"{endpoint}?{query}&page={page}", cancellationToken);
                items = AikidoMapper.Items(document.RootElement).ToList();
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                reasons.Add($"{endpoint} could not be read in full ({Describe(ex)})");
                return pages;
            }

            // A page whose items were all seen already means the API ignored the
            // page number: stop rather than loop to the budget re-reading page one.
            var fresh = items.Where(i => IdOf(i, idField) is not { } id || seen.Add(id)).ToList();
            if (fresh.Count == 0)
            {
                return pages;
            }

            var redacted = fresh.Select(i => Redact(i, fields)).ToList();
            pages.Add(new AikidoPage(endpoint, page, redacted, JsonSerializer.Serialize(redacted)));

            if (items.Count < pageSize)
            {
                return pages;
            }
        }

        if (budgetIsIncomplete)
        {
            reasons.Add($"{endpoint} has more than {maxPages * pageSize} rows; the rest follow on the next sync");
        }

        return pages;
    }

    /// <summary>Keeps only the allow-listed top-level fields of one item.</summary>
    public static JsonElement Redact(JsonElement item, IReadOnlyList<string> fields)
    {
        var kept = new JsonObject();
        if (item.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in fields)
            {
                if (item.TryGetProperty(field, out var value))
                {
                    kept[field] = JsonNode.Parse(value.GetRawText());
                }
            }
        }

        return JsonSerializer.SerializeToElement(kept);
    }

    private async Task<string> GetTokenAsync(string region, string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{AikidoRegions.HostFor(region)}/api/oauth/token")
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")]),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
        {
            throw new AikidoAccessLostException("Aikido rejected the client id or secret.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("access_token", out var token) && token.ValueKind == JsonValueKind.String
            ? token.GetString()!
            : throw new AikidoAccessLostException("Aikido returned no access token for these credentials.");
    }

    private async Task<JsonDocument> GetAsync(string region, string token, string relativePath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{AikidoRegions.HostFor(region)}{ApiPath}{relativePath}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("ProgrammePulse-SecurityAssurance");

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AikidoAccessLostException(
                "Aikido refused a read. The API client may have lost the issues:read, repositories:read or reports:read scope.");
        }

        if (response.StatusCode == (HttpStatusCode)429)
        {
            throw new HttpRequestException("the Aikido rate limit was reached");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string? IdOf(JsonElement item, string field) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(field, out var value) && value.ValueKind is JsonValueKind.Number or JsonValueKind.String
            ? value.ToString()
            : null;

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "timed out",
        HttpRequestException http => http.Message,
        _ => "the response could not be read",
    };
}
