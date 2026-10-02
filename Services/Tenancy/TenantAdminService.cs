using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.Tenancy;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.Tenancy;

/// <summary>The outcome of a platform console command: whether the tenant existed, and what to tell the operator.</summary>
public sealed record TenantAdminResult(bool Found, string Message);

/// <summary>
/// The Platform Admin's tenant lifecycle: list, create, change status, change
/// plan. Every change is audited. Moved out of StaffTenantAdminController.
///
/// Cross-tenant sync status is not built here. That read lives in
/// ProgrammeOps, which already depends on Tenancy, so the controller
/// composes the two rather than creating a cycle (FeatureAreaDependencyTests).
/// </summary>
public interface ITenantAdminService
{
    Task<(IReadOnlyList<TenantAdminRowViewModel> Rows, int StaffWithoutTenant)> BuildTenantRowsAsync(Guid? currentTenantId);

    Task<string> CreateAsync(string? name, string? shortCode, string? plan, TenantStatus status, int? trialDays, int? actorMemberId);

    Task<TenantAdminResult> SetStatusAsync(Guid tenantKey, TenantStatus status, int? trialDays, Guid? operatorsOwnTenantId, int? actorMemberId);

    Task<TenantAdminResult> SetPlanAsync(Guid tenantKey, string plan, int? actorMemberId);
}

public sealed partial class TenantAdminService(
    ITenantRepository tenantRepository,
    IStaffRepository staffRepository,
    IStaffAuditLogRepository staffAuditLogRepository,
    IOptions<EntitlementOptions> entitlementOptions,
    TimeProvider timeProvider) : ITenantAdminService
{
    public async Task<(IReadOnlyList<TenantAdminRowViewModel> Rows, int StaffWithoutTenant)> BuildTenantRowsAsync(Guid? currentTenantId)
    {
        var tenants = await tenantRepository.GetAllAsync();
        var allStaff = await staffRepository.GetAllAsync();
        var staffByTenant = allStaff.Where(s => s.TenantId is not null).ToLookup(s => s.TenantId!.Value);
        var knownTenantKeys = tenants.Select(t => t.TenantKey).ToHashSet();
        var staffWithoutTenant = allStaff.Count(s => s.TenantId is null || !knownTenantKeys.Contains(s.TenantId.Value));

        var rows = tenants.Select(t => new TenantAdminRowViewModel(
                t.TenantKey,
                t.Name,
                t.ShortCode,
                t.Status,
                t.Plan,
                t.TrialEndsAtUtc,
                staffByTenant[t.TenantKey].Count(s => s.IsActive),
                staffByTenant[t.TenantKey].Count(),
                0,
                PlanEntitlements.FeaturesFor(t.Plan, entitlementOptions.Value).OrderBy(f => f).ToList(),
                [],
                0m,
                0m,
                t.CreatedAtUtc,
                t.TenantKey == currentTenantId))
            .ToList();

        return (rows, staffWithoutTenant);
    }

    public async Task<string> CreateAsync(string? name, string? shortCode, string? plan, TenantStatus status, int? trialDays, int? actorMemberId)
    {
        var existing = await tenantRepository.GetAllAsync();
        var errors = ValidateNew(name, shortCode, plan, status, existing.Select(t => t.ShortCode));
        if (errors.Count > 0)
        {
            return string.Join(" ", errors);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var tenant = new Tenant
        {
            TenantKey = Guid.NewGuid(),
            Name = name!.Trim(),
            ShortCode = shortCode!.Trim().ToLowerInvariant(),
            IsActive = Tenant.IsUsableStatus(status),
            Status = status,
            Plan = TenantPlan.Normalize(plan!),
            TrialEndsAtUtc = status == TenantStatus.Trial && trialDays is > 0 ? now.AddDays(trialDays.Value) : null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await tenantRepository.CreateAsync(tenant);
        await LogAsync(tenant.TenantKey, "TenantCreated",
            new { name = tenant.Name, shortCode = tenant.ShortCode, plan = tenant.Plan, status = tenant.Status.ToString() }, actorMemberId);

        return $"Tenant '{tenant.Name}' created. Open its onboarding workspace to provision the customer administrator and guide setup.";
    }

    public async Task<TenantAdminResult> SetStatusAsync(Guid tenantKey, TenantStatus status, int? trialDays, Guid? operatorsOwnTenantId, int? actorMemberId)
    {
        var tenant = await tenantRepository.GetByKeyAsync(tenantKey);
        if (tenant is null)
        {
            return new TenantAdminResult(false, string.Empty);
        }

        if (tenantKey == operatorsOwnTenantId && !Tenant.IsUsableStatus(status))
        {
            // TenantAccessFilter would 403 this very console on the next
            // request — a platform admin can't lock themselves out.
            return new TenantAdminResult(true, "You can't suspend or archive the tenant your own account belongs to.");
        }

        if (tenant.Status == TenantStatus.Archived && status != TenantStatus.Archived)
        {
            return new TenantAdminResult(true, "An archived tenant can't be reactivated from here — that's a deliberate data-lifecycle decision (docs/data-governance.md).");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var updated = tenant with
        {
            Status = status,
            IsActive = Tenant.IsUsableStatus(status),
            TrialEndsAtUtc = status == TenantStatus.Trial
                ? (trialDays is > 0 ? now.AddDays(trialDays.Value) : tenant.TrialEndsAtUtc)
                : null,
            UpdatedAtUtc = now
        };

        await tenantRepository.UpdateAsync(updated);
        await LogAsync(tenantKey, "TenantStatusChanged", new { from = tenant.Status.ToString(), to = status.ToString() }, actorMemberId);

        return new TenantAdminResult(true, $"'{tenant.Name}' is now {status}.");
    }

    public async Task<TenantAdminResult> SetPlanAsync(Guid tenantKey, string plan, int? actorMemberId)
    {
        if (!TenantPlan.IsKnown(plan))
        {
            return new TenantAdminResult(true, $"'{plan}' is not a known plan.");
        }

        var tenant = await tenantRepository.GetByKeyAsync(tenantKey);
        if (tenant is null)
        {
            return new TenantAdminResult(false, string.Empty);
        }

        var normalized = TenantPlan.Normalize(plan);
        await tenantRepository.UpdateAsync(tenant with { Plan = normalized, UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime });
        await LogAsync(tenantKey, "TenantPlanChanged", new { from = tenant.Plan, to = normalized }, actorMemberId);

        return new TenantAdminResult(true, $"'{tenant.Name}' moved to the {normalized} plan.");
    }

    /// <summary>
    /// Pure validation for a new tenant, testable without a database — same
    /// seam as StaffOnboardingService.Validate.
    /// </summary>
    internal static List<string> ValidateNew(string? name, string? shortCode, string? plan, TenantStatus status, IEnumerable<string> existingShortCodes)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Name is required.");
        }
        else if (name.Trim().Length > 200)
        {
            errors.Add("Name must be 200 characters or fewer.");
        }

        var code = shortCode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!ShortCodePattern().IsMatch(code))
        {
            errors.Add("Short code must be 2–32 characters of lowercase letters, digits or hyphens.");
        }
        else if (existingShortCodes.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Short code '{code}' is already in use.");
        }

        if (!TenantPlan.IsKnown(plan))
        {
            errors.Add($"'{plan}' is not a known plan.");
        }

        if (!Tenant.IsUsableStatus(status))
        {
            errors.Add("A new tenant must start as Trial or Active.");
        }

        return errors;
    }

    [GeneratedRegex("^[a-z0-9-]{2,32}$")]
    private static partial Regex ShortCodePattern();

    private Task LogAsync(Guid tenantKey, string action, object detail, int? actorMemberId) =>
        staffAuditLogRepository.LogAsync("Tenant", tenantKey.ToString(), action, actorMemberId, JsonSerializer.Serialize(detail), timeProvider.GetUtcNow().UtcDateTime, tenantKey);
}
