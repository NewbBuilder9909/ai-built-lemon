using System.Text.RegularExpressions;

namespace ProgrammePulse.Services.Shared;

/// <summary>
/// Table and column names placed into SQL text. Values always travel as
/// parameters; an identifier can't, so the shared helpers that take a table or
/// column name as a string pass it through here. Only plain identifier
/// characters are accepted, and the name is bracket-quoted. Every caller passes
/// a constant today; this keeps a future caller from turning one of these
/// helpers into an injection point (Aikido: SQL built by string concatenation).
/// </summary>
public static partial class SqlIdentifier
{
    public static string Quote(string name) =>
        Plain().IsMatch(name)
            ? $"[{name}]"
            : throw new ArgumentException($"'{name}' is not a plain SQL identifier.", nameof(name));

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex Plain();
}
