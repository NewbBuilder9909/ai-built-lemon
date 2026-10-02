namespace ProgrammePulse.Models.Programme;

/// <summary>
/// An optional grouping above Programme, used only for "work by customer"
/// reporting. Deliberately not ClickUp-sourced — ClickUp Spaces map 1:1 to
/// Programme, and nothing in ClickUp's data model represents a customer, so
/// this is admin-authored (create/rename, assign a Programme to one) rather
/// than synced. Existing Programmes stay unassigned until an admin opts them
/// in; reporting shows "(no customer)" for those.
/// </summary>
public sealed record Customer
{
    public required Guid CustomerKey { get; init; }

    public Guid? TenantId { get; init; }

    public required string Name { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }
}
