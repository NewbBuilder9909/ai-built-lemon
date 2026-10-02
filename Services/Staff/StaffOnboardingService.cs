using ProgrammePulse.Models.Staff;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Security;

namespace ProgrammePulse.Services.Staff;

/// <summary>
/// Creates a new Umbraco Member (login identity), assigns it a StaffRole
/// group, and creates the linked StaffOps_Staff row — the piece that was
/// entirely missing before: IStaffRepository.CreateAsync only ever inserted
/// against an already-existing MemberId. No email/notification service
/// exists in this codebase, so the Admin sets the initial password directly
/// on the form rather than this service generating one.
///
/// The new profile is stamped with the tenant the caller passes, which the
/// controller takes from its [CurrentTenant] parameter, so an Admin can only
/// ever onboard people into their own organisation. There is deliberately
/// no fallback: this used to read ITenantContext and default to the platform
/// tenant when unresolved, which was fail-open. It also made Staff depend on
/// Tenancy while Tenancy depends on Staff.
/// </summary>
public sealed class StaffOnboardingService(
    MemberManager memberManager,
    IStaffRepository staffRepository,
    IWorkHoursHistoryRepository workHoursHistoryRepository,
    TimeProvider timeProvider) : IStaffOnboardingService
{
    /// <summary>Shown when the email can't be used, without saying whether an account exists.</summary>
    public const string UnusableEmail = "That email address can't be used for a new account. Check it, or use a different address.";

    public async Task<StaffOnboardingResult> CreateStaffAsync(StaffOnboardingRequest request, Guid tenantId)
    {
        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return StaffOnboardingResult.Failure(validationErrors.ToArray());
        }

        var email = request.Email.Trim();
        var existingMember = await memberManager.FindByEmailAsync(email);
        if (existingMember is not null)
        {
            // Member emails are unique across every tenant, so saying "already exists"
            // would tell one organisation's Admin that someone elsewhere has an account.
            return StaffOnboardingResult.Failure(UnusableEmail);
        }

        var fullName = request.FullName.Trim();
        var identityUser = MemberIdentityUser.CreateNew(email, email, "Member", true, fullName);
        var createResult = await memberManager.CreateAsync(identityUser, request.Password);
        if (!createResult.Succeeded)
        {
            return StaffOnboardingResult.Failure(createResult.Errors.Select(e => e.Description).ToArray());
        }

        var roleResult = await memberManager.AddToRoleAsync(identityUser, request.Role);
        if (!roleResult.Succeeded)
        {
            return StaffOnboardingResult.Failure(roleResult.Errors.Select(e => e.Description).ToArray());
        }

        var memberId = int.Parse(identityUser.Id);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var staffKey = Guid.NewGuid();

        await staffRepository.CreateAsync(new StaffProfile
        {
            StaffKey = staffKey,
            MemberId = memberId,
            FullName = fullName,
            Email = email,
            JobTitle = NormalizeOptional(request.JobTitle),
            Department = NormalizeOptional(request.Department),
            Team = NormalizeOptional(request.Team),
            DefaultWorkHoursPerWeek = request.DefaultWorkHoursPerWeek,
            TenantId = tenantId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        // Seeds the history WorkHoursHistoryRepository needs to answer "what
        // were this person's hours on day X" from day one — without this,
        // ReportingQueryService's history lookup would fall back to
        // StaffProfile.DefaultWorkHoursPerWeek (today's value) for every day
        // before the first SetWorkHours change, silently wrong the moment
        // that value is ever edited. ChangedByStaffKey is the new person's
        // own key here — there's no admin actor identity threaded into this
        // service today (StaffAdminController.LogAsync resolves the current
        // member for the audit log entry independently, after this returns),
        // and WorkHoursHistory mirrors StaffRate's non-nullable
        // ChangedByStaffKey rather than diverging just for this seed row.
        await workHoursHistoryRepository.SetCurrentHoursAsync(staffKey, request.DefaultWorkHoursPerWeek, staffKey);

        return StaffOnboardingResult.Success(staffKey);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Pure validation, factored out so it's exercisable without a live
    /// MemberManager (a concrete Identity UserManager subclass this repo
    /// can't fake) — see StaffOnboardingServiceTests for what that seam
    /// buys and what it deliberately leaves to the live-run walkthrough.
    /// </summary>
    internal static List<string> Validate(StaffOnboardingRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            errors.Add("Full name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors.Add("Email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors.Add("Password is required.");
        }

        if (!StaffRole.All.Contains(request.Role))
        {
            errors.Add($"'{request.Role}' is not a valid role.");
        }

        if (request.DefaultWorkHoursPerWeek <= 0)
        {
            errors.Add("Default work hours per week must be greater than zero.");
        }

        return errors;
    }
}
