using System.Reflection;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Integrations.AzureDevOps;
using ProgrammePulse.Services.SkillsEvidence;

namespace ProgrammePulse.Tests.SkillsEvidence.AzureDevOps;

/// <summary>
/// The structural rules the second forge must keep, enforced by
/// reflection so a later change breaks the build rather than the promise.
///
/// Kept in this folder rather than added to SourceIndependenceTests or
/// EvidenceContractTests: both are shared, another agent is active in the
/// repository, and — the precedent EvidenceContractTests set — a small
/// duplication beats a merge conflict in an architecture test.
/// </summary>
public sealed class AzureDevOpsBoundaryTests
{
    private static readonly string[] AzureDevOpsNamespaces =
    [
        "ProgrammePulse.Services.Integrations.AzureDevOps",
        "ProgrammePulse.Models.Integrations.AzureDevOps"
    ];

    private static readonly string[] GitHubNamespaces =
    [
        "ProgrammePulse.Services.Integrations.GitHub",
        "ProgrammePulse.Models.Integrations.GitHub"
    ];

    private static readonly string[] NeutralNamespaces =
    [
        "ProgrammePulse.Models.SkillsEvidence",
        "ProgrammePulse.Services.SkillsEvidence"
    ];

    private static readonly Assembly App = typeof(EngineeringEvidence).Assembly;

    [Fact]
    public void Azure_devops_never_references_github()
    {
        AssertNoReferences(from: AzureDevOpsNamespaces, to: GitHubNamespaces,
            "Azure DevOps must not borrow GitHub's types; each forge stands alone below the evidence contract.");
    }

    [Fact]
    public void Github_never_references_azure_devops()
    {
        AssertNoReferences(from: GitHubNamespaces, to: AzureDevOpsNamespaces,
            "GitHub must not depend on Azure DevOps.");
    }

    [Fact]
    public void The_evidence_contract_never_references_azure_devops()
    {
        AssertNoReferences(from: NeutralNamespaces, to: AzureDevOpsNamespaces,
            "Adding a forge must need no change above Bronze.");
    }

    [Fact]
    public void The_guard_has_azure_devops_types_to_scan()
    {
        Assert.NotEmpty(TypesIn(AzureDevOpsNamespaces));
        Assert.NotEmpty(TypesIn(GitHubNamespaces));
    }

    /// <summary>The same firewall the GitHub services are held to: activity can never raise a proficiency.</summary>
    [Fact]
    public void No_azure_devops_service_can_reach_the_assertion_lifecycle()
    {
        foreach (var type in TypesIn(AzureDevOpsNamespaces))
        {
            var dependencies = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType)
                .ToArray();

            Assert.DoesNotContain(typeof(ISkillAssertionService), dependencies);
            Assert.DoesNotContain(typeof(ISkillsEvidenceRepository), dependencies);
        }
    }

    private static void AssertNoReferences(string[] from, string[] to, string rule)
    {
        var violations = new List<string>();

        foreach (var type in TypesIn(from))
        {
            foreach (var referenced in ReferencedTypes(type))
            {
                if (referenced.Namespace is { } ns && to.Any(t => ns == t || ns.StartsWith(t + ".", StringComparison.Ordinal)))
                {
                    violations.Add($"{type.FullName} references {referenced.FullName}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            rule + Environment.NewLine + string.Join(Environment.NewLine, violations.Distinct().Order()));
    }

    private static IReadOnlyList<Type> TypesIn(string[] namespaces) =>
        App.GetTypes()
            .Where(t => t.Namespace is { } ns && namespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal)))
            .ToList();

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(All)) yield return Unwrap(field.FieldType);
        foreach (var property in type.GetProperties(All)) yield return Unwrap(property.PropertyType);

        foreach (var constructor in type.GetConstructors(All))
        {
            foreach (var parameter in constructor.GetParameters()) yield return Unwrap(parameter.ParameterType);
        }

        foreach (var method in type.GetMethods(All))
        {
            yield return Unwrap(method.ReturnType);
            foreach (var parameter in method.GetParameters()) yield return Unwrap(parameter.ParameterType);
        }

        if (type.BaseType is { } baseType) yield return baseType;
        foreach (var implemented in type.GetInterfaces()) yield return implemented;
    }

    /// <summary>Task&lt;IReadOnlyList&lt;T&gt;&gt; hides T from a plain namespace check; look through generics and arrays.</summary>
    private static Type Unwrap(Type type)
    {
        while (true)
        {
            if (type.HasElementType)
            {
                type = type.GetElementType()!;
                continue;
            }

            if (type.IsGenericType && type.GetGenericArguments().Length > 0)
            {
                var inner = type.GetGenericArguments().Last();
                if (inner.Namespace?.StartsWith("ProgrammePulse", StringComparison.Ordinal) == true || inner.IsGenericType)
                {
                    type = inner;
                    continue;
                }
            }

            return type;
        }
    }
}
