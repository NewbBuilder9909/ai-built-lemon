using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.SecurityAssurance;
using ProgrammePulse.Services.SecurityAssurance;

namespace ProgrammePulse.Services.Integrations.Aikido;

/// <summary>
/// The one place Aikido's API shapes and the tool-neutral security records
/// both appear. Pure, so every rule below is unit-tested against payloads in
/// the shape the live workspace returned (probe run of 26 September 2026).
///
/// Metadata only: file paths, line numbers, titles and snippets in Aikido's
/// payload are never carried into a record.
/// </summary>
public static partial class AikidoMapper
{
    public const string GitHubProvider = "GitHub";

    /// <summary>
    /// GitHub owner/name from an Aikido repository URL. Aikido returns GitHub's
    /// API form (https://api.github.com/repos/{owner}/{name}); the web form is
    /// accepted too. Anchored, so "repos" can never be read as an owner, and a
    /// malformed URL yields null rather than a guess.
    /// </summary>
    public static CodeRepositoryRef? GitHubRepository(string? url)
    {
        var match = ApiUrl().Match(url ?? string.Empty);
        if (!match.Success)
        {
            match = WebUrl().Match(url ?? string.Empty);
        }

        if (!match.Success)
        {
            return null;
        }

        var owner = match.Groups["owner"].Value;
        return CodeRepositoryRef.TryCreate(GitHubProvider, owner, $"{owner}/{match.Groups["name"].Value}", out _);
    }

    /// <summary>
    /// Maps the repository list and the per-repository PR-check configuration to
    /// observations. Only GitHub repositories whose URL parses are kept; two
    /// repositories resolving to the same owner/name are both dropped rather than
    /// one silently taking the other's findings.
    /// </summary>
    public static IReadOnlyList<RepositoryGateObservation> Observations(
        Guid tenantId, IEnumerable<JsonElement> repositories, IEnumerable<JsonElement> checkConfigurations, DateTime observedAtUtc)
    {
        var configsByRepo = checkConfigurations
            .Where(c => Number(c, "code_repo_id") is not null)
            .GroupBy(c => Number(c, "code_repo_id")!.Value)
            .ToDictionary(g => g.Key, g => g.Last());

        var candidates = repositories
            .Where(r => string.Equals(Text(r, "provider"), "github", StringComparison.OrdinalIgnoreCase) && Number(r, "id") is not null)
            .Select(r => (Json: r, Id: Number(r, "id")!.Value, Repository: GitHubRepository(Text(r, "url"))))
            .Where(r => r.Repository is not null)
            .ToList();

        var unique = candidates
            .GroupBy(r => r.Repository!.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .Select(g => g.Single());

        return unique.Select(r =>
        {
            configsByRepo.TryGetValue(r.Id, out var config);
            // A configuration explicitly switched off is no gate. Absent means on:
            // the per-repository list only returns configurations that exist.
            var configured = config.ValueKind == JsonValueKind.Object && !IsFalse(config, "is_enabled");
            var lastScanned = Number(r.Json, "last_scanned_at");
            return new RepositoryGateObservation
            {
                TenantId = tenantId,
                Tool = SecurityTools.Aikido,
                Repository = r.Repository!,
                ExternalRepoId = Text(r.Json, "external_repo_id"),
                ToolRepositoryId = r.Id.ToString(CultureInfo.InvariantCulture),
                LastScannedAtUtc = lastScanned is > 0 ? FromUnix(lastScanned.Value) : null,
                GateConfigured = configured,
                GateMinimumSeverity = configured ? Severity(Text(config, "minimum_severity")) : null,
                FailsOnDependencies = configured && Flag(config, "fail_on_dependency_scan"),
                FailsOnCode = configured && Flag(config, "fail_on_sast_scan"),
                FailsOnSecrets = configured && Flag(config, "fail_on_secrets_scan"),
                ObservedAtUtc = observedAtUtc,
            };
        }).ToList();
    }

    /// <summary>
    /// Maps issues to findings for repositories that were observed. Issues with no
    /// code repository (domains, cloud) or of an unrecognised severity are left out:
    /// contract obligations here are about code repositories.
    /// </summary>
    public static IReadOnlyList<SecurityFinding> Findings(
        Guid tenantId, IEnumerable<JsonElement> issues, IReadOnlyList<RepositoryGateObservation> observations, DateTime seenAtUtc)
    {
        var repositoriesById = observations.ToDictionary(o => o.ToolRepositoryId, o => o.Repository, StringComparer.Ordinal);
        var findings = new List<SecurityFinding>();
        foreach (var issue in issues)
        {
            var repoId = Number(issue, "code_repo_id")?.ToString(CultureInfo.InvariantCulture);
            var id = Number(issue, "id")?.ToString(CultureInfo.InvariantCulture);
            var severity = Severity(Text(issue, "severity"));
            var status = Status(Text(issue, "status"));
            var detected = Number(issue, "first_detected_at");
            if (repoId is null || id is null || severity is null || status is null || detected is null
                || !repositoriesById.TryGetValue(repoId, out var repository))
            {
                continue;
            }

            var closed = Number(issue, "closed_at");
            findings.Add(new SecurityFinding
            {
                TenantId = tenantId,
                Tool = SecurityTools.Aikido,
                ExternalId = id,
                Repository = repository,
                Severity = severity.Value,
                Status = status.Value,
                FindingType = Clip(Text(issue, "type"), 32) ?? "unknown",
                CveId = Clip(Text(issue, "cve_id"), 64),
                RuleId = Clip(Text(issue, "rule_id"), 128),
                AffectedPackage = Clip(Text(issue, "affected_package"), 256),
                FirstDetectedAtUtc = FromUnix(detected.Value),
                ClosedAtUtc = closed is > 0 ? FromUnix(closed.Value) : null,
                LastSeenAtUtc = seenAtUtc,
            });
        }

        return findings;
    }

    /// <summary>Maps pull-request check runs for observed repositories.</summary>
    public static IReadOnlyList<SecurityCheckRun> CheckRuns(
        Guid tenantId, IEnumerable<JsonElement> scans, IReadOnlyList<RepositoryGateObservation> observations)
    {
        var repositoriesById = observations.ToDictionary(o => o.ToolRepositoryId, o => o.Repository, StringComparer.Ordinal);
        var runs = new List<SecurityCheckRun>();
        foreach (var scan in scans)
        {
            var repoId = Number(scan, "code_repo_id")?.ToString(CultureInfo.InvariantCulture);
            var id = Number(scan, "scan_id")?.ToString(CultureInfo.InvariantCulture);
            var started = Number(scan, "started_at");
            if (repoId is null || id is null || started is null || !repositoriesById.TryGetValue(repoId, out var repository))
            {
                continue;
            }

            runs.Add(new SecurityCheckRun
            {
                TenantId = tenantId,
                Tool = SecurityTools.Aikido,
                ExternalId = id,
                Repository = repository,
                Outcome = Outcome(Text(scan, "gate_status")),
                StartedAtUtc = FromUnix(started.Value),
                CommitSha = Clip(Text(scan, "related_commit_sha"), 64),
                PullRequestUrl = Clip(Text(scan, "pull_request_url"), 512),
            });
        }

        return runs;
    }

    /// <summary>Aikido list endpoints return either a bare array or an object wrapping one.</summary>
    public static IReadOnlyList<JsonElement> Items(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().Select(e => e.Clone()).ToList();
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value.EnumerateArray().Select(e => e.Clone()).ToList();
                }
            }
        }

        return [];
    }

    public static SecuritySeverity? Severity(string? value) => value?.ToLowerInvariant() switch
    {
        "low" => SecuritySeverity.Low,
        "medium" => SecuritySeverity.Medium,
        "high" => SecuritySeverity.High,
        "critical" => SecuritySeverity.Critical,
        _ => null, // includes "always_pass_check": the check never fails
    };

    public static SecurityFindingStatus? Status(string? value) => value?.ToLowerInvariant() switch
    {
        "open" => SecurityFindingStatus.Open,
        "snoozed" => SecurityFindingStatus.Snoozed,
        "ignored" => SecurityFindingStatus.Ignored,
        "closed" => SecurityFindingStatus.Closed,
        _ => null,
    };

    public static CheckRunOutcome Outcome(string? value) => value?.ToLowerInvariant() switch
    {
        "passed" => CheckRunOutcome.Passed,
        "failed" => CheckRunOutcome.Failed,
        "bypassed" => CheckRunOutcome.Bypassed,
        "timed_out" => CheckRunOutcome.TimedOut,
        "pending" => CheckRunOutcome.Pending,
        _ => CheckRunOutcome.Unknown,
    };

    private static DateTime FromUnix(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt64(out var n) => n,
                JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
                _ => null,
            }
            : null;

    private static bool Flag(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static bool IsFalse(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.False;

    private static string? Clip(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length > length ? value[..length] : value;

    [GeneratedRegex(@"^https://api\.github\.com/repos/(?<owner>[A-Za-z0-9._-]+)/(?<name>[A-Za-z0-9._-]+?)/?$")]
    private static partial Regex ApiUrl();

    [GeneratedRegex(@"^https://(?:www\.)?github\.com/(?<owner>[A-Za-z0-9._-]+)/(?<name>[A-Za-z0-9._-]+?)(?:\.git)?/?$")]
    private static partial Regex WebUrl();
}
