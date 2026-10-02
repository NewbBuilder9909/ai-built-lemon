namespace ProgrammePulse.Models.ViewModels.Commercial;

/// <summary>
/// Everything the public /purchase page needs that isn't static copy.
/// Commercial catalogue, quote defaults and the self-service signup form all
/// come from configuration/services rather than being hard-coded in the view.
/// </summary>
public sealed record PurchasePageViewModel(
    string? EnquiryEmail,
    decimal DefaultLoadedHourlyRate,
    decimal DiagnosticFee,
    decimal MonthlyRepeatFee,
    int TrialDays,
    IReadOnlyList<CommercialPlanViewModel> Plans,
    IReadOnlyList<CommercialModuleViewModel> Modules,
    IReadOnlyList<string> SelfServicePlans,
    PurchaseSignupFormViewModel Signup,
    string? SignupSuccessMessage,
    string ProductName = "Delivery Evidence Check",
    bool SelfServiceTrialEnabled = false)
{
    public bool EnquiryConfigured => !string.IsNullOrWhiteSpace(EnquiryEmail);
}

public sealed record CommercialPlanViewModel(
    string Key,
    string DisplayName,
    decimal MonthlyPrice,
    decimal SetupFee,
    string Summary,
    bool SelfServiceAvailable,
    IReadOnlyList<string> IncludedFeatures);

public sealed record CommercialModuleViewModel(
    string FeatureKey,
    string DisplayName,
    decimal MonthlyPrice,
    decimal SetupFee,
    string Summary,
    bool SelfServiceAvailable,
    string? AvailableFromPlan);

public sealed record PurchaseSignupFormViewModel(
    string CompanyName,
    string ShortCode,
    string AdminFullName,
    string AdminEmail,
    string Plan,
    IReadOnlyList<string> SelectedModules,
    IReadOnlyList<string> Errors);

public sealed record CommercialQuoteViewModel(
    string? Plan,
    decimal MonthlyPrice,
    decimal SetupFee,
    IReadOnlyList<string> SelectedModules,
    IReadOnlyList<string> EffectiveFeatures);
