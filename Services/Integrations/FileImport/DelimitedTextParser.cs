using System.Text;

namespace ProgrammePulse.Services.Integrations.FileImport;

/// <summary>
/// RFC 4180 CSV reader: quoted fields, doubled quotes, embedded commas and
/// line breaks, CRLF or LF, an optional UTF-8 BOM. Hand-rolled rather than a
/// new NuGet dependency for the same reason TotpAuthenticator is — the
/// format is small and fully specified, and a parser here is one file.
///
/// Refuses rather than guesses: an unterminated quote, text after a closing
/// quote, or more rows/bytes than the caller allows throws
/// <see cref="FileImportFormatException"/> with a reader-facing message.
/// </summary>
public static class DelimitedTextParser
{
    public static IReadOnlyList<string[]> Parse(string text, int maxRows)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var afterClosingQuote = false;
        var line = 1;
        var i = text.Length > 0 && text[0] == '﻿' ? 1 : 0;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            afterClosingQuote = false;
        }

        void EndRow()
        {
            EndField();
            // A blank line (one empty field) carries no data — skip it rather
            // than reporting a row with every required column missing.
            if (!(fields.Count == 1 && fields[0].Length == 0))
            {
                if (rows.Count >= maxRows)
                    throw new FileImportFormatException($"The file has more than {maxRows - 1:N0} data rows. Split it and import each part.");
                rows.Add(fields.ToArray());
            }
            fields.Clear();
            line++;
        }

        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                        afterClosingQuote = true;
                    }
                }
                else
                {
                    if (c == '\n') line++;
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case ',':
                    EndField();
                    break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    EndRow();
                    break;
                case '\n':
                    EndRow();
                    break;
                case '"' when field.Length == 0 && !afterClosingQuote:
                    inQuotes = true;
                    break;
                default:
                    if (afterClosingQuote)
                        throw new FileImportFormatException($"Line {line}: text after a closing quote. Wrap the whole value in quotes and double any quote inside it.");
                    field.Append(c);
                    break;
            }
        }

        if (inQuotes)
            throw new FileImportFormatException($"Line {line}: a quoted value is never closed.");

        if (field.Length > 0 || fields.Count > 0)
            EndRow();

        return rows;
    }
}

/// <summary>The file itself cannot be read as the expected shape. Its message is safe to show the uploader.</summary>
public sealed class FileImportFormatException(string message) : Exception(message);
