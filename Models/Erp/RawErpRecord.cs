namespace ProgrammePulse.Models.Erp;

/// <summary>
/// Shape of a record exactly as it arrives from the (fictional) source ERP system,
/// before any validation, trimming, or type conversion has happened.
/// Every field is deliberately loose (string/nullable) because that is how messy
/// upstream data actually looks — the transformation layer's job is to clean this up.
/// </summary>
public sealed record RawErpRecord
{
    public required string SourceErpId { get; init; }

    public string? CustomerName { get; init; }

    public string? WorkType { get; init; }

    public string? RawStatus { get; init; }

    public string? RawAmount { get; init; }

    public string? RawSourceDate { get; init; }

    /// <summary>
    /// IANA time zone id the source system reports dates in, e.g. "America/New_York".
    /// Null means the source date should be treated as already UTC.
    /// </summary>
    public string? SourceTimeZoneId { get; init; }
}
