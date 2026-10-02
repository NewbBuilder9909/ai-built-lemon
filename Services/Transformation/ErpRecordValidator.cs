using ProgrammePulse.Models.Erp;

namespace ProgrammePulse.Services.Transformation;

/// <summary>
/// Inspects a raw record and reports what is wrong with it. This layer only
/// observes and reports — it never mutates or converts values. That job belongs
/// to ErpTransformationService, which decides how to react to each finding.
/// </summary>
public sealed class ErpRecordValidator : IErpRecordValidator
{
    public ValidationResult Validate(RawErpRecord record)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(record.CustomerName))
        {
            result.AddWarning("Customer name is missing.");
        }

        if (string.IsNullOrWhiteSpace(record.WorkType))
        {
            result.AddWarning("Work type is missing; will default to 'Unspecified'.");
        }

        if (string.IsNullOrWhiteSpace(record.RawStatus))
        {
            result.AddWarning("Source status is missing; record cannot be mapped to a business status yet.");
        }
        else if (MapsToRecognisedStatus(record.RawStatus) is false)
        {
            result.AddWarning($"Source status '{record.RawStatus.Trim()}' is not recognised; mapped to Exception.");
        }

        if (string.IsNullOrWhiteSpace(record.RawAmount))
        {
            result.AddWarning("Amount is missing.");
        }
        else if (!ErpParsing.TryCleanAmount(record.RawAmount, out _))
        {
            result.AddError($"Amount '{record.RawAmount}' could not be parsed as a number.");
        }

        if (string.IsNullOrWhiteSpace(record.RawSourceDate))
        {
            result.AddWarning("Source date is missing.");
        }
        else if (!ErpParsing.TryParseToUtc(record.RawSourceDate, record.SourceTimeZoneId, out _))
        {
            result.AddError($"Source date '{record.RawSourceDate}' could not be parsed.");
        }

        return result;
    }

    private static bool MapsToRecognisedStatus(string rawStatus) =>
        rawStatus.Trim().ToUpperInvariant() is "OPEN" or "WIP" or "DONE";
}
