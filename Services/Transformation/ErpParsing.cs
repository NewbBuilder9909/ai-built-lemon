using System.Globalization;
using System.Text.RegularExpressions;

namespace ProgrammePulse.Services.Transformation;

/// <summary>
/// Shared, side-effect-free parsing helpers used by both the validator (to check
/// whether a value *can* be parsed) and the transformation service (to actually
/// convert it). Kept separate so neither has to duplicate the parsing rules.
/// </summary>
internal static partial class ErpParsing
{
    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitOffsetPattern();

    public static bool TryCleanAmount(string rawAmount, out decimal amount)
    {
        var cleaned = rawAmount.Trim().Trim('$', '£', '€').Replace(",", string.Empty);
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    public static bool TryParseToUtc(string rawDate, string? sourceTimeZoneId, out DateTime utc)
    {
        var trimmed = rawDate.Trim();

        // Only trust DateTimeOffset's own offset handling when the string actually
        // carries an explicit offset/"Z". Otherwise DateTimeOffset.TryParse silently
        // assumes the *host machine's* local time zone, which would make the same
        // source date convert differently depending on where this app happens to run.
        if (ExplicitOffsetPattern().IsMatch(trimmed) &&
            DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
        {
            utc = withOffset.UtcDateTime;
            return true;
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            if (string.IsNullOrWhiteSpace(sourceTimeZoneId))
            {
                utc = DateTime.SpecifyKind(local, DateTimeKind.Utc);
                return true;
            }

            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(sourceTimeZoneId);
                var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
                utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
                return true;
            }
            catch (TimeZoneNotFoundException)
            {
                utc = DateTime.SpecifyKind(local, DateTimeKind.Utc);
                return true;
            }
        }

        utc = default;
        return false;
    }
}
