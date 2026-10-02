using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Branding;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.Staff;
using ProgrammePulse.Services.Security;
using ProgrammePulse.Services.Staff;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Security;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Login/logout for the Staff Operations portal, backed by Umbraco Members
/// (not the backoffice user system). Plain MVC controller, not a
/// SurfaceController, consistent with LanguageController.
///
/// Admin and Platform Admin members require TOTP MFA on top of the password: Login no longer
/// signs the member in directly — it only verifies the password via
/// CheckPasswordSignInAsync, then hands off to MfaEnroll/MfaChallenge via
/// the short-lived MfaPending cookie (IMfaChallengeStore). The real
/// sign-in (SignInAsync) only happens once a correct TOTP code has been
/// entered. Non-Admin members are unaffected — same PasswordSignInAsync-
/// equivalent flow as before, just via CheckPasswordSignInAsync + SignInAsync.
///
/// The Admin check here uses IMemberService.GetMembersByGroup, not
/// MemberManager.IsMemberAuthorizedAsync — that one reads the signed-in
/// member from HttpContext, and at this point in Login() nobody is signed
/// in yet (that's the whole point of checking the password first). This is
/// the "IMemberService group queries" path IStaffRoleAssignmentService's
/// docs already point to for checking a member who isn't the current user.
/// </summary>
[Route("staffops/account")]
public sealed class StaffAccountController(
    MemberSignInManager signInManager,
    MemberManager memberManager,
    IMemberService memberService,
    ISignInEligibility signInEligibility,
    IMfaChallengeStore mfaChallengeStore,
    MfaVerificationService mfaVerification,
    IOptions<ProductBrandOptions> productBrand,
    ILogger<StaffAccountController> logger) : Controller
{
    public const string ResetPath = "/staffops/account/reset";

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl) => View("~/Views/StaffOps/Account/Login.cshtml", (object?)returnUrl);

    /// <summary>
    /// Forgotten password. There is no email service, so this says who can
    /// help: an Admin issues a one-time reset link from Settings → People.
    /// </summary>
    [HttpGet("forgot")]
    public IActionResult Forgot()
    {
        ViewData["Title"] = "Forgotten your password?";
        return View("~/Views/StaffOps/Account/Forgot.cshtml");
    }

    /// <summary>The page a reset link opens. The token is checked only when a new password is posted.</summary>
    [HttpGet("reset")]
    public IActionResult Reset(int m, string? t)
    {
        ViewData["Title"] = "Set a new password";
        return View("~/Views/StaffOps/Account/Reset.cshtml", new PasswordResetFormViewModel(m, t ?? string.Empty, [], Done: false));
    }

    [HttpPost("reset")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Reset(int m, string? t, string? password, string? confirm, [FromServices] IMemberAccountAdministration accounts)
    {
        ViewData["Title"] = "Set a new password";
        IReadOnlyList<string> errors =
            string.IsNullOrWhiteSpace(t) ? ["This reset link isn't valid."]
            : string.IsNullOrEmpty(password) ? ["Enter a new password."]
            : password != confirm ? ["The two passwords don't match."]
            : await accounts.ResetPasswordAsync(m, t, password);

        SecurityEvents.Write(logger, HttpContext, errors.Count == 0 ? "PasswordReset" : "PasswordResetFailed", m.ToString(), warning: errors.Count > 0);
        return View("~/Views/StaffOps/Account/Reset.cshtml", new PasswordResetFormViewModel(m, t ?? string.Empty, errors, Done: errors.Count == 0));
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl)
    {
        var member = await memberManager.FindByEmailAsync(email);
        if (member is null)
        {
            SecurityEvents.Write(logger, HttpContext, "LoginFailed");
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View("~/Views/StaffOps/Account/Login.cshtml", (object?)returnUrl);
        }

        var checkResult = await signInManager.CheckPasswordSignInAsync(member, password, lockoutOnFailure: true);
        if (!checkResult.Succeeded)
        {
            SecurityEvents.Write(logger, HttpContext, checkResult.IsLockedOut ? "AccountLockedOut" : "LoginFailed", member.Id);
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View("~/Views/StaffOps/Account/Login.cshtml", (object?)returnUrl);
        }

        if (!int.TryParse(member.Id, out var memberIntId)
            || !await signInEligibility.IsActiveStaffMemberAsync(memberIntId))
        {
            SecurityEvents.Write(logger, HttpContext, "InactiveAccountLoginDenied", member.Id);
            ModelState.AddModelError(string.Empty, "This staff account is inactive.");
            return View("~/Views/StaffOps/Account/Login.cshtml", (object?)returnUrl);
        }

        var requiresMfa = RequiresMfa(
                IsMemberInGroup(memberIntId, StaffRole.Admin),
                IsMemberInGroup(memberIntId, StaffRole.PlatformAdmin));
        if (!requiresMfa)
        {
            await signInManager.SignInAsync(member, isPersistent: false);
            SecurityEvents.Write(logger, HttpContext, "LoginSucceeded", member.Id, warning: false);
            return RedirectAfterSignIn(returnUrl);
        }

        await mfaVerification.EnsureEnrollmentAsync(memberIntId);
        await mfaChallengeStore.StartAsync(HttpContext, member.Id,
            member.SecurityStamp ?? throw new InvalidOperationException("Member security stamp is missing."), returnUrl);
        SecurityEvents.Write(logger, HttpContext, "MfaChallengeStarted", member.Id, warning: false);
        var enrollment = await mfaVerification.GetEnrollmentStateAsync(int.Parse(member.Id));

        return enrollment == MfaEnrollmentState.Enrolled
            ? Redirect("/staffops/account/mfa/challenge")
            : Redirect("/staffops/account/mfa/enroll");
    }

    [HttpGet("mfa/enroll")]
    public async Task<IActionResult> MfaEnroll()
    {
        var pending = await mfaChallengeStore.GetAsync(HttpContext);
        if (pending is null)
        {
            SecurityEvents.Write(logger, HttpContext, "MfaPendingMissing", warning: HttpMethods.IsPost(Request.Method));
            return Redirect("/staffops/account/login");
        }

        var memberId = int.Parse(pending.Value.MemberId);
        if (await mfaVerification.GetEnrollmentStateAsync(memberId) == MfaEnrollmentState.Enrolled)
        {
            // Already enrolled from an earlier session — nothing to set up, go straight to the code prompt.
            return Redirect("/staffops/account/mfa/challenge");
        }

        var secret = await mfaVerification.GetEnrollmentSecretAsync(memberId, pending.Value.CredentialVersion);
        if (secret is null) return Redirect("/staffops/account/login");
        var member = await memberManager.FindByIdAsync(pending.Value.MemberId);
        var otpAuthUri = TotpAuthenticator.BuildOtpAuthUri(secret, member?.Email ?? pending.Value.MemberId, productBrand.Value.Name);

        return View("~/Views/StaffOps/Account/MfaEnroll.cshtml", (secret, otpAuthUri));
    }

    [HttpPost("mfa/enroll")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> MfaEnrollConfirm(string code)
    {
        var pending = await mfaChallengeStore.GetAsync(HttpContext);
        if (pending is null)
        {
            SecurityEvents.Write(logger, HttpContext, "MfaPendingMissing", warning: HttpMethods.IsPost(Request.Method));
            return Redirect("/staffops/account/login");
        }

        var memberId = int.Parse(pending.Value.MemberId);
        var enrollment = await mfaVerification.GetEnrollmentStateAsync(memberId);
        if (enrollment == MfaEnrollmentState.Enrolled)
            return Redirect("/staffops/account/mfa/challenge");
        if (enrollment == MfaEnrollmentState.NotStarted)
        {
            return Redirect("/staffops/account/mfa/enroll");
        }

        var recoveryCodes = await mfaVerification.ConfirmEnrollmentAsync(memberId, pending.Value.CredentialVersion, code);
        if (recoveryCodes is null)
        {
            await mfaChallengeStore.RecordFailedAttemptAsync(HttpContext);
            SecurityEvents.Write(logger, HttpContext, "MfaFailed", pending.Value.MemberId);
            ModelState.AddModelError(string.Empty, await LockoutMessageAsync(int.Parse(pending.Value.MemberId))
                ?? "That code didn't match. Check the time on your authenticator app and try again.");
            return await MfaEnroll();
        }

        SecurityEvents.Write(logger, HttpContext, "MfaEnrolled", pending.Value.MemberId, warning: false);

        var signedInMember = await SignInMemberCoreAsync(pending.Value.MemberId);
        if (signedInMember is null)
        {
            return Redirect("/staffops/account/login");
        }

        return View("~/Views/StaffOps/Account/MfaRecoveryCodes.cshtml", (RecoveryCodes: recoveryCodes, ContinueUrl: SafeReturnUrl(pending.Value.ReturnUrl)));
    }

    [HttpGet("mfa/challenge")]
    public async Task<IActionResult> MfaChallenge()
    {
        var pending = await mfaChallengeStore.GetAsync(HttpContext);
        if (pending is null)
        {
            SecurityEvents.Write(logger, HttpContext, "MfaPendingMissing", warning: HttpMethods.IsPost(Request.Method));
            return Redirect("/staffops/account/login");
        }

        if (await mfaVerification.GetEnrollmentStateAsync(int.Parse(pending.Value.MemberId)) != MfaEnrollmentState.Enrolled)
        {
            return Redirect("/staffops/account/mfa/enroll");
        }

        return View("~/Views/StaffOps/Account/MfaChallenge.cshtml");
    }

    [HttpPost("mfa/challenge")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> MfaChallengeVerify(string code)
    {
        var pending = await mfaChallengeStore.GetAsync(HttpContext);
        if (pending is null)
        {
            SecurityEvents.Write(logger, HttpContext, "MfaPendingMissing", warning: HttpMethods.IsPost(Request.Method));
            return Redirect("/staffops/account/login");
        }

        if (await mfaVerification.GetEnrollmentStateAsync(int.Parse(pending.Value.MemberId)) != MfaEnrollmentState.Enrolled)
        {
            return Redirect("/staffops/account/mfa/enroll");
        }

        if (!await mfaVerification.VerifyAsync(int.Parse(pending.Value.MemberId), pending.Value.CredentialVersion, code))
        {
            await mfaChallengeStore.RecordFailedAttemptAsync(HttpContext);
            SecurityEvents.Write(logger, HttpContext, "MfaFailed", pending.Value.MemberId);
            ModelState.AddModelError(string.Empty, await LockoutMessageAsync(int.Parse(pending.Value.MemberId))
                ?? "That code didn't match. Check the time on your authenticator app and try again.");
            return View("~/Views/StaffOps/Account/MfaChallenge.cshtml");
        }

        return await CompleteSignInAsync(pending.Value.MemberId, pending.Value.ReturnUrl);
    }

    [HttpGet("mfa/challenge/recovery")]
    public async Task<IActionResult> MfaChallengeRecovery()
    {
        var pending = await mfaChallengeStore.GetAsync(HttpContext);
        if (pending is null)
        {
            SecurityEvents.Write(logger, HttpContext, "MfaPendingMissing", warning: HttpMethods.IsPost(Request.Method));
            return Redirect("/staffops/account/login");
        }

        return View("~/Views/StaffOps/Account/MfaChallengeRecovery.cshtml");
    }

    /// <summary>
    /// Break-glass sign-in for an Admin who lost their authenticator app and
    /// has no other Admin available to reset them via StaffAdminController —
    /// redeems one of the codes shown once at enrollment instead of a TOTP
    /// code. See MfaRecoveryCodeGenerator/IMfaRepository.RedeemRecoveryCodeAsync.
    /// </summary>
    [HttpPost("mfa/challenge/recovery")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> MfaChallengeRecoveryVerify(string code)
    {
        var pending = await mfaChallengeStore.GetAsync(HttpContext);
        if (pending is null)
        {
            SecurityEvents.Write(logger, HttpContext, "MfaPendingMissing", warning: HttpMethods.IsPost(Request.Method));
            return Redirect("/staffops/account/login");
        }

        var memberId = int.Parse(pending.Value.MemberId);
        if (await mfaVerification.GetEnrollmentStateAsync(memberId) != MfaEnrollmentState.Enrolled)
        {
            return Redirect("/staffops/account/mfa/enroll");
        }

        var remaining = await mfaVerification.RedeemRecoveryCodeAsync(memberId, code);
        if (remaining is null)
        {
            await mfaChallengeStore.RecordFailedAttemptAsync(HttpContext);
            SecurityEvents.Write(logger, HttpContext, "MfaRecoveryFailed", pending.Value.MemberId);
            ModelState.AddModelError(string.Empty, await LockoutMessageAsync(memberId)
                ?? "That recovery code is invalid or has already been used.");
            return View("~/Views/StaffOps/Account/MfaChallengeRecovery.cshtml");
        }

        SecurityEvents.Write(logger, HttpContext, "MfaRecoveryRedeemed", pending.Value.MemberId);
        var member = await SignInMemberCoreAsync(pending.Value.MemberId);
        if (member is null)
        {
            return Redirect("/staffops/account/login");
        }

        return View("~/Views/StaffOps/Account/MfaRecoveryCodeUsed.cshtml", (RemainingCodes: remaining.Value, ContinueUrl: SafeReturnUrl(pending.Value.ReturnUrl)));
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await mfaChallengeStore.ClearAsync(HttpContext);
        await signInManager.SignOutAsync();
        return Redirect("/staffops/account/login");
    }

    private async Task<IActionResult> CompleteSignInAsync(string memberId, string? returnUrl)
    {
        var member = await SignInMemberCoreAsync(memberId);
        return member is null
            ? Redirect("/staffops/account/login")
            : RedirectAfterSignIn(returnUrl);
    }

    /// <summary>Shared by the TOTP, recovery-code, and enrollment-confirm paths: signs the member into the real Umbraco Member cookie and clears the short-lived MfaPending cookie either way.</summary>
    private async Task<MemberIdentityUser?> SignInMemberCoreAsync(string memberId)
    {
        var member = await memberManager.FindByIdAsync(memberId);
        if (member is not null && int.TryParse(member.Id, out var id)
            && await signInEligibility.IsActiveStaffMemberAsync(id)
            && await signInManager.CanSignInAsync(member)
            && !await memberManager.IsLockedOutAsync(member)
            && await mfaChallengeStore.TryConsumeAsync(HttpContext, memberId))
        {
            await signInManager.SignInAsync(member, isPersistent: false);
            SecurityEvents.Write(logger, HttpContext, "MfaLoginSucceeded", member.Id, warning: false);
        }
        else
        {
            SecurityEvents.Write(logger, HttpContext, "MfaSignInDenied", memberId);
            member = null;
        }

        await mfaChallengeStore.ClearAsync(HttpContext);
        return member;
    }

    private bool IsMemberInGroup(int memberId, string groupName) =>
        memberService.GetMembersByGroup(groupName).Any(m => m.Id == memberId);

    internal static bool RequiresMfa(bool isAdmin, bool isPlatformAdmin) => isAdmin || isPlatformAdmin;

    // With no return URL, sign-in goes to the landing resolver rather than a
    // fixed page, so each role starts on the page it gets value from (and a
    // Platform Admin is not bounced back to this login form). See
    // Services/Staff/StaffLandingPage.
    private IActionResult RedirectAfterSignIn(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : Redirect(StaffLandingPage.ResolverPath);

    /// <summary>The message for a member whose MFA is locked after repeated wrong codes; null when not locked.</summary>
    private async Task<string?> LockoutMessageAsync(int memberId) =>
        await mfaVerification.GetLockedUntilAsync(memberId) switch
        {
            null => null,
            { } until when until >= MfaVerificationService.LockedUntilReset =>
                "Too many incorrect codes. Your MFA is locked until another Admin resets it.",
            { } until => $"Too many incorrect codes. Try again after {until:yyyy-MM-dd HH:mm} UTC, or ask another Admin to reset your MFA."
        };

    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : StaffLandingPage.ResolverPath;
}
