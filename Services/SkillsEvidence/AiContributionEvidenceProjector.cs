using ProgrammePulse.Models.SkillsEvidence;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// Pure composition of already-approved person attribution and artefact-level
/// AI observations. Callers must authorize access to the subject and supply
/// current, non-retracted observations; this is not an access-control service.
/// All joins include tenant, connection, repository, source type and source id.
/// Nothing is written and no skill assertion can be promoted by this projector.
/// </summary>
public static class AiContributionEvidenceProjector
{
    public static AiContributionEvidencePortfolio Build(
        Guid tenantId,
        Guid staffKey,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        IReadOnlyList<EngineeringEvidence> evidence,
        IReadOnlyList<AiAssistanceObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(observations);
        if (tenantId == Guid.Empty || staffKey == Guid.Empty)
            throw new ArgumentException("Tenant and subject are required.");
        if (fromUtc.Kind != DateTimeKind.Utc || toUtcExclusive.Kind != DateTimeKind.Utc || fromUtc >= toUtcExclusive)
            throw new ArgumentException("A non-empty UTC observation window is required.");

        var assistanceByArtifact = observations
            .Where(o => o.Artifact.TenantId == tenantId)
            .Where(o => !string.IsNullOrWhiteSpace(o.ToolName) && !string.IsNullOrWhiteSpace(o.SourceRecordId))
            .Where(o => Enum.IsDefined(o.SignalKind) && Enum.IsDefined(o.Workflow))
            .Distinct()
            .ToLookup(o => o.Artifact);

        var rows = evidence
            .Where(e => e.TenantId == tenantId && e.StaffKey == staffKey && e.IsAttributableToAPerson)
            .Where(e => e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtcExclusive)
            .GroupBy(ContributionArtifactKey.From)
            .Select(group => new AiContributionEvidenceRow(
                group.Key,
                group.Select(e => e.EvidenceKey).Distinct().Order().ToArray(),
                group.Select(e => e.Role).Distinct().Order().ToArray(),
                group.Select(e => e.SourceUrl).OfType<string>()
                    .Where(IsSafeSourceUrl).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                group.Min(e => e.OccurredAtUtc),
                assistanceByArtifact[group.Key].OrderBy(o => o.ObservedAtUtc).ThenBy(o => o.ObservationKey).ToArray()))
            .OrderByDescending(row => row.OccurredAtUtc)
            .ThenBy(row => row.Artifact.RepositoryKey, StringComparer.Ordinal)
            .ThenBy(row => row.Artifact.ExternalId, StringComparer.Ordinal)
            .ToArray();

        return new(tenantId, staffKey, fromUtc, toUtcExclusive, rows);
    }

    // No URL fetching. HTTPS links still need the existing provider-host policy
    // at ingestion; rejecting active-content schemes here protects future views.
    private static bool IsSafeSourceUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);
}
