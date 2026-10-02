using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using ProgrammePulse.Controllers;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// The public contract of /staffops: every endpoint's HTTP verb and URL, the
/// capabilities it requires, whether it is tenant-scoped and whether it
/// validates an antiforgery token. Compared against a checked-in snapshot
/// (staffops-routes.txt).
///
/// Which controller class an endpoint lives in is deliberately *not* part of
/// the contract, so a controller can be split without the snapshot moving.
/// That split is the Phase 1 refactor this guards: "routes unchanged" is a
/// diff here, not a claim. Any real change to a URL or a gate (a new page, a
/// capability swap) fails, and the snapshot is updated in the same change so
/// the diff is reviewed:
///   PP_UPDATE_ROUTE_CONTRACT=1 dotnet test --filter RouteContractTests
/// </summary>
public class RouteContractTests
{
    private const string UpdateVariable = "PP_UPDATE_ROUTE_CONTRACT";
    private static readonly string SnapshotPath =
        Path.Combine(SourceTree.Root, "ProgrammePulse.Tests", "Architecture", "staffops-routes.txt");

    [Fact]
    public void Staffops_routes_and_their_gates_match_the_snapshot()
    {
        var actual = Contract();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllLines(SnapshotPath, actual);
            return;
        }

        Assert.True(File.Exists(SnapshotPath), $"No snapshot at {SnapshotPath}; run with {UpdateVariable}=1 to create it.");
        var expected = File.ReadAllLines(SnapshotPath).Where(l => l.Length > 0).ToList();

        var missing = expected.Except(actual).ToList();
        var added = actual.Except(expected).ToList();
        Assert.True(missing.Count == 0 && added.Count == 0,
            $"The /staffops contract changed. If intended, re-run with {UpdateVariable}=1 and review the diff." + Environment.NewLine
            + "Removed or changed:" + Environment.NewLine + string.Join(Environment.NewLine, missing.Select(l => "  - " + l)) + Environment.NewLine
            + "Added or changed:" + Environment.NewLine + string.Join(Environment.NewLine, added.Select(l => "  + " + l)));
    }

    [Fact]
    public void Every_staffops_endpoint_is_unique()
    {
        var duplicates = Endpoints().GroupBy(e => $"{e.Verb} {e.Path}").Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.True(duplicates.Count == 0, "Ambiguous routes: " + string.Join(", ", duplicates));
    }

    private static List<string> Contract() =>
        Endpoints()
            .Select(e => $"{e.Verb,-4} {e.Path} | caps: {(e.Capabilities.Length == 0 ? "-" : string.Join(",", e.Capabilities))} | tenant: {(e.Tenant ? "yes" : "no")} | antiforgery: {(e.Antiforgery ? "yes" : "no")} | module: {e.Module ?? "-"}")
            .Order(StringComparer.Ordinal)
            .ToList();

    private sealed record Endpoint(string Verb, string Path, string[] Capabilities, bool Tenant, bool Antiforgery, string? Module);

    private static IEnumerable<Endpoint> Endpoints()
    {
        var controllers = typeof(StaffReportingController).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(StaffReportingController).Namespace
                     && t.Name.StartsWith("Staff", StringComparison.Ordinal)
                     && typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var controller in controllers)
        {
            var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            var classCapabilities = controller.GetCustomAttributes<RequireCapabilityAttribute>().Select(a => a.Capability);
            var classAntiforgery = controller.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Any();
            var classModule = controller.GetCustomAttribute<RequireModuleAttribute>()?.Module;

            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null))
            {
                var capabilities = classCapabilities.Concat(method.GetCustomAttributes<RequireCapabilityAttribute>().Select(a => a.Capability))
                    .Distinct().Order(StringComparer.Ordinal).ToArray();
                var tenant = method.GetParameters().Any(p => p.GetCustomAttribute<CurrentTenantAttribute>() is not null);
                var antiforgery = classAntiforgery || method.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Any();
                var module = method.GetCustomAttribute<RequireModuleAttribute>()?.Module ?? classModule;

                foreach (var http in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    foreach (var verb in http.HttpMethods)
                    {
                        yield return new Endpoint(verb, Combine(prefix, http.Template), capabilities, tenant, antiforgery, module);
                    }
                }
            }
        }
    }

    private static string Combine(string prefix, string? template)
    {
        if (template is not null && (template.StartsWith('/') || template.StartsWith("~/", StringComparison.Ordinal)))
        {
            return "/" + template.TrimStart('~', '/');
        }

        var path = string.IsNullOrEmpty(template) ? prefix : $"{prefix.TrimEnd('/')}/{template}";
        return "/" + path.Trim('/');
    }
}
