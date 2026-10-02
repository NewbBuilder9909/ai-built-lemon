namespace ProgrammePulse.Models.ViewModels.Staff;

/// <summary>
/// Admin-only: includes CostPerHour/RateCurrency. Only ever built inside
/// StaffAdminController actions, after IStaffAuthorizationService.IsAdminAsync
/// has already been checked.
/// </summary>
public sealed record StaffAdminDetailViewModel(
    Guid StaffKey,
    string FullName,
    string Email,
    string? JobTitle,
    string? Department,
    string? Team,
    bool IsActive,
    decimal DefaultWorkHoursPerWeek,
    decimal? CurrentCostPerHour,
    string? CurrentRateCurrency,
    IReadOnlyList<StaffRateHistoryRowViewModel> RateHistory,
    IReadOnlyList<WorkHoursHistoryRowViewModel> WorkHoursHistory,
    StaffMfaStatusViewModel Mfa)
{
    /// <summary>The sign-in account behind the profile; null when the login has been deleted.</summary>
    public Services.Staff.MemberAccountState? Account { get; init; }

    /// <summary>The Admin is looking at their own record, so suspend, delete and dropping Admin are refused.</summary>
    public bool IsSelf { get; init; }
}

/// <summary>Null Enabled/RemainingRecoveryCodes means no MFA row exists at all — the member has never started enrollment.</summary>
public sealed record StaffMfaStatusViewModel(bool? Enabled, int RemainingRecoveryCodes);

public sealed record StaffRateHistoryRowViewModel(
    decimal CostPerHour,
    string RateCurrency,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    DateTime ChangedAtUtc);

public sealed record WorkHoursHistoryRowViewModel(
    decimal HoursPerWeek,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    DateTime ChangedAtUtc);
