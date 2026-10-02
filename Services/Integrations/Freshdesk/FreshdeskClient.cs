using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ProgrammePulse.Models.Integrations.Freshdesk.Raw;
using ProgrammePulse.Models.ServiceOps;

namespace ProgrammePulse.Services.Integrations.Freshdesk;

/// <summary>
/// Read-only Freshdesk v2 client. The credential is supplied per request
/// so a tenant's token never enters pooled HttpClient defaults — the same
/// rule JiraApiClient and GitHubEvidenceClient follow, and it matters
/// here because several tenants' desks share this client.
///
/// Freshdesk authenticates an API key as the HTTP Basic username with an
/// arbitrary password, which is the vendor's documented scheme.
///
/// Every request's host is re-validated through <see cref="DeskHostPolicy"/>
/// even though the connection stored an already-canonical value, so no
/// caller can bypass the allow-list by constructing a call directly.
///
/// Field mapping is configurable-by-convention and unverified against a
/// live tenant — see the notes on FreshdeskTicket. The parsing here is
/// defensive: an unrecognised status or a missing field yields Unknown
/// and the raw value is preserved, rather than guessing.
/// </summary>
public sealed class FreshdeskClient(HttpClient client) : IFreshdeskClient
{
    private const int PageSize = 100;

    /// <summary>
    /// Page budget per run. Freshdesk caps how deep list pagination can
    /// go, and a first sync of a busy desk will exceed this — which is
    /// reported as partial, and the next run continues from the advanced
    /// watermark rather than starting over.
    /// </summary>
    private const int MaxPagesPerRun = 40;

    public async Task<string?> VerifyAsync(string apiBaseUrl, string apiToken, CancellationToken cancellationToken)
    {
        using var document = await GetAsync(apiBaseUrl, "tickets?per_page=1", apiToken, cancellationToken);
        if (document is null)
        {
            return null;
        }

        // A valid credential that returns an array — even an empty one —
        // is a working connection on a desk with no tickets yet.
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? new Uri(apiBaseUrl).Host
            : null;
    }

    public Task<FreshdeskPage> GetTicketsUpdatedSinceAsync(
        string apiBaseUrl, string apiToken, DateTime? updatedSinceUtc, CancellationToken cancellationToken) =>
        FetchAsync(apiBaseUrl, apiToken, updatedSinceUtc, withdrawn: false, cancellationToken);

    public Task<FreshdeskPage> GetWithdrawnTicketsAsync(
        string apiBaseUrl, string apiToken, DateTime? updatedSinceUtc, CancellationToken cancellationToken) =>
        FetchAsync(apiBaseUrl, apiToken, updatedSinceUtc, withdrawn: true, cancellationToken);

    private async Task<FreshdeskPage> FetchAsync(
        string apiBaseUrl, string apiToken, DateTime? updatedSinceUtc, bool withdrawn, CancellationToken cancellationToken)
    {
        var tickets = new List<FreshdeskTicket>();
        DateTime? watermark = updatedSinceUtc;

        var since = updatedSinceUtc is null
            ? string.Empty
            : $"&updated_since={Uri.EscapeDataString(Iso(updatedSinceUtc.Value))}";
        var filter = withdrawn ? "&filter=deleted" : string.Empty;

        for (var page = 1; page <= MaxPagesPerRun; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            JsonDocument? document;
            try
            {
                document = await GetAsync(
                    apiBaseUrl, $"tickets?per_page={PageSize}&page={page}&order_by=updated_at&order_type=asc{since}{filter}",
                    apiToken, cancellationToken, throwOnAccessLoss: true);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                // Keep what we fetched; do not advance the coverage claim.
                return new FreshdeskPage(tickets, watermark, false, Describe(ex));
            }

            if (document is null)
            {
                return new FreshdeskPage(tickets, watermark, false, "the desk could not be read");
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return new FreshdeskPage(tickets, watermark, false, "unexpected response shape");
                }

                var count = 0;
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    count++;
                    var ticket = MapTicket(element, withdrawn, apiBaseUrl);
                    if (ticket is null)
                    {
                        continue;
                    }

                    tickets.Add(ticket);
                    if (ticket.UpdatedAtUtc is { } updated && (watermark is null || updated > watermark))
                    {
                        watermark = updated;
                    }
                }

                // A short page is the end of the stream.
                if (count < PageSize)
                {
                    return new FreshdeskPage(tickets, watermark, true);
                }
            }
        }

        // Ran out of page budget. There is more, and saying so is the
        // honest answer — the watermark advances so the next run resumes.
        return new FreshdeskPage(
            tickets, watermark, false,
            $"more than {MaxPagesPerRun * PageSize} tickets changed since the last run; the rest follow on the next sync");
    }

    private async Task<JsonDocument?> GetAsync(
        string apiBaseUrl, string relativePath, string apiToken, CancellationToken cancellationToken, bool throwOnAccessLoss = false)
    {
        // Re-validated on every request. The connection stored a
        // canonical value, but this is the check that cannot be bypassed.
        var canonical = DeskHostPolicy.CanonicalizeFreshdesk(apiBaseUrl)
            ?? throw new InvalidOperationException("The desk connection's API host is not permitted.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{canonical}/{relativePath}");

        // Freshdesk: API key as the Basic username, any password.
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiToken}:X")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("ProgrammePulse-ServiceOps");

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode == (HttpStatusCode)429)
        {
            // Rate limited. Transient, so it is a partial run rather than
            // a lost permission — the distinction the caller needs.
            throw new HttpRequestException("the Freshdesk rate limit was reached");
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            if (throwOnAccessLoss)
            {
                throw new DeskAccessLostException(new Uri(canonical).Host);
            }

            return null;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "the run was cancelled or timed out",
        HttpRequestException http => http.Message,
        _ => "the response could not be read"
    };

    // ---- mapping ----

    private static FreshdeskTicket? MapTicket(JsonElement element, bool withdrawnStream, string apiBaseUrl)
    {
        var id = Text(element, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var stats = Child(element, "stats");
        var status = Number(element, "status");

        // The `deleted` and `spam` flags are authoritative; the stream
        // they arrived on is a fallback for desks that only expose the
        // filtered list.
        var isDeleted = Flag(element, "deleted") ?? withdrawnStream;
        var isSpam = Flag(element, "spam") ?? false;

        return new FreshdeskTicket(
            Id: id!,
            CreatedAtUtc: Timestamp(element, "created_at"),
            UpdatedAtUtc: Timestamp(element, "updated_at"),
            StatusCode: status,
            RawStatus: status?.ToString(CultureInfo.InvariantCulture),
            PriorityCode: Number(element, "priority"),
            ComponentTag: ComponentTag(element),
            CaseType: Text(element, "type"),
            IsDeleted: isDeleted,
            IsSpam: isSpam,
            ResolvedAtUtc: Timestamp(stats, "resolved_at"),
            ClosedAtUtc: Timestamp(stats, "closed_at"),
            ReopenedAtUtc: Timestamp(stats, "reopened_at"),
            ResponderId: Text(element, "responder_id"),
            ResponderName: null,
            LinkedIssueKeys: [],
            SourceUrl: $"https://{new Uri(DeskHostPolicy.CanonicalizeFreshdesk(apiBaseUrl)!).Host}/a/tickets/{id}");
    }

    /// <summary>
    /// Which field carries the product/component varies by customer, so
    /// this tries the conventional ones in order and gives up rather than
    /// inventing a value. What it finds is still only a *raw* tag: the
    /// mapper checks it against the tenant's approved list before it
    /// becomes a component on a chart.
    /// </summary>
    private static string? ComponentTag(JsonElement element)
    {
        foreach (var field in new[] { "product_id", "category" })
        {
            var direct = Text(element, field);
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }
        }

        var custom = Child(element, "custom_fields");
        if (custom.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in new[] { "cf_component", "cf_product", "cf_category" })
            {
                var value = Text(custom, candidate);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        // First tag, if the customer uses tags for this. Ordered by the
        // desk, so it is a convention rather than a guarantee.
        if (element.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tags.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tag.GetString()))
                {
                    return tag.GetString();
                }
            }
        }

        return null;
    }

    private static JsonElement Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child) ? child : default;

    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static int? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static bool? Flag(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;

    private static DateTime? Timestamp(JsonElement element, string name)
    {
        var text = Text(element, name);
        return DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string Iso(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
