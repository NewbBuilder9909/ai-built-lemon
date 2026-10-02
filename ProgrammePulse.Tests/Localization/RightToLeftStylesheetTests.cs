using System.Text.RegularExpressions;

namespace ProgrammePulse.Tests.Localization;

/// <summary>
/// Any language can be switched on in configuration, including right-to-left
/// ones, so the shared stylesheet must describe position by reading direction
/// (inline-start/end), not by the physical left/right. One physical rule —
/// the skip link at left:-999px — once widened every Arabic page to 2,365px,
/// pushing the navigation off-screen.
/// </summary>
public class RightToLeftStylesheetTests
{
    private static readonly Regex PhysicalDirection = new(
        @"(?:margin|padding|border)-(?:left|right)\b|(?<![-\w])(?:left|right)\s*:|text-align\s*:\s*(?:left|right)\b|float\s*:\s*(?:left|right)\b",
        RegexOptions.Compiled);

    [Theory]
    [InlineData("app.css")]
    [InlineData("charts.css")]
    public void The_shared_stylesheets_use_logical_properties_only(string stylesheet)
    {
        var css = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "wwwroot", "css", stylesheet));
        var withoutComments = Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        var offending = withoutComments.Split('\n')
            .Select((line, index) => (line: line.Trim(), number: index + 1))
            .Where(l => PhysicalDirection.IsMatch(l.line))
            .Select(l => l.line)
            .ToList();

        Assert.True(offending.Count == 0,
            "Use inset-inline-start/end, margin-inline-*, padding-inline-* or text-align: start/end instead of:\n"
            + string.Join('\n', offending));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProgrammePulse.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
