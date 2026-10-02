using System.ComponentModel.DataAnnotations;

namespace ProgrammePulse.Models.ExecutiveReview;

public enum ExecutiveDataOperation { RedactDecision, WithdrawPack, ApplyRetention, PurgeTenantData }
public enum ExecutiveDataReason { PersonalData, IncorrectEvidence, RetentionPolicy, TenantOffboarding }

public sealed class ExecutiveDataRequest
{
    [Required] public ExecutiveDataOperation? Operation { get; set; }
    public Guid? TargetKey { get; set; }
    [Required] public ExecutiveDataReason? Reason { get; set; }
}

public sealed record ExecutiveDataCounts(int Decisions, int DecisionVersions, int Packs, int MarketVersions, int AuditEvents);
public sealed record ExecutiveDataPreview(ExecutiveDataRequest Request, ExecutiveDataCounts Counts,
    DateTime? RetentionCutoffUtc, string ConfirmationToken);
public sealed record ExecutiveDataReceipt(Guid OperationKey, ExecutiveDataOperation Operation,
    ExecutiveDataCounts Counts, DateTime CompletedAtUtc);
public sealed record ExecutiveDataEvent(Guid OperationKey, string Operation, Guid? TargetKey, string Reason,
    int? ActorMemberId, ExecutiveDataCounts Counts, DateTime RecordedAtUtc);
public sealed record ExecutiveDataPage(int RetentionDays, IReadOnlyList<ExecutiveDataEvent> Events,
    ExecutiveDataPreview? Preview = null, string? ErrorKey = null, ExecutiveDataReceipt? Receipt = null,
    ExecutiveDataRequest? Input = null);

public sealed class ExecutiveDataOptions
{
    public const string SectionName = "ExecutiveReview:DataLifecycle";
    // Zero disables retention until the operator agrees a retention period with the tenant.
    [Range(0, 3650)] public int RetentionDays { get; set; }
}
