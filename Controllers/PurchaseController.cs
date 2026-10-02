using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.ViewModels.Commercial;
using ProgrammePulse.Services.Commercial;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Public, credential-free product discovery page and self-service trial
/// starting point. Anonymous by design: configuration and controlled
/// provisioning only, no tenant context or existing member required.
/// </summary>
[Route("purchase")]
public sealed class PurchaseController(
    IOptions<CommercialOptions> options,
    IOptions<ProgrammePulse.Models.Branding.ProductBrandOptions> productBrand,
    ICommercialCatalogueService catalogue,
    ISelfServiceSignupService selfServiceSignupService) : Controller
{
    [HttpGet("")]
    public IActionResult Index(string? signup = null) => ViewFor(
        signupSuccessMessage: string.Equals(signup, "success", StringComparison.OrdinalIgnoreCase)
            ? "Your trial workspace is ready. Sign in at /staffops/account/login and complete MFA when prompted."
            : null);

    [HttpPost("start-trial")]
    [EnableRateLimiting("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartTrial(
        string companyName,
        string shortCode,
        string adminFullName,
        string adminEmail,
        string password,
        string confirmPassword,
        string plan,
        string[]? selectedModules)
    {
        // Off by default: the public page sells one assisted offer, and an
        // anonymous endpoint that creates tenants and admin accounts should not
        // exist in a deployment that isn't offering it.
        if (!options.Value.SelfServiceTrialEnabled)
            return NotFound();

        var request = new SelfServiceSignupRequest(
            companyName,
            shortCode,
            adminFullName,
            adminEmail,
            password,
            confirmPassword,
            plan,
            selectedModules ?? []);

        var result = await selfServiceSignupService.CreateTrialAsync(request);
        if (result.Succeeded)
        {
            return RedirectToAction(nameof(Index), new { signup = "success" });
        }

        return ViewFor(new PurchaseSignupFormViewModel(
            companyName,
            shortCode,
            adminFullName,
            adminEmail,
            plan,
            selectedModules ?? [],
            result.Errors));
    }

    private ViewResult ViewFor(PurchaseSignupFormViewModel? signup = null, string? signupSuccessMessage = null)
    {
        var commercial = options.Value;
        var selfServicePlans = catalogue.GetSelfServicePlans();
        var defaultPlan = signup?.Plan;
        if (string.IsNullOrWhiteSpace(defaultPlan) || !selfServicePlans.Contains(defaultPlan, StringComparer.OrdinalIgnoreCase))
        {
            defaultPlan = selfServicePlans.FirstOrDefault() ?? string.Empty;
        }

        var model = new PurchasePageViewModel(
            commercial.EnquiryConfigured ? commercial.EnquiryEmail : null,
            commercial.DefaultLoadedHourlyRate,
            commercial.DiagnosticFee,
            commercial.MonthlyRepeatFee,
            commercial.TrialDays,
            catalogue.GetPlans(),
            catalogue.GetModules(),
            selfServicePlans,
            signup ?? new PurchaseSignupFormViewModel(string.Empty, string.Empty, string.Empty, string.Empty, defaultPlan, [], []),
            signupSuccessMessage,
            // The one product-name setting (Product:Name) — shared with the
            // landing page, sign-in and the app, so they can't disagree.
            productBrand.Value.Name,
            commercial.SelfServiceTrialEnabled);

        return View("~/Views/Purchase/Index.cshtml", model);
    }
}
