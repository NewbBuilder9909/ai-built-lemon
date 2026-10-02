using ProgrammePulse.Models.Tenancy;

namespace ProgrammePulse.Services.Commercial;

/// <summary>
/// Bound from configuration section "Commercial". Settings for the public,
/// credential-free product page at /purchase.
///
/// <see cref="EnquiryEmail"/> deliberately has no default. An unconfigured
/// enquiry address means the page has no working lead capture at all, and a
/// silently-broken "Open in email" link is worse than an absent one — so the
/// page renders an explicit "enquiries not configured" state instead. Set it
/// per environment (user-secrets locally, environment variable in hosting):
///
///   dotnet user-secrets set "Commercial:EnquiryEmail" "you@example.com" --project .
///   COMMERCIAL__ENQUIRYEMAIL=you@example.com
///
/// The acceptance test for this setting is behavioural, not structural: send
/// a real enquiry from the deployed page and confirm it arrives.
/// </summary>
public sealed class CommercialOptions
{
    public const string SectionName = "Commercial";

    /// <summary>Where a "Open in email" enquiry from /purchase is addressed. Null/blank disables the button.</summary>
    public string? EnquiryEmail { get; set; }

    /// <summary>
    /// Assumed fully-loaded internal hourly cost used to price the buyer's
    /// own adoption effort in the ROI check. Exposed as configuration so the
    /// figure can be corrected without a redeploy once real delivery hours
    /// have been measured.
    /// </summary>
    public decimal DefaultLoadedHourlyRate { get; set; } = 60m;

    /// <summary>
    /// Fixed fee for the diagnostic, as currently quoted: the founding price in
    /// docs/commercial/first-sale-playbook.md. Shown on /purchase and pre-filled
    /// in the ROI check. 0 hides both.
    /// </summary>
    public decimal DiagnosticFee { get; set; } = 3500m;

    /// <summary>Monthly fee for the evidence review that follows a diagnostic, as currently quoted. 0 hides it.</summary>
    public decimal MonthlyRepeatFee { get; set; } = 2000m;


    /// <summary>
    /// Whether /purchase offers the self-service trial, plan catalogue and
    /// add-ons, and whether POST /purchase/start-trial provisions anything.
    /// Off by default so a buyer meets one offer (the assisted diagnostic);
    /// the capability stays intact behind the switch for when a customer asks
    /// for it (docs/archive/cpo-readiness-review-2026-09-26.md, B4).
    /// Switch on deliberately: the sign-up has no email verification yet, so
    /// anyone could register an Admin under someone else's address, and
    /// docs/saas-onboarding.md defers public sign-up until invitations and
    /// abuse controls exist.
    /// </summary>
    public bool SelfServiceTrialEnabled { get; set; }

    /// <summary>Default trial length for self-service tenant provisioning.</summary>
    public int TrialDays { get; set; } = 30;

    public Dictionary<string, PlanOffer> Plans { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [TenantPlan.Starter] = new()
        {
            DisplayName = "Starter",
            Summary = "ClickUp-backed reporting for a single team with a guided trial setup.",
            MonthlyPrice = 495m,
            SetupFee = 750m,
            SelfServiceAvailable = true
        },
        [TenantPlan.Professional] = new()
        {
            DisplayName = "Professional",
            Summary = "Adds resource planning, branding and service-health support to the reporting core.",
            MonthlyPrice = 995m,
            SetupFee = 1250m,
            SelfServiceAvailable = true
        },
        [TenantPlan.Enterprise] = new()
        {
            DisplayName = "Enterprise",
            Summary = "Operator-assisted rollout for broader governance, evidence and commercial controls.",
            MonthlyPrice = 1995m,
            SetupFee = 2500m,
            SelfServiceAvailable = false
        }
    };

    public Dictionary<string, ModuleOffer> Modules { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [ProductFeature.HubPlannerSync] = new()
        {
            DisplayName = "Hub Planner sync",
            Summary = "Adds planned-capacity ingestion on top of the reporting core.",
            MonthlyPrice = 150m,
            SetupFee = 250m,
            SelfServiceAvailable = true,
            AvailableFromPlan = TenantPlan.Starter
        },
        [ProductFeature.Branding] = new()
        {
            DisplayName = "Branding Ops",
            Summary = "Tenant-specific branding controls, publish workflow and themed runtime shell.",
            MonthlyPrice = 125m,
            SetupFee = 200m,
            SelfServiceAvailable = true,
            AvailableFromPlan = TenantPlan.Starter
        },
        [ProductFeature.ContractOps] = new()
        {
            DisplayName = "Contract Ops",
            Summary = "Margin, invoicing and contract-document controls for commercial oversight.",
            MonthlyPrice = 250m,
            SetupFee = 400m,
            SelfServiceAvailable = true,
            AvailableFromPlan = TenantPlan.Starter
        },
        [ProductFeature.SupportEvidence] = new()
        {
            DisplayName = "Service health",
            Summary = "Support-case trend reporting and code-link review for service teams.",
            MonthlyPrice = 175m,
            SetupFee = 300m,
            SelfServiceAvailable = true,
            AvailableFromPlan = TenantPlan.Starter
        },
        [ProductFeature.GitHubEvidence] = new()
        {
            DisplayName = "Repository evidence",
            Summary = "Reviewed engineering-evidence ingestion. Enabled only with operator assistance.",
            MonthlyPrice = 225m,
            SetupFee = 500m,
            SelfServiceAvailable = false,
            AvailableFromPlan = TenantPlan.Professional
        }
    };

    public bool EnquiryConfigured => !string.IsNullOrWhiteSpace(EnquiryEmail);

    public sealed class PlanOffer
    {
        public string DisplayName { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public decimal MonthlyPrice { get; set; }

        public decimal SetupFee { get; set; }

        public bool SelfServiceAvailable { get; set; }
    }

    public sealed class ModuleOffer
    {
        public string DisplayName { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public decimal MonthlyPrice { get; set; }

        public decimal SetupFee { get; set; }

        public bool SelfServiceAvailable { get; set; }

        public string? AvailableFromPlan { get; set; }
    }
}
