namespace ProgrammePulse.Models.ViewModels;

public sealed record ErpRecordDetailViewModel(
    string ErpId,
    string CustomerDisplay,
    string WorkTypeDisplay,
    string RawStatusDisplay,
    string NormalisedStatusDisplay,
    string NormalisedStatusCssClass,
    string AmountDisplay,
    string RawSourceDateDisplay,
    string NormalisedUtcDateDisplay,
    ValidationSummaryViewModel Validation,
    string ProcessingStageDisplay,
    string ProcessingStageCssClass);
