using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// The Phase 1 shape of the web layer (docs/architecture-review-2026-09-24.md),
/// made permanent: controllers bind, call an application service, and
/// return. Two properties are checkable:
///
/// - **No controller takes a repository.** Rules (validation, timestamps,
///   audit, tenant ownership checks) live in services, where they have one
///   home and can be tested without HTTP. Before Phase 1, 18 controllers
///   injected repositories, and this is how the unscoped staff audit read and
///   the unaudited Jira/Tempo credential writes went unnoticed.
/// - **ViewData carries page chrome only.** Page data travels in a typed
///   model that the Razor check can see. The chrome keys are the title (read
///   by the layout), a flash message, and a localised error resource key.
/// </summary>
public partial class ControllerLayerTests
{
    private static readonly HashSet<string> ChromeKeys = new(StringComparer.Ordinal) { "Title", "Message", "ErrorKey" };

    [Fact]
    public void No_controller_depends_on_a_repository()
    {
        var offenders = typeof(StaffReportingController).Assembly.GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Where(p => p.ParameterType.Name.EndsWith("Repository", StringComparison.Ordinal))
                .Select(p => $"{t.Name}({p.ParameterType.Name} {p.Name})"))
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "Controllers must go through an application service, not a repository: " + string.Join(", ", offenders));
    }

    [Fact]
    public void ViewData_carries_only_page_chrome()
    {
        var offenders = Directory.GetFiles(Path.Combine(SourceTree.Root, "Controllers"), "*.cs")
            .SelectMany(file => ViewDataAssignment().Matches(SourceTree.CodeOf(file))
                .Select(m => m.Groups["key"].Value)
                .Where(key => !ChromeKeys.Contains(key))
                .Select(key => $"{Path.GetFileName(file)}: ViewData[\"{key}\"]"))
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "Put page data in the view model instead of ViewData: " + string.Join(", ", offenders));
    }

    [GeneratedRegex(@"ViewData\[""(?<key>[A-Za-z]+)""\]\s*=")]
    private static partial Regex ViewDataAssignment();
}
