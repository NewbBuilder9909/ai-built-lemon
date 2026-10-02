namespace ProgrammePulse.Models.Programme;

/// <summary>
/// Top of the canonical Programme/Project/Workstream/WorkItem hierarchy.
/// Source-agnostic Silver model — ExternalId/ExternalSource identify which
/// ingestion source (ClickUp today) this row was mapped from, but nothing
/// here is ClickUp-shaped.
/// </summary>
public sealed record Programme
{
    public required Guid ProgrammeKey { get; init; }

    public Guid? TenantId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Optional, admin-assigned — see <see cref="Customer"/>.</summary>
    public Guid? CustomerKey { get; init; }

    /// <summary>
    /// Optional, admin-set — the budget-vs-actual figure for a programme
    /// with no formal Contract record (Contract Ops' burn-down already
    /// covers programmes that do have one). Null means "no budget set", not
    /// zero — the Cost Summary page must not compute a burn-% until one is.
    /// </summary>
    public decimal? BudgetAmount { get; init; }

    public string? BudgetCurrency { get; init; }

    public string? ExternalSource { get; init; }

    public string? ExternalId { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }
}
