using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// Makes "every /staffops action is gated" a build-time fact rather than a
/// reviewing habit. Access to an action is now declared in its signature
/// ([RequireCapability], [CurrentTenant]), so reflection can see it. Before
/// that, the gate was the first statement of each body, and only a source
/// scan or a human could.
///
/// An action with no declared capability gate must appear in
/// <see cref="Exemptions"/> with the reason it gates itself (or needs no
/// gate). The list is checked in both directions: a new ungated action
/// fails, and so does an exemption for an action that no longer exists or
/// has since gained an attribute, so the list cannot rot into a blanket pass.
/// </summary>
public class ControllerGateTests
{
    /// <summary>Whole controllers whose gate lives somewhere other than an attribute.</summary>
    private static readonly Dictionary<Type, string> ExemptControllers = new()
    {
        [typeof(StaffAccountController)] = "Sign-in, MFA and sign-out run before a member has any capability.",
        [typeof(StaffExecutiveReviewController)] = "Service-enforced: ExecutiveReviewService throws UnauthorizedAccessException, mapped to 403 by Guard.",
        [typeof(StaffExecutiveDecisionController)] = "Service-enforced, same pattern as StaffExecutiveReviewController.",
    };

    /// <summary>Individual actions that gate imperatively, each with its reason.</summary>
    private static readonly Dictionary<string, string> Exemptions = new()
    {
        ["StaffBrandingController.ThemeCss"] = "Deliberately ungated: serves already-published colours to anonymous pages.",
        ["StaffEvidenceConnectionController.GitHubCallback"] = "OAuth callback: capability checked alongside the signed state and nonce.",
        ["StaffOAuthConnectionController.JiraCallback"] = "OAuth callback: capability checked alongside the signed state and nonce.",
        ["StaffOAuthConnectionController.TempoCallback"] = "OAuth callback: capability checked alongside the signed state and nonce.",
        ["StaffPortalController.Index"] = "Self-service home: an unauthorised caller is redirected to login, not refused.",
        ["StaffPortalController.MyWork"] = "Self-service: redirects to login rather than refusing.",
        ["StaffPortalController.Home"] = "Post-sign-in landing: redirects to the first page the caller's capabilities open, or to login when there are none (StaffLandingPage).",
        ["StaffPortalController.Start"] = "Start page for a signed-in member whose role has nothing switched on; redirects to login when signed out.",
        ["StaffSkillsController.Index"] = "Self-service: redirects to login rather than refusing.",
    };

    [Fact]
    public void Every_staffops_action_declares_a_capability_gate_or_a_documented_exemption()
    {
        var ungated = StaffActions()
            .Where(a => !ExemptControllers.ContainsKey(a.Controller))
            .Where(a => !HasCapabilityGate(a))
            .Select(a => a.Key)
            .Where(key => !Exemptions.ContainsKey(key))
            .Distinct()
            .ToList();

        Assert.True(ungated.Count == 0,
            "These actions declare no [RequireCapability] and have no documented exemption: " + string.Join(", ", ungated));
    }

    [Fact]
    public void Every_exemption_names_a_real_action_that_is_still_ungated()
    {
        var actions = StaffActions().ToLookup(a => a.Key);

        foreach (var (key, _) in Exemptions)
        {
            Assert.True(actions.Contains(key), $"Exemption '{key}' names no action — remove it.");
            Assert.False(actions[key].Any(HasCapabilityGate), $"Exemption '{key}' now declares [RequireCapability] — remove the exemption.");
        }
    }

    [Fact]
    public void The_scan_actually_finds_gated_actions()
    {
        // Guards against the scan silently matching nothing (renamed
        // namespace, changed base class) and passing vacuously.
        Assert.True(StaffActions().Count(HasCapabilityGate) > 100);
    }

    [Fact]
    public void Every_required_capability_is_a_known_capability()
    {
        var unknown = StaffActions()
            .SelectMany(a => a.Controller.GetCustomAttributes<RequireCapabilityAttribute>()
                .Concat(a.Method.GetCustomAttributes<RequireCapabilityAttribute>())
                .Select(attribute => (a.Key, attribute.Capability)))
            .Where(pair => !Capability.All.Contains(pair.Capability))
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void Every_current_tenant_parameter_is_a_non_nullable_guid()
    {
        // A Guid? would suggest the action handles "no tenant" itself, which
        // it never sees: CurrentTenantFilter refuses first.
        var wrong = StaffActions()
            .SelectMany(a => a.Method.GetParameters()
                .Where(p => p.GetCustomAttribute<CurrentTenantAttribute>() is not null && p.ParameterType != typeof(Guid))
                .Select(p => $"{a.Key}({p.ParameterType.Name} {p.Name})"))
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void The_current_tenant_binding_source_never_reads_the_request()
    {
        // The property that makes [CurrentTenant] safe: a ?tenantId= query
        // value or form field is never considered.
        Assert.False(CurrentTenantAttribute.Source.IsFromRequest);
        Assert.Equal(CurrentTenantAttribute.Source, new CurrentTenantAttribute().BindingSource);
    }

    [Fact]
    public void Every_staffops_post_validates_its_antiforgery_token()
    {
        var unprotected = StaffActions()
            .Where(a => a.Method.GetCustomAttributes<HttpPostAttribute>().Any())
            .Where(a => !a.Method.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Any()
                     && !a.Controller.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Any())
            .Select(a => a.Key)
            .ToList();

        Assert.True(unprotected.Count == 0, "POST actions without [ValidateAntiForgeryToken]: " + string.Join(", ", unprotected));
    }

    private sealed record StaffAction(Type Controller, MethodInfo Method)
    {
        public string Key => $"{Controller.Name}.{Method.Name}";
    }

    private static IEnumerable<StaffAction> StaffActions() =>
        typeof(StaffReportingController).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(StaffReportingController).Namespace
                     && t.Name.StartsWith("Staff", StringComparison.Ordinal)
                     && typeof(Controller).IsAssignableFrom(t)
                     && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null)
                .Select(m => new StaffAction(t, m)));

    private static bool HasCapabilityGate(StaffAction action) =>
        action.Controller.GetCustomAttributes<RequireCapabilityAttribute>().Any()
        || action.Method.GetCustomAttributes<RequireCapabilityAttribute>().Any();
}
