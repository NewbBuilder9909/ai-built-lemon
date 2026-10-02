using System.Globalization;
using System.Text;

namespace ProgrammePulse.Services.Reporting;

/// <summary>
/// Minimal RFC 4180 CSV writer — no external dependency for something this
/// small. A field is quoted only when it contains a comma, quote, or
/// newline; embedded quotes are doubled.
///
/// Text that a spreadsheet would execute as a formula (leading =, +, -, @,
/// tab or carriage return — OWASP "CSV injection") is prefixed with a single
/// quote so Excel shows it as text. Names and titles reach these exports
/// from connectors and from customer file uploads, and the people opening
/// them are executives, so this is not optional. Plain numbers, including
/// negative variances, are left alone so they stay numeric.
/// </summary>
public static class CsvWriter
{
    public static string WriteRow(params string?[] fields) =>
        string.Join(',', fields.Select(Escape)) + "\r\n";

    private static string Escape(string? field)
    {
        field ??= string.Empty;
        // Leading spaces are skipped first: some spreadsheet programs still
        // evaluate " =cmd", so the check looks at the first visible character.
        var visible = field.TrimStart(' ');
        if (visible.Length > 0 && visible[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            && !decimal.TryParse(field, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            field = "'" + field;
        }

        var needsQuoting = field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r');
        if (!needsQuoting)
        {
            return field;
        }

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }

    public static byte[] ToBytes(string csv) => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
}
