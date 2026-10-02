using System.Reflection;
using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// The guard that keeps Stage 0's exit criterion true: a future Jira (or
/// calendar, or HR) integration must be addable without rewriting Silver or
/// Gold. That only stays true if nothing above the integration boundary can
/// reach a vendor type, so this asserts it mechanically rather than leaving
/// it to code review and doc comments.
///
/// Scope of the check: for every type in the canonical (Silver) and
/// ProgrammeOps (Gold/persistence) namespaces, every type reachable from its
/// *signatures* — base type, interfaces, fields (including private, which is
/// what a constructor-injected dependency becomes), properties, method
/// parameters and returns, constructor parameters, and generic arguments —
/// must not live in a vendor namespace.
///
/// Because compiler-generated types (async state machines, lambda display
/// classes) are themselves scanned, a vendor type used only inside an async
/// method body or a lambda is caught too: it becomes a field on the
/// generated type. A vendor type touched only in a synchronous method body
/// and never stored or passed would slip through — catching that needs IL
/// parsing and a new dependency, and every realistic leak vector (injecting
/// a client, storing a DTO, taking or returning one) already goes through a
/// signature.
///
/// Services/Integrations/Abstractions is intentionally absent from the
/// forbidden list: it is the source-neutral seam (ISyncSource,
/// SourceCapabilities, IIngestionContext, RawEntity) that Silver/Gold *may*
/// depend on. Only the vendor-specific namespaces are barred.
/// </summary>
public sealed class SourceIndependenceTests
{
    /// <summary>
    /// One entry per integrated vendor, listing every namespace that vendor
    /// owns. Grouped by vendor rather than kept as a flat namespace list
    /// because a vendor legitimately spans two namespaces (its service
    /// folder and its Raw DTO folder), and those two must not count as
    /// "cross-vendor" references to each other.
    /// </summary>
    private static readonly (string Vendor, string[] Namespaces)[] Vendors =
    [
        ("ClickUp",
        [
            "ProgrammePulse.Services.Integrations.ClickUp",
            "ProgrammePulse.Models.Integrations.ClickUp"
        ]),
        ("HubPlanner",
        [
            "ProgrammePulse.Services.Integrations.HubPlanner",
            "ProgrammePulse.Models.Integrations.HubPlanner"
        ]),
        ("Aikido",
        [
            "ProgrammePulse.Services.Integrations.Aikido"
        ]),
        ("FileImport",
        [
            "ProgrammePulse.Services.Integrations.FileImport"
        ]),
        // The Jira and Tempo report (JiraTempoReconciliation) is source-aware by
        // name only; these keep it, and the two sources, from reaching further.
        ("Jira",
        [
            "ProgrammePulse.Services.Integrations.Jira"
        ]),
        ("Tempo",
        [
            "ProgrammePulse.Services.Integrations.Tempo"
        ])
    ];

    /// <summary>Every vendor-owned namespace. Nothing above the boundary may reference these.</summary>
    private static readonly string[] VendorNamespaces = Vendors.SelectMany(v => v.Namespaces).ToArray();

    /// <summary>The layers that must stay source-agnostic.</summary>
    private static readonly string[] SourceAgnosticNamespaces =
    [
        "ProgrammePulse.Models.Programme",
        "ProgrammePulse.Services.ProgrammeOps",

        // Security assurance is tool-neutral (decision 5), and contract assurance
        // reads it only through the snapshot, never through a scanner's types.
        "ProgrammePulse.Models.SecurityAssurance",
        "ProgrammePulse.Services.SecurityAssurance",
        "ProgrammePulse.Services.ContractOps"
    ];

    private static readonly Assembly ApplicationAssembly = typeof(WorkItem).Assembly;

    [Fact]
    public void Silver_and_Gold_types_never_reference_a_vendor_namespace()
    {
        var violations = new List<string>();

        foreach (var type in TypesIn(SourceAgnosticNamespaces))
        {
            foreach (var referenced in ReferencedTypes(type))
            {
                if (VendorOf(referenced) is not null)
                {
                    violations.Add($"{type.FullName} references {referenced.FullName}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Silver/Gold must stay source-agnostic so a new Bronze source needs no changes above the boundary. "
            + "Route the dependency through Services/Integrations/Abstractions instead. Violations:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Distinct().Order()));
    }

    /// <summary>
    /// Guards the inverse of the rule above: one vendor integration must not
    /// depend on another's namespace. This failed before Stage 0 — Hub
    /// Planner's API client reached into the ClickUp namespace for
    /// RawEntity&lt;T&gt;, which is why that type now lives in Abstractions.
    /// </summary>
    [Fact]
    public void One_vendor_integration_never_references_another()
    {
        var violations = new List<string>();

        foreach (var type in TypesIn(VendorNamespaces))
        {
            var ownVendor = VendorOf(type);
            if (ownVendor is null)
            {
                continue;
            }

            foreach (var referenced in ReferencedTypes(type))
            {
                var otherVendor = VendorOf(referenced);
                if (otherVendor is not null && otherVendor != ownVendor)
                {
                    violations.Add($"{type.FullName} references {referenced.FullName}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Each Bronze integration must stand alone; shared plumbing belongs in Services/Integrations/Abstractions. Violations:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Distinct().Order()));
    }

    /// <summary>
    /// Sanity check on the guard itself: if the namespace names above ever
    /// drift (a rename, a move), the two tests above would pass vacuously by
    /// scanning nothing. This makes that failure mode loud.
    /// </summary>
    [Fact]
    public void The_guard_actually_has_types_to_scan()
    {
        Assert.NotEmpty(TypesIn(SourceAgnosticNamespaces));

        foreach (var (vendor, namespaces) in Vendors)
        {
            Assert.True(TypesIn(namespaces).Count > 0, $"No types found for vendor '{vendor}' — has it been renamed or moved?");
        }
    }

    private static List<Type> TypesIn(string[] namespaces) =>
        ApplicationAssembly.GetTypes()
            .Where(t => t.Namespace is not null && namespaces.Any(n => IsInNamespace(t, n)))
            .ToList();

    private static bool IsInNamespace(Type type, string ns) =>
        type.Namespace is not null
        && (type.Namespace == ns || type.Namespace.StartsWith(ns + ".", StringComparison.Ordinal));

    private static string? VendorOf(Type type) =>
        Vendors.FirstOrDefault(v => v.Namespaces.Any(n => IsInNamespace(type, n))).Vendor;

    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Every type reachable from <paramref name="type"/>'s signatures,
    /// flattening generic arguments, arrays and by-ref wrappers so that
    /// e.g. Task&lt;IReadOnlyList&lt;ClickUpTaskDto&gt;&gt; is caught.
    /// </summary>
    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        var referenced = new List<Type>();

        if (type.BaseType is not null)
        {
            referenced.Add(type.BaseType);
        }

        referenced.AddRange(type.GetInterfaces());
        referenced.AddRange(type.GetFields(AllMembers).Select(f => f.FieldType));
        referenced.AddRange(type.GetProperties(AllMembers).Select(p => p.PropertyType));

        foreach (var method in type.GetMethods(AllMembers))
        {
            referenced.Add(method.ReturnType);
            referenced.AddRange(method.GetParameters().Select(p => p.ParameterType));
        }

        foreach (var constructor in type.GetConstructors(AllMembers))
        {
            referenced.AddRange(constructor.GetParameters().Select(p => p.ParameterType));
        }

        return referenced.SelectMany(Flatten).Distinct();
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        var element = type;
        while (element.IsArray || element.IsByRef || element.IsPointer)
        {
            element = element.GetElementType()!;
        }

        // An open generic parameter (the T in RawEntity<T>) names no concrete
        // type and belongs to no namespace — it is not a reference to anything.
        if (element.IsGenericParameter)
        {
            yield break;
        }

        yield return element;

        if (element.IsGenericType)
        {
            foreach (var argument in element.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }
    }
}
