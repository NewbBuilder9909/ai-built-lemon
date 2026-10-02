using System.Text.RegularExpressions;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// Source-level access for the architecture tests that need what reflection
/// cannot see: a type used only inside a method body (a thrown exception, a
/// static call), or the text of a SQL predicate.
/// </summary>
internal static partial class SourceTree
{
    public static string Root { get; } = FindRoot();

    public static string ServicesDirectory => Path.Combine(Root, "Services");

    /// <summary>
    /// The file's text with comments removed, so a type named in a doc
    /// comment or a commented-out line is not mistaken for a dependency.
    /// Line breaks are preserved so offsets still map to lines.
    /// </summary>
    public static string CodeOf(string path)
    {
        var text = File.ReadAllText(path);
        text = BlockComment().Replace(text, m => new string('\n', m.Value.Count(c => c == '\n')));
        return LineComment().Replace(text, string.Empty);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProgrammePulse.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (ProgrammePulse.csproj) not found.");
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    // Not inside a string: requires the // to be preceded by start-of-line
    // or whitespace, which excludes "https://..." literals.
    [GeneratedRegex(@"(?<=^|\s)//.*$", RegexOptions.Multiline)]
    private static partial Regex LineComment();
}
