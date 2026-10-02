namespace ProgrammePulse.Services.ProgrammeOps;

public enum DecisionWriteOutcome
{
    Saved,
    NotFound,

    /// <summary>A later review exists: earlier reviews are the record and can't be changed.</summary>
    NotLatest
}

/// <summary>A place a staff member appears in the review history, for a subject access request.</summary>
public sealed record EvidenceReviewStaffReference(string Summary, DateTime RecordedAtUtc);

/// <summary>
/// Recorded Evidence Check reviews, tenant-scoped on every method that
/// takes a tenant. The two staff-reference methods are keyed by StaffKey
/// alone, like IIdentityResolutionRepository's GDPR methods, because a
/// StaffKey is globally unique.
/// </summary>
public interface IEvidenceReviewRepository
{
    Task AddAsync(EvidenceReview review);

    /// <summary>The whole review, including every finding's source records.</summary>
    Task<EvidenceReview?> GetAsync(Guid tenantId, Guid reviewKey);

    /// <summary>
    /// The tenant's latest review of one scope (<see cref="EvidenceScope.Key"/>),
    /// with source records: comparison and carry-forward match records across
    /// reviews, so an unchanged count over different records isn't reported
    /// as no change. Reviews from before scopes existed count as whole-organisation.
    /// </summary>
    Task<EvidenceReview?> GetLatestAsync(Guid tenantId, string scopeKey);

    /// <summary>The review of the same scope recorded immediately before <paramref name="reviewKey"/>, with source records, or null if it was the first.</summary>
    Task<EvidenceReview?> GetPreviousAsync(Guid tenantId, Guid reviewKey);

    /// <summary>Newest first. Findings carry no source records: history needs the counts, not the evidence.</summary>
    Task<IReadOnlyList<EvidenceReview>> GetHistoryAsync(Guid tenantId, int take);

    /// <summary>Records a decision on one finding of the tenant's latest review.</summary>
    Task<DecisionWriteOutcome> DecideAsync(Guid tenantId, Guid reviewKey, string findingKey, FindingDisposition disposition,
        Guid? ownerStaffKey, DateOnly? targetDate, string? note, Guid? decidedByStaffKey, DateTime decidedAtUtc);

    Task<IReadOnlyList<EvidenceReviewStaffReference>> GetStaffReferencesAsync(Guid staffKey);

    /// <summary>Removes the staff key from owner, decider and recorder columns. Free-text notes are not rewritten.</summary>
    Task EraseStaffReferencesAsync(Guid staffKey);
}
