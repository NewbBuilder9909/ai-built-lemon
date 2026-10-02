namespace ProgrammePulse.Services.Shared;

/// <summary>
/// Escapes a user's search text for a T-SQL <c>LIKE … ESCAPE '\'</c>, so a
/// typed %, _ or [ matches itself instead of acting as a wildcard.
/// </summary>
public static class SqlLike
{
    public static string Escape(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}
