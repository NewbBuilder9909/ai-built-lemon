namespace ProgrammePulse.Models.ViewModels.ProgrammeOverview;

/// <summary>
/// The identity queue page (/staffops/programme/identities): unmatched
/// people from the source tools, the explicit links already approved, and
/// the active staff an Admin can link them to.
/// </summary>
public sealed record IdentityQueueViewModel(
    IReadOnlyList<UnresolvedIdentityRowViewModel> Unresolved,
    IReadOnlyList<IdentityLinkRowViewModel> Links,
    IReadOnlyList<StaffOptionViewModel> StaffOptions,
    string? Message,
    ProgrammePulse.Services.Shared.PageLinks UnresolvedPages);

public sealed record UnresolvedIdentityRowViewModel(
    Guid UnresolvedIdentityKey,
    string ExternalSource,
    string? ExternalUserId,
    string? Email,
    string? DisplayName,
    string Context,
    int OccurrenceCount,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    Guid? SuggestedStaffKey = null,
    string? SuggestedStaffName = null);

public sealed record IdentityLinkRowViewModel(
    Guid LinkKey,
    string ExternalSource,
    string? ExternalUserId,
    string? Email,
    string StaffFullName,
    DateTime CreatedAtUtc);

public sealed record StaffOptionViewModel(Guid StaffKey, string FullName, string Email);
