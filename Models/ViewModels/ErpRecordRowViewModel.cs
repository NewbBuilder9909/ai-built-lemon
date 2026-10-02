namespace ProgrammePulse.Models.ViewModels;

public sealed record ErpRecordRowViewModel(
    string ErpId,
    string CustomerDisplay,
    string WorkTypeDisplay,
    string RawStatusDisplay,
    string NormalisedStatusDisplay,
    string NormalisedStatusCssClass,
    string AmountDisplay,
    string SourceDateDisplay,
    string NormalisedUtcDateDisplay,
    ValidationSummaryViewModel Validation,
    string ProcessingStageDisplay,
    string ProcessingStageCssClass);
