namespace ProgrammePulse.Models.ContractOps;

/// <summary>
/// Commercial terms for a customer engagement. Admin-only, structurally —
/// the same principle as StaffOps_StaffRate: nothing outside
/// Services/ContractOps and StaffContractController should ever query this.
/// Terms are set once at creation; only Status and Notes change afterward.
/// A real term change is a new ContractDocument (Amendment), not a data
/// mutation — keeps the burn-down historically honest.
/// </summary>
public sealed record Contract
{
    public required Guid ContractKey { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid CustomerKey { get; init; }

    public required string Reference { get; init; }

    public required CommercialModel CommercialModel { get; init; }

    /// <summary>Used by <see cref="ContractOps.CommercialModel.FixedPrice"/>.</summary>
    public decimal? TotalContractValue { get; init; }

    /// <summary>Used by <see cref="ContractOps.CommercialModel.Ongoing"/>.</summary>
    public decimal? AnnualValue { get; init; }

    /// <summary>
    /// Optional blended client bill rate for TimeAndMaterials revenue.
    /// Left null means revenue is reported as "not set", never guessed.
    /// </summary>
    public decimal? BillRate { get; init; }

    public required string Currency { get; init; }

    public required DateOnly StartDate { get; init; }

    public required DateOnly EndDate { get; init; }

    public required ContractStatus Status { get; init; }

    public string? Notes { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }
}
