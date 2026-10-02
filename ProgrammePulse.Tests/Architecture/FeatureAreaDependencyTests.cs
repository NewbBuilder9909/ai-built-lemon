using System.Text.RegularExpressions;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// The feature areas under Services/ (Staff, ProgrammeOps, ContractOps, ...)
/// must depend on each other in one direction only. A cycle means neither
/// area can be understood, tested or eventually extracted without the
/// other, which is what the modular monolith exists to prevent.
///
/// This used to fail twice. Staff ⇄ ProgrammeOps existed only because
/// CrossTenantReferenceException lived in ProgrammeOps, and every area
/// throwing it had to import that namespace; it now lives in
/// Services/Shared. Staff ⇄ Tenancy existed because StaffOnboardingService
/// read ITenantContext itself (and defaulted to the platform tenant when it
/// could not resolve one); the caller now passes the tenant in.
///
/// Source-level rather than reflection, because the usual coupling is a
/// type used only inside a method body, e.g. a thrown exception, which never
/// appears in a signature. An edge A → B is recorded only when a file in A
/// both imports B's namespace and names a type declared in B, so a stale
/// using directive alone does not count.
/// </summary>
public partial class FeatureAreaDependencyTests
{
    /// <summary>Plumbing below the areas, with its own boundary tests (SourceIndependenceTests).</summary>
    private const string Integrations = "Integrations";

    /// <summary>The shared kernel: anyone may depend on it, it depends on no area.</summary>
    private const string Shared = "Shared";

    [Fact]
    public void Feature_areas_have_no_dependency_cycles()
    {
        var edges = Edges();
        var cycles = FindCycles(edges.Keys.Select(e => e.From).Concat(edges.Keys.Select(e => e.To)).Distinct(), edges.Keys);

        Assert.True(cycles.Count == 0,
            "Feature areas depend on each other in a cycle. Move the shared type to Services/Shared, or pass the value in "
            + "from the caller. Cycles:" + Environment.NewLine
            + string.Join(Environment.NewLine, cycles.Select(c => string.Join(" → ", c)
                + "   [" + string.Join("; ", c.Zip(c.Skip(1)).Select(pair => $"{pair.First}→{pair.Second}: {edges[(pair.First, pair.Second)]}")) + "]")));
    }

    [Fact]
    public void The_shared_kernel_depends_on_no_feature_area()
    {
        var outgoing = Edges().Where(e => e.Key.From == Shared).Select(e => $"{e.Key.To} ({e.Value})").ToList();

        Assert.True(outgoing.Count == 0, "Services/Shared must not depend on a feature area: " + string.Join(", ", outgoing));
    }

    [Fact]
    public void The_scan_sees_the_known_one_way_dependencies()
    {
        // Guards against a vacuous pass: these real, legitimate edges must
        // be detected, or the scanner has stopped working.
        var edges = Edges();
        Assert.Contains(("ProgrammeOps", "Staff"), edges.Keys);
        Assert.Contains(("ContractOps", "ProgrammeOps"), edges.Keys);
        Assert.Contains(("SkillsEvidence", Shared), edges.Keys);
    }

    /// <summary>(from, to) → one example "File uses Type" as evidence.</summary>
    private static Dictionary<(string From, string To), string> Edges()
    {
        var areas = Directory.GetDirectories(SourceTree.ServicesDirectory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(a => a != Integrations)
            .ToArray();

        var filesByArea = areas.ToDictionary(
            area => area,
            area => Directory.GetFiles(Path.Combine(SourceTree.ServicesDirectory, area), "*.cs", SearchOption.AllDirectories));

        var declaredByArea = filesByArea.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .SelectMany(file => Declaration().Matches(SourceTree.CodeOf(file)).Select(m => m.Groups[1].Value))
                .ToHashSet(StringComparer.Ordinal));

        var edges = new Dictionary<(string, string), string>();
        foreach (var (area, files) in filesByArea)
        {
            foreach (var file in files)
            {
                var code = SourceTree.CodeOf(file);
                var body = UsingLine().Replace(code, string.Empty);

                foreach (var other in areas.Where(a => a != area))
                {
                    if (edges.ContainsKey((area, other)))
                    {
                        continue;
                    }

                    var imports = code.Contains($"using ProgrammePulse.Services.{other};", StringComparison.Ordinal)
                        || Regex.IsMatch(code, $@"\bServices\.{other}\.");
                    if (!imports)
                    {
                        continue;
                    }

                    var used = declaredByArea[other]
                        .Where(type => !declaredByArea[area].Contains(type))
                        .FirstOrDefault(type => Regex.IsMatch(body, $@"\b{Regex.Escape(type)}\b"));
                    if (used is not null)
                    {
                        edges[(area, other)] = $"{Path.GetFileName(file)} uses {used}";
                    }
                }
            }
        }

        return edges;
    }

    private static List<string[]> FindCycles(IEnumerable<string> nodes, IEnumerable<(string From, string To)> edges)
    {
        var next = edges.ToLookup(e => e.From, e => e.To);
        var cycles = new List<string[]>();
        var seen = new HashSet<string>();

        foreach (var start in nodes.Order())
        {
            Walk(start, [start]);
        }

        return cycles;

        void Walk(string node, List<string> path)
        {
            foreach (var target in next[node])
            {
                if (target == path[0])
                {
                    // Report each cycle once, from its alphabetically first node.
                    if (path.Min(StringComparer.Ordinal) == path[0] && seen.Add(string.Join(">", path)))
                    {
                        cycles.Add([.. path, target]);
                    }
                }
                else if (!path.Contains(target) && string.CompareOrdinal(target, path[0]) > 0)
                {
                    Walk(target, [.. path, target]);
                }
            }
        }
    }

    [GeneratedRegex(@"\b(?:class|interface|record|enum|struct)\s+([A-Z][A-Za-z0-9_]*)")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^\s*using\s+[^;]+;", RegexOptions.Multiline)]
    private static partial Regex UsingLine();
}
