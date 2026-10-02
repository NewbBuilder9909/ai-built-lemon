using System.Text.Json;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.Staff;

namespace ProgrammePulse.Services.Staff;

/// <summary>What happened to a staff admin command.</summary>
public enum StaffAdminStatus
{
    Done,

    /// <summary>The staff key is not a person in the caller's tenant.</summary>
    NotFound,

    /// <summary>The input was refused; see the message.</summary>
    Invalid,

    /// <summary>The acting Admin has no staff profile to record the change against.</summary>
    NoActingProfile
}

public sealed record StaffAdminResult(StaffAdminStatus Status, string? Message = null)
{
    public static StaffAdminResult Done { get; } = new(StaffAdminStatus.Done);

    public static StaffAdminResult NotFound { get; } = new(StaffAdminStatus.NotFound);
}

/// <summary>
/// Staff administration: roster, detail (with cost fields), onboarding,
/// weekly hours, cost rates, MFA reset and GDPR export/erasure. Every
/// command first proves the target staff key belongs to the caller's tenant
/// (NotFound otherwise, so a guessed key is indistinguishable from a
/// missing one), and every change is audited. Moved out of
/// StaffAdminController.
///
/// Cost rates are only ever read on this path, which is Admin-only
/// (ManageStaff). See CLAUDE.md, "Cost/rate data isolation is structural".
/// </summary>
public interface IStaffAdminService
{
    Task<IReadOnlyList<StaffListRowViewModel>> BuildRosterAsync(Guid tenantId);

    Task<StaffOnboardingResult> OnboardAsync(Guid tenantId, StaffOnboardingRequest request, int? actorMemberId);

    Task<StaffAdminDetailViewModel?> BuildDetailAsync(Guid tenantId, Guid staffKey);

    Task<StaffAdminResult> SetWorkHoursAsync(Guid tenantId, Guid staffKey, decimal hoursPerWeek, StaffProfile? changedBy, int? actorMemberId);

    Task<StaffAdminResult> SetRateAsync(Guid tenantId, Guid staffKey, decimal costPerHour, string rateCurrency, StaffProfile? changedBy, int? actorMemberId);

    Task<StaffAdminResult> ResetMfaAsync(Guid tenantId, Guid staffKey, int? actorMemberId);

    /// <summary>Null when the person is not in this tenant. The export itself is audited.</summary>
    Task<GdprExport?> ExportAsync(Guid tenantId, Guid staffKey, int? actorMemberId);

    Task<StaffAdminResult> EraseAsync(Guid tenantId, Guid staffKey, int? actorMemberId);

    /// <summary>Replaces the person's roles. At least one; never the caller's own Admin role or the last active Admin's.</summary>
    Task<StaffAdminResult> SetRolesAsync(Guid tenantId, Guid staffKey, IReadOnlyCollection<string> roles, int? actorMemberId);

    /// <summary>Suspends (false) or restores (true) sign-in and every page. Never the caller, or the last active Admin.</summary>
    Task<StaffAdminResult> SetAccessAsync(Guid tenantId, Guid staffKey, bool active, int? actorMemberId);

    /// <summary>Clears a lockout from repeated wrong passwords.</summary>
    Task<StaffAdminResult> UnlockAsync(Guid tenantId, Guid staffKey, int? actorMemberId);

    /// <summary>A one-time reset token for the person's login. The Admin passes the link on; nobody sees a password.</summary>
    Task<PasswordResetIssue?> IssuePasswordResetAsync(Guid tenantId, Guid staffKey, int? actorMemberId);

    /// <summary>
    /// Deletes the person: erases their personal data (as <see cref="EraseAsync"/>) and deletes their login.
    /// History stays, under a placeholder name. Never the caller, or the last active Admin.
    /// </summary>
    Task<StaffAdminResult> DeleteAsync(Guid tenantId, Guid staffKey, int? actorMemberId);

    /// <summary>The detail page, marking whether the Admin is looking at their own record.</summary>
    Task<StaffAdminDetailViewModel?> BuildDetailAsync(Guid tenantId, Guid staffKey, int? actorMemberId);
}

/// <summary>A reset token for one member's login, to be put in a link.</summary>
public sealed record PasswordResetIssue(int MemberId, string FullName, string Token);

public sealed class StaffAdminService(
    IStaffRepository staffRepository,
    IStaffRateRepository staffRateRepository,
    IWorkHoursHistoryRepository workHoursHistoryRepository,
    IGdprService gdprService,
    IStaffOnboardingService staffOnboardingService,
    IStaffAuditLogRepository staffAuditLogRepository,
    IStaffMfaAdministration mfaAdministration,
    IMemberAccountAdministration accounts,
    TimeProvider timeProvider) : IStaffAdminService
{
    public const string HoursMustBePositive = "Weekly hours must be greater than zero.";
    public const string NotYourself = "You can't do that to your own account. Ask another Admin.";
    public const string LastAdmin = "This is the organisation's last active Admin. Make someone else an Admin first.";
    public const string NoRoles = "Choose at least one role.";
    public const string LoginDeleted = "This person's login has been deleted.";

    public async Task<IReadOnlyList<StaffListRowViewModel>> BuildRosterAsync(Guid tenantId) =>
        (await staffRepository.GetByTenantAsync(tenantId))
            .Select(s => new StaffListRowViewModel(s.StaffKey, s.FullName, s.JobTitle, s.Department, s.Team, s.IsActive))
            .ToList();

    public async Task<StaffOnboardingResult> OnboardAsync(Guid tenantId, StaffOnboardingRequest request, int? actorMemberId)
    {
        var result = await staffOnboardingService.CreateStaffAsync(request, tenantId);
        if (result.Succeeded)
        {
            await LogAsync("Staff", result.StaffKey!.Value, "Created", new { role = request.Role, email = request.Email.Trim() }, actorMemberId, tenantId);
        }

        return result;
    }

    public Task<StaffAdminDetailViewModel?> BuildDetailAsync(Guid tenantId, Guid staffKey) =>
        BuildDetailAsync(tenantId, staffKey, actorMemberId: null);

    public async Task<StaffAdminDetailViewModel?> BuildDetailAsync(Guid tenantId, Guid staffKey, int? actorMemberId)
    {
        var staff = await GetTenantStaffAsync(staffKey, tenantId);
        if (staff is null)
        {
            return null;
        }

        var current = await staffRateRepository.GetCurrentAsync(staffKey);
        var history = await staffRateRepository.GetHistoryAsync(staffKey);
        var workHoursHistory = await workHoursHistoryRepository.GetHistoryAsync(staffKey);
        var mfa = await mfaAdministration.GetStatusAsync(staff.MemberId);

        return new StaffAdminDetailViewModel(
            staff.StaffKey,
            staff.FullName,
            staff.Email,
            staff.JobTitle,
            staff.Department,
            staff.Team,
            staff.IsActive,
            staff.DefaultWorkHoursPerWeek,
            current?.CostPerHour,
            current?.RateCurrency,
            history.Select(r => new StaffRateHistoryRowViewModel(r.CostPerHour, r.RateCurrency, r.EffectiveFromUtc, r.EffectiveToUtc, r.ChangedAtUtc)).ToList(),
            workHoursHistory.Select(h => new WorkHoursHistoryRowViewModel(h.HoursPerWeek, h.EffectiveFromUtc, h.EffectiveToUtc, h.ChangedAtUtc)).ToList(),
            mfa)
        {
            Account = await accounts.GetStateAsync(staff.MemberId),
            IsSelf = actorMemberId == staff.MemberId
        };
    }

    public async Task<StaffAdminResult> SetWorkHoursAsync(Guid tenantId, Guid staffKey, decimal hoursPerWeek, StaffProfile? changedBy, int? actorMemberId)
    {
        if (await GetTenantStaffAsync(staffKey, tenantId) is null)
        {
            return StaffAdminResult.NotFound;
        }

        if (hoursPerWeek <= 0)
        {
            return new StaffAdminResult(StaffAdminStatus.Invalid, HoursMustBePositive);
        }

        if (changedBy is null)
        {
            return new StaffAdminResult(StaffAdminStatus.NoActingProfile);
        }

        // Both updated together — DefaultWorkHoursPerWeek is the "current"
        // convenience value forward-looking views read directly, while
        // WorkHoursHistory is what a past-period report reads instead
        // (see WorkHoursHistory's doc comment). Letting them drift apart
        // would defeat the point of adding the history at all.
        await staffRepository.UpdateDefaultWorkHoursAsync(staffKey, hoursPerWeek, timeProvider.GetUtcNow().UtcDateTime);
        await workHoursHistoryRepository.SetCurrentHoursAsync(staffKey, hoursPerWeek, changedBy.StaffKey);

        await LogAsync("Staff", staffKey, "WorkHoursChanged", new { defaultWorkHoursPerWeek = hoursPerWeek }, actorMemberId, tenantId);
        return StaffAdminResult.Done;
    }

    public async Task<StaffAdminResult> SetRateAsync(Guid tenantId, Guid staffKey, decimal costPerHour, string rateCurrency, StaffProfile? changedBy, int? actorMemberId)
    {
        if (await GetTenantStaffAsync(staffKey, tenantId) is null)
        {
            return StaffAdminResult.NotFound;
        }

        if (changedBy is null)
        {
            return new StaffAdminResult(StaffAdminStatus.NoActingProfile);
        }

        await staffRateRepository.SetCurrentRateAsync(staffKey, costPerHour, rateCurrency, changedBy.StaffKey);
        await LogAsync("StaffRate", staffKey, "RateChanged", new { costPerHour, currency = rateCurrency }, actorMemberId, tenantId);
        return StaffAdminResult.Done;
    }

    public async Task<StaffAdminResult> ResetMfaAsync(Guid tenantId, Guid staffKey, int? actorMemberId)
    {
        var staff = await GetTenantStaffAsync(staffKey, tenantId);
        if (staff is null)
        {
            return StaffAdminResult.NotFound;
        }

        await mfaAdministration.ResetAsync(staff.MemberId);
        await LogAsync("StaffMfa", staffKey, "MfaReset", null, actorMemberId, tenantId);
        return StaffAdminResult.Done;
    }

    public async Task<GdprExport?> ExportAsync(Guid tenantId, Guid staffKey, int? actorMemberId)
    {
        if (await GetTenantStaffAsync(staffKey, tenantId) is null)
        {
            return null;
        }

        var export = await gdprService.BuildExportAsync(staffKey, tenantId);
        if (export is not null)
        {
            await LogAsync("Staff", staffKey, "GdprExported", null, actorMemberId, tenantId);
        }

        return export;
    }

    public async Task<StaffAdminResult> EraseAsync(Guid tenantId, Guid staffKey, int? actorMemberId)
    {
        if (await GetTenantStaffAsync(staffKey, tenantId) is null)
        {
            return StaffAdminResult.NotFound;
        }

        await gdprService.EraseAsync(staffKey);
        await LogAsync("Staff", staffKey, "GdprErased", null, actorMemberId, tenantId);
        return StaffAdminResult.Done;
    }

    public async Task<StaffAdminResult> SetRolesAsync(Guid tenantId, Guid staffKey, IReadOnlyCollection<string> roles, int? actorMemberId)
    {
        var staff = await GetTenantStaffAsync(staffKey, tenantId);
        if (staff is null)
        {
            return StaffAdminResult.NotFound;
        }

        var wanted = roles.Where(StaffRole.All.Contains).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return Invalid(NoRoles);
        }

        var state = await accounts.GetStateAsync(staff.MemberId);
        if (state is null)
        {
            return Invalid(LoginDeleted);
        }

        var dropsAdmin = state.Roles.Contains(StaffRole.Admin) && !wanted.Contains(StaffRole.Admin);
        if (dropsAdmin && staff.MemberId == actorMemberId)
        {
            return Invalid(NotYourself);
        }

        if (dropsAdmin && staff.IsActive && await IsLastActiveAdminAsync(tenantId, staff))
        {
            return Invalid(LastAdmin);
        }

        var saved = await accounts.SetRolesAsync(staff.MemberId, wanted);
        await LogAsync("Staff", staffKey, "RolesChanged", new { from = state.Roles, to = saved }, actorMemberId, tenantId);
        return StaffAdminResult.Done;
    }

    public async Task<StaffAdminResult> SetAccessAsync(Guid tenantId, Guid staffKey, bool active, int? actorMemberId)
    {
        var staff = await GetTenantStaffAsync(staffKey, tenantId);
        if (staff is null)
        {
            return StaffAdminResult.NotFound;
        }

        if (!active && staff.MemberId == actorMemberId)
        {
            return Invalid(NotYourself);
        }

        if (!active && staff.IsActive && await IsLastActiveAdminAsync(tenantId, staff))
        {
            return Invalid(LastAdmin);
        }

        if (await accounts.GetStateAsync(staff.MemberId) is null)
        {
            return Invalid(LoginDeleted);
        }

        await staffRepository.SetActiveAsync(staffKey, tenantId, active, timeProvider.GetUtcNow().UtcDateTime);
        await accounts.SetApprovedAsync(staff.MemberId, active);
        await LogAsync("Staff", staffKey, active ? "AccessRestored" : "AccessSuspended", null, actorMemberId, tenantId);
        return StaffAdminResult.Done;
    }

    public async Task<StaffAdminResult> UnlockAsync(Guid tenantId, Guid staffKey, int? actorMemberId)
    {
        var staff = await GetTenantStaffAsync(staffKey, tenantId);
        if (staff is null)
        {
            return StaffAdminResult.NotFound;
        }

        if (await accounts.GetStateAsync(staff.MemberId) is null)
        {
            return Invalid(LoginDeleted);
        }

        await accounts.UnlockAsync(staff.MemberId);
        await LogAsync("Staff", staffKey, "Unlocked", null, actorMemberId, tenantId);
        return StaffAdminResult.Done;
    }

    public async Task<PasswordResetIssue?> IssuePasswordResetAsync(Guid tenantId, Guid staffKey, int? actorMemberId)
    {
        var staff = await GetTenantStaffAsync(staffKey, tenantId);
        if (staff is null || !staff.IsActive)
        {
            return null;
        }

        var token = await accounts.CreatePasswordResetTokenAsync(staff.MemberId);
        if (token is null)
        {
            return null;
        }

        await LogAsync("Staff", staffKey, "PasswordResetIssued", null, actorMemberId, tenantId);
        return new PasswordResetIssue(staff.MemberId, staff.FullName, token);
    }

    public async Task<StaffAdminResult> DeleteAsync(Guid tenantId, Guid staffKey, int? actorMemberId)
    {
        var staff = await GetTenantStaffAsync(staffKey, tenantId);
        if (staff is null)
        {
            return StaffAdminResult.NotFound;
        }

        if (staff.MemberId == actorMemberId)
        {
            return Invalid(NotYourself);
        }

        if (staff.IsActive && await IsLastActiveAdminAsync(tenantId, staff))
        {
            return Invalid(LastAdmin);
        }

        await gdprService.EraseAsync(staffKey);
        await accounts.DeleteAsync(staff.MemberId);
        await LogAsync("Staff", staffKey, "Deleted", null, actorMemberId, tenantId);
        return StaffAdminResult.Done;
    }

    /// <summary>Whether <paramref name="staff"/> is an Admin and no other active person in the tenant is.</summary>
    private async Task<bool> IsLastActiveAdminAsync(Guid tenantId, StaffProfile staff)
    {
        var admins = await accounts.GetAdminMemberIdsAsync();
        if (!admins.Contains(staff.MemberId))
        {
            return false;
        }

        var roster = await staffRepository.GetByTenantAsync(tenantId);
        return !roster.Any(s => s.IsActive && s.StaffKey != staff.StaffKey && admins.Contains(s.MemberId));
    }

    private static StaffAdminResult Invalid(string message) => new(StaffAdminStatus.Invalid, message);

    private async Task<StaffProfile?> GetTenantStaffAsync(Guid staffKey, Guid tenantId)
    {
        var staff = await staffRepository.GetByStaffKeyAsync(staffKey);
        return staff is not null && staff.TenantId == tenantId ? staff : null;
    }

    private Task LogAsync(string entityType, Guid staffKey, string action, object? detail, int? actorMemberId, Guid tenantId) =>
        staffAuditLogRepository.LogAsync(entityType, staffKey.ToString(), action, actorMemberId,
            detail is null ? null : JsonSerializer.Serialize(detail), timeProvider.GetUtcNow().UtcDateTime, tenantId);
}
