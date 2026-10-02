using ProgrammePulse.Services.Shared;
using ProgrammePulse.Models.SkillsEvidence;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// One person's engineering evidence, with the caveats that make it
/// readable. Never returned without them — see
/// <see cref="StaffEvidencePortfolio"/>.
/// </summary>
public interface IEvidencePortfolioQueryService
{
    Task<StaffEvidencePortfolio> BuildForStaffAsync(Guid staffKey, Guid tenantId, PageRequest page);

    /// <summary>Tenant-wide coverage, for the connection admin page.</summary>
    Task<EvidenceCoverageSummary> BuildCoverageAsync(Guid tenantId);
}

/// <summary>
/// A person's evidence and everything needed to read it honestly.
///
/// The caveats are not optional fields on the side: a caller cannot
/// obtain the evidence without also obtaining the observation window, the
/// coverage status and the unmapped count. That is deliberate — the
/// failure mode this product must avoid is a confident-looking list of
/// someone's contributions that silently omits half of them because a
/// sync was partial or their second account was never mapped.
///
/// There is no score here, and no ranking. By design: the design document
/// rules out an automatic individual "value", "quality" or maturity rank,
/// and the way to ensure a view cannot show one is to give it nothing to
/// show. EvidencePortfolioShapeTests fails the build if a score-shaped
/// member appears.
/// </summary>
public sealed record StaffEvidencePortfolio
{
    public required Guid StaffKey { get; init; }

    /// <summary>One page, most recent first. Each row links back to the artefact at source.</summary>
    public required IReadOnlyList<EngineeringEvidence> Evidence { get; init; }

    /// <summary>How many artefacts per role — counts, deliberately not weighted into anything.</summary>
    public required IReadOnlyDictionary<EvidenceRole, int> CountsByRole { get; init; }

    /// <summary>
    /// Languages suggested by the file paths this person's work touched.
    /// Means "participated in changes to files classified this way during
    /// the observation window" — never "is proficient". Never joined to a
    /// skill assertion in the data.
    /// </summary>
    public required IReadOnlyList<string> LanguageHints { get; init; }

    public required EvidenceCoverageSummary Coverage { get; init; }

    /// <summary>The external accounts approved as this person, so a reader can see what the list is built from.</summary>
    public required IReadOnlyList<EvidenceActorLink> MappedAccounts { get; init; }

    /// <summary>
    /// Artefacts in the whole record. <see cref="Evidence"/> is one page of
    /// them; the counts, hints and dates here always describe all of them.
    /// </summary>
    public required int EvidenceCount { get; init; }

    public required PageLinks EvidencePages { get; init; }

    public required DateTime? EarliestEvidenceUtc { get; init; }

    public required DateTime? LatestEvidenceUtc { get; init; }

    /// <summary>
    /// True when there is nothing to show *and* nothing was reliably
    /// looked at — the difference between "this person contributed
    /// nothing" and "we cannot say", which a portfolio must never blur.
    /// </summary>
    public bool AbsenceIsInconclusive => EvidenceCount == 0 && !Coverage.IsComplete;
}

public sealed class EvidencePortfolioQueryService(
    IEngineeringEvidenceRepository repository) : IEvidencePortfolioQueryService
{
    public async Task<StaffEvidencePortfolio> BuildForStaffAsync(Guid staffKey, Guid tenantId, PageRequest page)
    {
        var evidence = await repository.GetEvidencePageForStaffAsync(staffKey, tenantId, page);
        var summary = await repository.GetEvidenceSummaryForStaffAsync(staffKey, tenantId);
        var links = await repository.GetActorLinksForStaffAsync(staffKey);

        // Defence in depth: the repository already filters on tenant, and
        // a link is created only against a connection in the caller's
        // tenant, but a portfolio is the one place a leak would be a
        // person's work shown to the wrong organisation.
        var ownLinks = links.Where(l => l.TenantId == tenantId).ToList();

        return new StaffEvidencePortfolio
        {
            StaffKey = staffKey,
            Evidence = evidence.Items,
            EvidenceCount = summary.Count,
            EvidencePages = evidence.Links,
            EarliestEvidenceUtc = summary.EarliestUtc,
            LatestEvidenceUtc = summary.LatestUtc,
            CountsByRole = summary.CountsByRole,
            LanguageHints = summary.LanguageHints,
            Coverage = await BuildCoverageAsync(tenantId),
            MappedAccounts = ownLinks
        };
    }

    public async Task<EvidenceCoverageSummary> BuildCoverageAsync(Guid tenantId)
    {
        var coverage = await repository.GetCoverageAsync(tenantId);
        var unmappedPeople = await repository.CountOpenUnmappedActorsAsync(tenantId, includeBots: false);
        var unattributed = await repository.CountUnattributedAsync(tenantId);

        return new EvidenceCoverageSummary
        {
            Repositories = coverage,
            // Bots are excluded from person attribution by design, so
            // listing them as work awaiting identification would overstate
            // the gap the admin has to close.
            UnmappedActors = unmappedPeople,
            UnattributedEvidence = unattributed,
            // Nothing has completed a clean run against a live
            // installation, so anything shown is a fixture or a sandbox
            // replay and the UI must say so.
            IsUnverifiedReplay = coverage.Count == 0 || coverage.All(c => c.LastSucceededAtUtc is null)
        };
    }
}
