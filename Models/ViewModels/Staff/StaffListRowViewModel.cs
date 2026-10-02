namespace ProgrammePulse.Models.ViewModels.Staff;

/// <summary>
/// No cost fields — used by Team Lead/Approver-facing views. See
/// StaffAdminDetailViewModel for the admin-only equivalent with rates.
/// </summary>
public sealed record StaffListRowViewModel(
    Guid StaffKey,
    string FullName,
    string? JobTitle,
    string? Department,
    string? Team,
    bool IsActive);
