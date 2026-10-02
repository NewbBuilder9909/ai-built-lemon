namespace ProgrammePulse.Models.ViewModels.Staff;

/// <summary>One row on /staffops/admin/audit — a StaffOps or ProgrammeOps audit entry with the actor resolved to a display name.</summary>
public sealed record AuditEntryRowViewModel(
    string Source,
    string EntityType,
    string EntityId,
    string Action,
    string Actor,
    string? DetailJson,
    DateTime TimestampUtc);

/// <summary>
/// One page of /staffops/admin/audit. The trail is four separately stored
/// logs merged newest first, so each page reads the top rows of all four;
/// paging therefore stops at <paramref name="DepthLimit"/> rows, and
/// <paramref name="OlderEntriesNotShown"/> says when that hid anything.
/// </summary>
public sealed record AuditTrailPageViewModel(
    ProgrammePulse.Services.Shared.ResultPage<AuditEntryRowViewModel> Entries,
    bool OlderEntriesNotShown,
    int DepthLimit);
