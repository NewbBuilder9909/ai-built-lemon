namespace ProgrammePulse.Models.ContractOps;

/// <summary>
/// A single non-labour cost entry (expense, PO, vendor invoice) against a
/// Contract — the minimal entity the ERP audit called for, deliberately not
/// a full purchase-order workflow. A cost in a different currency than the
/// contract is excluded from ContractCommercialService's totals and
/// surfaced separately, the same honesty pattern as TimeEntryCostCalculator
/// already applies to labour cost.
/// </summary>
public sealed record NonLabourCost
{
    public required Guid NonLabourCostKey { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid ContractKey { get; init; }

    public required string Description { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required DateOnly IncurredOn { get; init; }

    public Guid? RecordedByStaffKey { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}
