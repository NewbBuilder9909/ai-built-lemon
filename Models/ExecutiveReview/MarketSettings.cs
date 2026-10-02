using System.ComponentModel.DataAnnotations;

namespace ProgrammePulse.Models.ExecutiveReview;

public sealed record MarketSettings
{
    public string MarketCode { get; init; } = "GB";
    public string UiCulture { get; init; } = "en-GB";
    public string FormatCulture { get; init; } = "en-GB";
    public string ReportingCurrency { get; init; } = "GBP";
    public string TimeZoneId { get; init; } = "Europe/London";
    public int FiscalYearStartMonth { get; init; } = 1;
}

public sealed record MarketSettingsVersion(
    int Version, MarketSettings Settings, DateTime? EffectiveAtUtc, int? ChangedByMemberId);

public sealed class MarketSettingsInput
{
    [Required, Range(0, int.MaxValue)]
    public int? ExpectedVersion { get; set; }
    [Required] public string MarketCode { get; set; } = "GB";
    [Required] public string UiCulture { get; set; } = "en-GB";
    [Required] public string FormatCulture { get; set; } = "en-GB";
    [Required] public string ReportingCurrency { get; set; } = "GBP";
    [Required] public string TimeZoneId { get; set; } = "Europe/London";
    [Range(1, 12)] public int FiscalYearStartMonth { get; set; } = 1;

    public MarketSettings ToSettings() => new()
    {
        MarketCode = MarketCode, UiCulture = UiCulture, FormatCulture = FormatCulture,
        ReportingCurrency = ReportingCurrency, TimeZoneId = TimeZoneId,
        FiscalYearStartMonth = FiscalYearStartMonth
    };
}
