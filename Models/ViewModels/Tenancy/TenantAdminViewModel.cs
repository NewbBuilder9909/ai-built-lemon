using ProgrammePulse.Models.Tenancy;

namespace ProgrammePulse.Models.ViewModels.Tenancy;

public sealed record TenantAdminViewModel(
    IReadOnlyList<TenantAdminRowViewModel> Tenants,
    IReadOnlyList<string> Plans,
    IReadOnlyList<TenantStatus> Statuses,
    IReadOnlyList<TenantSellableModuleViewModel> SellableModules,
    PlatformSupportViewModel Support,
    string? Message);

public sealed record TenantAdminRowViewModel(
    Guid TenantKey,
    string Name,
    string ShortCode,
    TenantStatus Status,
    string Plan,
    DateTime? TrialEndsAtUtc,
    int ActiveStaffCount,
    int TotalStaffCount,
    int MissingTenantStaffCount,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> SelectedModules,
    decimal MonthlyPrice,
    decimal SetupFee,
    DateTime CreatedAtUtc,
    bool IsCallersOwnTenant);

public sealed record TenantSellableModuleViewModel(
    string FeatureKey,
    string DisplayName,
    decimal MonthlyPrice,
    decimal SetupFee);

/// <summary>
/// Platform-wide support signals. Sync is single-tenant this cycle (see
/// docs/tenancy.md), so these are global, not per tenant. Sources is the
/// same per-source publication state the Programme Overview header shows
/// (one source of truth: ProgrammeOps_SyncRun).
/// </summary>
public sealed record PlatformSupportViewModel(
    IReadOnlyList<ProgrammeOverview.SourcePublicationStateViewModel> Sources,
    int StaffWithoutTenantCount);
