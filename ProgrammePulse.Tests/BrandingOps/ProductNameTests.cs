using System.Text.RegularExpressions;
using ProgrammePulse.Models.Branding;

namespace ProgrammePulse.Tests.BrandingOps;

/// <summary>
/// The product's customer-facing name is configuration (Product:Name) because
/// neither working name has cleared: "Hwb" is the Welsh Government's schools
/// platform and "ProgrammePulse" sits beside BearingPoint's "Program Pulse".
/// These tests keep a rename a one-line change by failing the build if a view
/// or a shipped asset spells either name out again.
/// </summary>
public class ProductNameTests
{
    private static readonly string Root = FindRepositoryRoot();

    // A literal product name in visible text — not a C# namespace
    // ("ProgrammePulse.Models...") or an identifier.
    private static readonly Regex LiteralProductName = new(@"ProgrammePulse(?![.\w])|Programme<span>Pulse", RegexOptions.Compiled);
    private static readonly Regex Hwb = new(@"hwb", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static TheoryData<string> CustomerFacingFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "Views"), "*.cshtml", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(Path.Combine(Root, "wwwroot", "css"), "*.css"))
                     .Concat(Directory.EnumerateFiles(Path.Combine(Root, "wwwroot", "js"), "*.js")))
        {
            data.Add(Path.GetRelativePath(Root, file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CustomerFacingFiles))]
    public void No_customer_facing_file_hardcodes_the_product_name(string relativePath)
    {
        var offending = File.ReadAllLines(Path.Combine(Root, relativePath))
            .Select((line, index) => (line, number: index + 1))
            .Where(l => LiteralProductName.IsMatch(l.line))
            .Select(l => $"line {l.number}: {l.line.Trim()}")
            .ToList();

        Assert.True(offending.Count == 0,
            $"{relativePath} spells out the product name; use @ProductBrand.Value.Name instead.\n{string.Join('\n', offending)}");
    }

    [Theory]
    [MemberData(nameof(CustomerFacingFiles))]
    public void No_customer_facing_file_uses_the_Hwb_name(string relativePath)
    {
        var offending = File.ReadAllLines(Path.Combine(Root, relativePath))
            .Select((line, index) => (line, number: index + 1))
            .Where(l => Hwb.IsMatch(l.line))
            .Select(l => $"line {l.number}: {l.line.Trim()}")
            .ToList();

        Assert.True(offending.Count == 0, $"{relativePath} uses \"Hwb\".\n{string.Join('\n', offending)}");
    }

    [Theory]
    [InlineData("ProgrammePulse", "Pulse", "Programme", "Pulse")]
    [InlineData("Northwind", "wind", "North", "wind")]
    [InlineData("Northwind", null, "Northwind", "")]
    [InlineData("Northwind", "South", "Northwind", "")] // not a suffix: shown plain
    [InlineData("Pulse", "Pulse", "Pulse", "")]          // whole name is not an accent
    public void The_wordmark_splits_only_on_a_real_suffix(string name, string? accent, string lead, string accentPart)
    {
        var brand = new ProductBrandOptions { Name = name, WordmarkAccent = accent };

        Assert.Equal(lead, brand.WordmarkLead);
        Assert.Equal(accentPart, brand.WordmarkAccentPart);
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
