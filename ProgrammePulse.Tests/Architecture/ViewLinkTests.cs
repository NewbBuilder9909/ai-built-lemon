using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// Links and form targets that the compiler cannot check.
///
/// A tag helper such as <c>asp-controller="StaffReporting" asp-action="CreateRisk"</c>
/// that names a controller or action which no longer exists does not fail
/// the build or the Razor check. It renders an empty <c>href</c> or form
/// <c>action</c>, and the form then posts back to the page it is on. Moving
/// actions between controllers (the Phase 1 split) is exactly how that
/// happens, so every pair is resolved here against the real controllers.
///
/// A literal <c>Redirect("/staffops/…")</c> in a controller has the same
/// property, so each one must match a GET route in the /staffops contract.
/// </summary>
public partial class ViewLinkTests
{
    [Fact]
    public void Every_tag_helper_controller_and_action_pair_exists()
    {
        var actionsByController = typeof(StaffReportingController).Assembly.GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToDictionary(
                t => t.Name[..^"Controller".Length],
                t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName)
                    .Select(m => m.GetCustomAttribute<ActionNameAttribute>()?.Name ?? m.Name)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        var broken = new List<string>();
        var checkedPairs = 0;
        foreach (var view in Directory.GetFiles(Path.Combine(SourceTree.Root, "Views"), "*.cshtml", SearchOption.AllDirectories))
        {
            foreach (Match element in TagWithController().Matches(File.ReadAllText(view)))
            {
                var controller = element.Groups["controller"].Value;
                var action = ActionAttribute().Match(element.Value) is { Success: true } a ? a.Groups["action"].Value : "Index";
                checkedPairs++;

                if (!actionsByController.TryGetValue(controller, out var actions) || !actions.Contains(action))
                {
                    broken.Add($"{Path.GetRelativePath(SourceTree.Root, view)}: {controller}.{action}");
                }
            }
        }

        Assert.True(checkedPairs > 20, "The scan found almost no tag helper links; the pattern has probably stopped matching.");
        Assert.True(broken.Count == 0,
            "These tag helpers name a controller action that does not exist and would render an empty link:"
            + Environment.NewLine + string.Join(Environment.NewLine, broken.Distinct()));
    }

    [Fact]
    public void Every_literal_staffops_redirect_targets_a_real_get_route()
    {
        var getRoutes = File.ReadAllLines(Path.Combine(SourceTree.Root, "ProgrammePulse.Tests", "Architecture", "staffops-routes.txt"))
            .Where(l => l.StartsWith("GET", StringComparison.Ordinal))
            .Select(l => l[4..l.IndexOf(" |", StringComparison.Ordinal)].Trim())
            .Select(route => new Regex("^" + Regex.Replace(Regex.Escape(route), @"\\\{[^}]*}", "[^/]+") + "$"))
            .ToList();

        var broken = new List<string>();
        var checkedRedirects = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(SourceTree.Root, "Controllers"), "*.cs"))
        {
            foreach (Match redirect in LiteralRedirect().Matches(SourceTree.CodeOf(file)))
            {
                checkedRedirects++;
                // Interpolations stand for a single path segment ({staffKey}),
                // and the query string is not part of the route.
                var path = Regex.Replace(redirect.Groups["path"].Value, @"\{[^}]*\}", "x").Split('?')[0].TrimEnd('/');
                if (path.Length == 0)
                {
                    path = "/";
                }

                if (!getRoutes.Any(route => route.IsMatch(path)))
                {
                    broken.Add($"{Path.GetFileName(file)}: {redirect.Groups["path"].Value}");
                }
            }
        }

        Assert.True(checkedRedirects > 30, "The scan found almost no literal redirects; the pattern has probably stopped matching.");
        Assert.True(broken.Count == 0,
            "These redirects point at no GET route in staffops-routes.txt:" + Environment.NewLine + string.Join(Environment.NewLine, broken.Distinct()));
    }

    [GeneratedRegex(@"<[a-zA-Z]+\b[^>]*?\basp-controller=""(?<controller>[A-Za-z]+)""[^>]*>", RegexOptions.Singleline)]
    private static partial Regex TagWithController();

    [GeneratedRegex(@"\basp-action=""(?<action>[A-Za-z]+)""")]
    private static partial Regex ActionAttribute();

    [GeneratedRegex(@"\bRedirect\(\$?""(?<path>/staffops[^""]*)""")]
    private static partial Regex LiteralRedirect();
}
