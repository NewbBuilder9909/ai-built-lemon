namespace ProgrammePulse.Models.Programme;

/// <summary>
/// An explicit, admin-approved statement that a person in an external
/// source (a ClickUp user id, a Hub Planner resource id, or an email seen
/// in either) is a particular StaffProfile. Consulted by
/// Services/ProgrammeOps/StaffIdentityResolver, and the only way an external
/// person is attributed: an email match is just a suggestion until an Admin
/// approves it as one of these. A link survives a change of upstream email
/// and can join people whose upstream email differs from their staff record. At least one of
/// ExternalUserId / Email must be set.
/// </summary>
public sealed record ExternalIdentityLink
{
    public required Guid LinkKey { get; init; }

    public Guid? TenantId { get; init; }

    /// <summary>
    /// The tenant's SourceConnection for ExternalSource, stamped when the
    /// link is created. Not part of the uniqueness key (tenantId +
    /// ExternalSource already gives one answer per tenant per source today,
    /// since a tenant has at most one active connection per source) —
    /// provenance for once a tenant can have more than one connection to
    /// the same source. See Models/Programme/SourceConnection.
    /// </summary>
    public Guid? ConnectionKey { get; init; }

    public required string ExternalSource { get; init; }

    public string? ExternalUserId { get; init; }

    public string? Email { get; init; }

    public required Guid StaffKey { get; init; }

    public int? CreatedByMemberId { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}
