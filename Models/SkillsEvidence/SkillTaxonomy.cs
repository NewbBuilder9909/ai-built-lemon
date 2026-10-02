using System.Text.RegularExpressions;

namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// The rules a skill key has to obey, and the version of the taxonomy those
/// rules belong to.
///
/// A skill key is the stable identifier an assertion points at
/// ("csharp", "umbraco", "incident-command"); the display name can be
/// edited without breaking a single stored assertion. Keys are normalised
/// on the way in — lower-cased, trimmed, spaces and underscores folded to
/// hyphens — so "C Sharp", "c sharp" and "C-Sharp" cannot become three
/// different skills that each look under-covered.
///
/// Pure, no I/O, so the rules are unit-testable on their own.
/// </summary>
public static partial class SkillTaxonomy
{
    /// <summary>
    /// Bumped when the *shape* of the taxonomy changes — a new
    /// <see cref="SkillKind"/>, a changed key rule, a changed rubric. Stored
    /// on every <see cref="SkillDefinition"/> so a later migration can tell
    /// which rows were written under which rules, rather than guessing from
    /// a timestamp.
    /// </summary>
    public const int CurrentVersion = 1;

    public const int MaxKeyLength = 128;
    public const int MaxNameLength = 200;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KeyShape();

    /// <summary>
    /// Folds a human-typed skill name or key into the canonical form. Does
    /// not validate — call <see cref="IsValidKey"/> on the result, because
    /// input that normalises to nothing usable ("!!!", "") must be rejected
    /// rather than stored as an empty key.
    /// </summary>
    public static string NormalizeKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var folded = raw.Trim().ToLowerInvariant();
        var builder = new System.Text.StringBuilder(folded.Length);

        foreach (var character in folded)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (character is ' ' or '_' or '-' or '.' or '/' or '+')
            {
                // Collapse runs of separators rather than emitting "c--net".
                if (builder.Length > 0 && builder[^1] != '-')
                {
                    builder.Append('-');
                }
            }

            // Anything else (punctuation, accents, emoji) is dropped: a key
            // is an identifier, and the display name is where the real
            // spelling lives.
        }

        return builder.ToString().Trim('-');
    }

    public static bool IsValidKey(string? key) =>
        !string.IsNullOrEmpty(key) && key.Length <= MaxKeyLength && KeyShape().IsMatch(key);

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaxNameLength;
}
