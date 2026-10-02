namespace ProgrammePulse.Models.ServiceOps;

/// <summary>
/// Where a case has got to, in provider-neutral terms. Every desk has its
/// own status vocabulary (and customers add their own); the adapter maps
/// into this small set, and <see cref="SupportCaseFact.ProviderStatus"/>
/// keeps the original so a mapping mistake is visible rather than silently
/// baked in.
/// </summary>
public enum SupportCaseState
{
    /// <summary>Open, or waiting on us.</summary>
    Active = 0,

    /// <summary>Waiting on the customer or a third party — not our clock.</summary>
    Pending = 1,

    /// <summary>Service restored, not yet closed.</summary>
    Resolved = 2,

    Closed = 3,

    /// <summary>
    /// Deleted or marked spam at the provider. Kept as a fact, excluded
    /// from every trend: a deleted ticket that silently vanished would
    /// make last month's numbers change retrospectively.
    /// </summary>
    Withdrawn = 4
}

/// <summary>
/// How urgent the desk said it was. Ordered, so "at or above" comparisons
/// work for severity-banded trends.
/// </summary>
public enum SupportCasePriority
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Urgent = 4
}

/// <summary>
/// One support case, as the rest of the application sees it —
/// provider-neutral, so a Zoho Desk or Zendesk adapter later writes the
/// same shape and no view changes.
///
/// **Metadata only.** No ticket body, no attachments, no requester name,
/// email or contact details. That is the design document's
/// data-minimisation rule and it is enforced by the shape of this record:
/// there is nowhere to put them, and SupportCaseShapeTests fails the build
/// if somewhere appears. A support case is somebody's customer's problem,
/// often described in their own words; this product needs to count and
/// categorise cases, not read them.
///
/// Stable key is (<see cref="TenantId"/>, <see cref="ConnectionKey"/>,
/// <see cref="ExternalTicketId"/>). The connection is in the key, not just
/// the tenant, because one tenant may connect two desks.
/// </summary>
public sealed record SupportCaseFact
{
    public required Guid CaseKey { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid ConnectionKey { get; init; }

    /// <summary>Stable provider name, e.g. "Freshdesk". Never localised.</summary>
    public required string Provider { get; init; }

    /// <summary>The desk account this came from — a Freshdesk domain. Part of what makes the key self-describing.</summary>
    public required string SourceAccountId { get; init; }

    public required string ExternalTicketId { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    public DateTime? ResolvedAtUtc { get; init; }

    public DateTime? ClosedAtUtc { get; init; }

    /// <summary>
    /// When the case was last reopened after being resolved. A reopen is
    /// the single most informative signal a desk gives about whether a fix
    /// actually worked, which is why it is a first-class field rather than
    /// something inferred from status history we do not have.
    /// </summary>
    public DateTime? ReopenedAtUtc { get; init; }

    public required SupportCaseState State { get; init; }

    /// <summary>The provider's own status string, kept so a mapping error is visible.</summary>
    public string? ProviderStatus { get; init; }

    public required SupportCasePriority Priority { get; init; }

    /// <summary>
    /// The product or component this case was tagged against, normalised
    /// against the tenant's approved list. Null when the desk gave no tag
    /// or gave one nobody has approved — see
    /// <see cref="RawComponentTag"/>, and the "unmapped components" figure
    /// on the service view.
    /// </summary>
    public string? ComponentKey { get; init; }

    /// <summary>What the desk actually said, before approval. Null when the desk said nothing.</summary>
    public string? RawComponentTag { get; init; }

    /// <summary>A coarse case type — "incident", "question", "request" — where the desk supplies one.</summary>
    public string? CaseType { get; init; }

    /// <summary>Link back to the case on the desk, so every number can be checked at source.</summary>
    public string? SourceUrl { get; init; }

    public required bool IsReopened { get; init; }

    /// <summary>Deleted or marked spam at the provider.</summary>
    public required bool IsWithdrawn { get; init; }

    public Guid? ObservedInRunKey { get; init; }

    public required int SchemaVersion { get; init; }

    public required DateTime FirstIngestedAtUtc { get; init; }

    public required DateTime IngestedAtUtc { get; init; }

    /// <summary>Counts towards service trends. Withdrawn cases never do.</summary>
    public bool CountsTowardsTrends => !IsWithdrawn;

    /// <summary>
    /// Time to restore service, when we have it. Deliberately measured to
    /// <see cref="ResolvedAtUtc"/> rather than <see cref="ClosedAtUtc"/> —
    /// closure is often an administrative sweep days later and would
    /// flatter or distort the figure.
    /// </summary>
    public TimeSpan? TimeToRestore =>
        ResolvedAtUtc is { } resolved && resolved >= CreatedAtUtc ? resolved - CreatedAtUtc : null;

    /// <summary>A case the desk gave no approved component for — visible, never guessed at.</summary>
    public bool HasUnmappedComponent => ComponentKey is null;
}

public static class SupportCaseSchema
{
    public const int CurrentVersion = 1;
}
