namespace ProgrammePulse.Models.ServiceOps;

/// <summary>One ServiceOps_AuditLog row. Mirrors the other feature areas' audit records rather than sharing one.</summary>
public sealed record ServiceOpsAuditLog
{
    public required Guid LogKey { get; init; }

    public required Guid TenantId { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public required string Action { get; init; }

    public int? ActorMemberId { get; init; }

    public string? DetailJson { get; init; }

    public required DateTime TimestampUtc { get; init; }
}

/// <summary>
/// Every audited action in Service Ops, as named in
/// docs/data-governance.md's audit-coverage table.
///
/// <see cref="RootCauseConfirmed"/> and <see cref="RootCauseRuledOut"/>
/// matter most. Confirming that a change caused a customer-affecting
/// case is the strongest claim this product can make, and the design
/// document requires it to be auditable — who decided, when, and on what
/// reasoning.
/// </summary>
public static class ServiceOpsAuditAction
{
    public const string ConnectionCreated = "DeskConnectionCreated";
    public const string ConnectionUpdated = "DeskConnectionUpdated";
    public const string ConnectionDisconnected = "DeskConnectionDisconnected";
    public const string ComponentsApproved = "DeskComponentsApproved";

    public const string SyncCompleted = "DeskSyncCompleted";
    public const string SyncFailed = "DeskSyncFailed";

    public const string LinkSuggested = "SupportLinkSuggested";
    public const string LinkCreated = "SupportLinkCreated";
    public const string LinkRemoved = "SupportLinkRemoved";
    public const string RootCauseConfirmed = "SupportRootCauseConfirmed";
    public const string RootCauseRuledOut = "SupportRootCauseRuledOut";

    public const string AgentLinkApproved = "DeskAgentLinkApproved";
    public const string AgentLinkRevoked = "DeskAgentLinkRevoked";

    public const string ParticipationDetachedForSubject = "ParticipationDetachedForSubject";

    public const string EntityTypeConnection = "DeskConnection";
    public const string EntityTypeCase = "SupportCase";
    public const string EntityTypeLink = "SupportCodeLink";
    public const string EntityTypeAgentLink = "DeskAgentLink";
    public const string EntityTypeStaff = "Staff";
}
