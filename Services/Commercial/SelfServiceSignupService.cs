using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Services.Staff;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Cms.Web.Common.Security;

namespace ProgrammePulse.Services.Commercial;

/// <summary>
/// Public trial provisioning: create the tenant, attach the first Admin member,
/// and persist any selected sellable add-ons so the feature gate and platform
/// console read the same commercial state.
/// </summary>
public sealed partial class SelfServiceSignupService(
    MemberManager memberManager,
    ITenantRepository tenantRepository,
    ITenantFeatureSelectionRepository tenantFeatureSelectionRepository,
    IStaffRepository staffRepository,
    IWorkHoursHistoryRepository workHoursHistoryRepository,
    ICommercialCatalogueService commercialCatalogue,
    IOptions<CommercialOptions> commercialOptions,
    IScopeProvider scopeProvider,
    TimeProvider timeProvider) : ISelfServiceSignupService
{
    private const decimal DefaultWorkHoursPerWeek = 37.5m;

    public async Task<SelfServiceSignupResult> CreateTrialAsync(SelfServiceSignupRequest request)
    {
        var existingShortCodes = (await tenantRepository.GetAllAsync()).Select(t => t.ShortCode);
        var errors = Validate(request, existingShortCodes, commercialCatalogue.GetSelfServicePlans());
        if (errors.Count > 0)
        {
            return SelfServiceSignupResult.Failure(errors.ToArray());
        }

        var email = request.AdminEmail.Trim();
        if (await memberManager.FindByEmailAsync(email) is not null)
        {
            // Public endpoint: never confirm whether an address has an account.
            return SelfServiceSignupResult.Failure(StaffOnboardingService.UnusableEmail);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var tenantKey = Guid.NewGuid();
        var staffKey = Guid.NewGuid();
        var fullName = request.AdminFullName.Trim();
        var identityUser = MemberIdentityUser.CreateNew(email, email, "Member", true, fullName);
        var createResult = await memberManager.CreateAsync(identityUser, request.Password);
        if (!createResult.Succeeded)
        {
            return SelfServiceSignupResult.Failure(createResult.Errors.Select(e => e.Description).ToArray());
        }

        var roleResult = await memberManager.AddToRoleAsync(identityUser, StaffRole.Admin);
        if (!roleResult.Succeeded)
        {
            await memberManager.DeleteAsync(identityUser);
            return SelfServiceSignupResult.Failure(roleResult.Errors.Select(e => e.Description).ToArray());
        }

        var memberId = int.Parse(identityUser.Id);
        var tenant = new Tenant
        {
            TenantKey = tenantKey,
            Name = request.CompanyName.Trim(),
            ShortCode = request.ShortCode.Trim().ToLowerInvariant(),
            IsActive = true,
            Status = TenantStatus.Trial,
            Plan = TenantPlan.Normalize(request.Plan),
            TrialEndsAtUtc = now.AddDays(Math.Max(1, commercialOptions.Value.TrialDays)),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        try
        {
            await tenantRepository.CreateAsync(tenant);
            await staffRepository.CreateAsync(new StaffProfile
            {
                StaffKey = staffKey,
                MemberId = memberId,
                FullName = fullName,
                Email = email,
                DefaultWorkHoursPerWeek = DefaultWorkHoursPerWeek,
                TenantId = tenantKey,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            await workHoursHistoryRepository.SetCurrentHoursAsync(staffKey, DefaultWorkHoursPerWeek, staffKey);

            var selectedModules = commercialCatalogue.NormalizeSelfServiceModules(tenant.Plan, request.RequestedModules);
            await tenantFeatureSelectionRepository.ReplaceAsync(tenantKey, selectedModules, now);

            return SelfServiceSignupResult.Success(tenantKey);
        }
        catch
        {
            await CleanupProvisioningAsync(tenantKey, staffKey, identityUser);
            return SelfServiceSignupResult.Failure("We couldn't provision the trial workspace. Please try again or contact support.");
        }
    }

    internal static List<string> Validate(SelfServiceSignupRequest request, IEnumerable<string> existingShortCodes, IReadOnlyList<string> selfServicePlans)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.CompanyName))
        {
            errors.Add("Company name is required.");
        }
        else if (request.CompanyName.Trim().Length > 200)
        {
            errors.Add("Company name must be 200 characters or fewer.");
        }

        var code = request.ShortCode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!ShortCodePattern().IsMatch(code))
        {
            errors.Add("Short code must be 2–32 characters of lowercase letters, digits or hyphens.");
        }
        else if (existingShortCodes.Any(existing => string.Equals(existing, code, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Short code '{code}' is already in use.");
        }

        if (string.IsNullOrWhiteSpace(request.AdminFullName))
        {
            errors.Add("Admin full name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AdminEmail))
        {
            errors.Add("Admin email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors.Add("Password is required.");
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            errors.Add("Password confirmation must match.");
        }

        if (!selfServicePlans.Contains(request.Plan, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"'{request.Plan}' is not available for self-service signup.");
        }

        return errors;
    }

    [GeneratedRegex("^[a-z0-9-]{2,32}$")]
    private static partial Regex ShortCodePattern();

    private async Task CleanupProvisioningAsync(Guid tenantKey, Guid staffKey, MemberIdentityUser identityUser)
    {
        using var scope = scopeProvider.CreateScope();
        scope.Database.Execute($"DELETE FROM [{TenantFeatureSelectionDto.TableName}] WHERE [tenantId] = @0", tenantKey);
        scope.Database.Execute($"DELETE FROM [{WorkHoursHistoryDto.TableName}] WHERE [staffKey] = @0", staffKey);
        scope.Database.Execute($"DELETE FROM [{StaffDto.TableName}] WHERE [staffKey] = @0", staffKey);
        scope.Database.Execute($"DELETE FROM [{TenantDto.TableName}] WHERE [tenantKey] = @0", tenantKey);
        scope.Complete();

        await memberManager.DeleteAsync(identityUser);
    }
}
