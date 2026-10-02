namespace ProgrammePulse.Models.ViewModels.Staff;

/// <summary>
/// Own-work view for StaffPortalController's "my-work" action — assigned
/// items and own logged hours only, no cost/rate data and no visibility into
/// other staff's work.
/// </summary>
public sealed record MyWorkViewModel(
    IReadOnlyList<MyWorkItemRowViewModel> Items,
    decimal TotalLoggedHours);

/// <param name="IsOverdue">Open and past its due date — the same rule as the Programme Overview's Overdue count.</param>
public sealed record MyWorkItemRowViewModel(
    string Title,
    string StageLabel,
    DateTime? DueDateUtc,
    decimal? EstimatedHours,
    decimal LoggedHours,
    bool IsOverdue = false,
    bool IsClosed = false);
