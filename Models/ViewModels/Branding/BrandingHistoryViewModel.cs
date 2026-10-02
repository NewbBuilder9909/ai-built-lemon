using ProgrammePulse.Models.Branding;

namespace ProgrammePulse.Models.ViewModels.Branding;

public sealed record BrandingHistoryViewModel(IReadOnlyList<BrandingHistoryRowViewModel> Versions);

public sealed record BrandingHistoryRowViewModel(
    Guid VersionKey,
    BrandingStatus Status,
    string CompanyName,
    string PrimaryColour,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    int? CreatedByMemberId);
