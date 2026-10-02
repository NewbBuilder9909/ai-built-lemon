using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.Staff;

/// <summary>
/// StaffOnboardingService's Member-creation half depends on the concrete
/// MemberManager (a UserManager&lt;MemberIdentityUser&gt; subclass this repo
/// can't fake without a mocking framework — same constraint every other
/// MemberManager-touching path here already lives with, e.g.
/// StaffAccountController's login/MFA flow has no unit tests either). These
/// tests cover the one part that's pure and doesn't need a live Member store:
/// StaffOnboardingService.Validate. The rest is covered by the live-run
/// regression walkthrough instead.
/// </summary>
public class StaffOnboardingServiceTests
{
    private static StaffOnboardingRequest MakeRequest(
        string fullName = "Jamie Gardner",
        string email = "jamie@example.com",
        string password = "Correct-Horse-1",
        string role = StaffRole.Staff,
        decimal defaultWorkHoursPerWeek = 37.5m) =>
        new(fullName, email, password, role, "Producer", "Drama", "Drama", defaultWorkHoursPerWeek);

    [Fact]
    public void Validate_accepts_a_well_formed_request()
    {
        var errors = StaffOnboardingService.Validate(MakeRequest());

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_rejects_a_missing_full_name(string fullName)
    {
        var errors = StaffOnboardingService.Validate(MakeRequest(fullName: fullName));

        Assert.Contains(errors, e => e.Contains("Full name"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_rejects_a_missing_email(string email)
    {
        var errors = StaffOnboardingService.Validate(MakeRequest(email: email));

        Assert.Contains(errors, e => e.Contains("Email"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_rejects_a_missing_password(string password)
    {
        var errors = StaffOnboardingService.Validate(MakeRequest(password: password));

        Assert.Contains(errors, e => e.Contains("Password"));
    }

    [Fact]
    public void Validate_rejects_a_role_that_is_not_one_of_the_four_staff_roles()
    {
        var errors = StaffOnboardingService.Validate(MakeRequest(role: "Backoffice Admin"));

        Assert.Contains(errors, e => e.Contains("not a valid role"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_rejects_non_positive_default_work_hours(decimal hours)
    {
        var errors = StaffOnboardingService.Validate(MakeRequest(defaultWorkHoursPerWeek: hours));

        Assert.Contains(errors, e => e.Contains("Default work hours"));
    }
}
