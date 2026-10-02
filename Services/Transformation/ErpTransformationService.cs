using System.Text.RegularExpressions;
using ProgrammePulse.Models.Erp;

namespace ProgrammePulse.Services.Transformation;

/// <summary>
/// Turns a RawErpRecord into a TransformedErpRecord, guided by what the
/// validator found. The pipeline stage a record ends up in is decided purely
/// from its data, in priority order:
///   1. any Error-level finding                -> Exception   (needs attention)
///   2. no source status supplied yet           -> New         (not started)
///   3. only Warning-level findings              -> Validating  (usable, but flagged)
///   4. otherwise                                -> Transformed (clean)
/// </summary>
public sealed class ErpTransformationService(IErpRecordValidator validator, IStatusMapper statusMapper)
    : IErpTransformationService
{
    public ErpPipelineItem Process(RawErpRecord raw)
    {
        var validation = validator.Validate(raw);
        var stage = DetermineStage(raw, validation);

        // Built unconditionally: each field converts independently and falls back
        // to null/a safe default on its own, so one bad field (e.g. an unparseable
        // date) never hides other fields that converted fine. The Stage is what
        // tells the dashboard/table this record still needs attention.
        var transformed = new TransformedErpRecord
        {
            SourceErpId = raw.SourceErpId,
            CustomerName = NormaliseCustomerName(raw.CustomerName),
            WorkType = string.IsNullOrWhiteSpace(raw.WorkType) ? "Unspecified" : raw.WorkType.Trim(),
            Status = statusMapper.Map(raw.RawStatus),
            Amount = ParseAmountSafely(raw.RawAmount),
            SourceDateUtc = ParseDateSafely(raw.RawSourceDate, raw.SourceTimeZoneId)
        };

        return new ErpPipelineItem
        {
            Raw = raw,
            Validation = validation,
            Stage = stage,
            Transformed = transformed
        };
    }

    public IReadOnlyList<ErpPipelineItem> ProcessAll(IEnumerable<RawErpRecord> rawRecords) =>
        rawRecords.Select(Process).ToList();

    private static ProcessingStage DetermineStage(RawErpRecord raw, ValidationResult validation)
    {
        if (validation.HasErrors)
        {
            return ProcessingStage.Exception;
        }

        if (string.IsNullOrWhiteSpace(raw.RawStatus))
        {
            return ProcessingStage.New;
        }

        return validation.HasWarnings ? ProcessingStage.Validating : ProcessingStage.Transformed;
    }

    /// <summary>
    /// Trims and collapses internal whitespace only. Deliberately does not
    /// re-case names (e.g. to Title Case), since that would mangle legitimate
    /// mixed-case names like "McDonald" or "O'Brien".
    /// </summary>
    private static string NormaliseCustomerName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return "Unknown Customer";
        }

        return Regex.Replace(rawName.Trim(), @"\s+", " ");
    }

    private static decimal? ParseAmountSafely(string? rawAmount) =>
        !string.IsNullOrWhiteSpace(rawAmount) && ErpParsing.TryCleanAmount(rawAmount, out var amount)
            ? amount
            : null;

    private static DateTime? ParseDateSafely(string? rawDate, string? sourceTimeZoneId) =>
        !string.IsNullOrWhiteSpace(rawDate) && ErpParsing.TryParseToUtc(rawDate, sourceTimeZoneId, out var utc)
            ? utc
            : null;
}
