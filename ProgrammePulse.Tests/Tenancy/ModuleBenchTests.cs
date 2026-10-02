using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Tests.Controllers;
using ProgrammePulse.Tests.Staff;

namespace ProgrammePulse.Tests.Tenancy;

/// <summary>
/// Benched modules: off for every tenant until an Admin switches one on in
/// Settings → Modules, and then on only for that tenant. The toolbox is the
/// only way on, and it refuses a module the plan doesn't include.
/// </summary>
public class ModuleBenchTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Fact]
    public async Task Every_module_starts_off()
    {
        var gate = new ModuleGate(FakeTenantContext.ResolvedOn(TenantPlan.Enterprise, TenantA), new FakeModuleSwitchRepository());

        foreach (var module in ProductModules.All)
        {
            Assert.False(await gate.IsOnAsync(module.Key), $"{module.Key} should start off.");
        }
    }

    [Fact]
    public async Task An_unresolved_tenant_has_every_module_off()
    {
        var switches = new FakeModuleSwitchRepository();
        await switches.SetAsync(TenantA, ProductModules.Reporting, true, 1, DateTime.UtcNow);

        var gate = new ModuleGate(new FakeTenantContext { IsResolved = false }, switches);

        Assert.Empty(await gate.GetSwitchedOnAsync());
    }

    [Fact]
    public async Task A_switch_reaches_only_its_own_tenant()
    {
        var switches = new FakeModuleSwitchRepository();
        await switches.SetAsync(TenantA, ProductModules.Reporting, true, 1, DateTime.UtcNow);

        Assert.True(await new ModuleGate(FakeTenantContext.ResolvedOn(TenantPlan.Enterprise, TenantA), switches).IsOnAsync(ProductModules.Reporting));
        Assert.False(await new ModuleGate(FakeTenantContext.ResolvedOn(TenantPlan.Enterprise, TenantB), switches).IsOnAsync(ProductModules.Reporting));
    }

    [Fact]
    public void A_stored_key_for_a_module_that_no_longer_exists_switches_nothing_on()
    {
        Assert.Equal([ProductModules.Skills], ModuleGate.Known(["retired-module", ProductModules.Skills]));
    }

    [Fact]
    public void Module_keys_are_unique_and_every_required_feature_is_a_real_plan_feature()
    {
        Assert.Equal(ProductModules.All.Count, ProductModules.All.Select(m => m.Key).Distinct().Count());
        Assert.All(ProductModules.All.Where(m => m.RequiresFeature is not null),
            m => Assert.Contains(m.RequiresFeature!, ProductFeature.All));
    }

    [Fact]
    public async Task The_toolbox_switches_a_module_on_and_off_and_audits_both()
    {
        var switches = new FakeModuleSwitchRepository();
        var audit = new FakeStaffAuditLogRepository();
        var toolbox = new ModuleToolboxService(switches, new FakeFeatureGate(ProductFeature.ReportingHub), audit, TimeProvider.System);

        var on = await toolbox.SetAsync(TenantA, ProductModules.Reporting, true, 7, _ => true);
        Assert.Equal(CommandStatus.Succeeded, on.Status);
        Assert.Contains(ProductModules.Reporting, await switches.GetSwitchedOnAsync(TenantA));

        var off = await toolbox.SetAsync(TenantA, ProductModules.Reporting, false, 7, _ => true);
        Assert.Equal(CommandStatus.Succeeded, off.Status);
        Assert.Empty(await switches.GetSwitchedOnAsync(TenantA));

        Assert.Equal(["ModuleSwitchedOn", "ModuleSwitchedOff"], audit.Entries.Select(e => e.Action));
        Assert.All(audit.Entries, e => Assert.Equal(7, e.ActorMemberId));
    }

    [Fact]
    public async Task The_toolbox_refuses_a_module_the_plan_does_not_include()
    {
        var switches = new FakeModuleSwitchRepository();
        var toolbox = new ModuleToolboxService(switches, new FakeFeatureGate(), new FakeStaffAuditLogRepository(), TimeProvider.System);

        var result = await toolbox.SetAsync(TenantA, ProductModules.Contracts, true, 7, _ => true);

        Assert.Equal(CommandStatus.Refused, result.Status);
        Assert.Empty(await switches.GetSwitchedOnAsync(TenantA));
    }

    [Fact]
    public async Task The_toolbox_refuses_a_module_the_deployment_does_not_allow_and_an_unknown_key()
    {
        var switches = new FakeModuleSwitchRepository();
        var toolbox = new ModuleToolboxService(switches, new FakeFeatureGate(ProductFeature.ReportingHub), new FakeStaffAuditLogRepository(), TimeProvider.System);

        Assert.Equal(CommandStatus.Refused, (await toolbox.SetAsync(TenantA, ProductModules.ExecutiveReview, true, 7, _ => false)).Status);
        Assert.Equal(CommandStatus.Refused, (await toolbox.SetAsync(TenantA, "not-a-module", true, 7, _ => true)).Status);
        Assert.Empty(await switches.GetSwitchedOnAsync(TenantA));
    }

    [Fact]
    public async Task A_module_can_always_be_switched_off_even_when_the_plan_no_longer_includes_it()
    {
        var switches = new FakeModuleSwitchRepository();
        await switches.SetAsync(TenantA, ProductModules.Contracts, true, 7, DateTime.UtcNow);
        var toolbox = new ModuleToolboxService(switches, new FakeFeatureGate(), new FakeStaffAuditLogRepository(), TimeProvider.System);

        var result = await toolbox.SetAsync(TenantA, ProductModules.Contracts, false, 7, _ => true);

        Assert.Equal(CommandStatus.Succeeded, result.Status);
        Assert.Empty(await switches.GetSwitchedOnAsync(TenantA));
    }

    [Fact]
    public async Task The_filter_answers_404_while_the_module_is_off_and_lets_the_request_through_once_on()
    {
        var switches = new FakeModuleSwitchRepository();
        var tenant = FakeTenantContext.ResolvedOn(TenantPlan.Enterprise, TenantA);

        var refused = await RunFilterAsync(new RequireModuleFilter(ProductModules.Skills, new ModuleGate(tenant, switches)));
        var view = Assert.IsType<ViewResult>(refused);
        Assert.Equal(StatusCodes.Status404NotFound, view.StatusCode);
        Assert.Equal(RequireModuleFilter.ViewPath, view.ViewName);

        await switches.SetAsync(TenantA, ProductModules.Skills, true, 1, DateTime.UtcNow);
        Assert.Null(await RunFilterAsync(new RequireModuleFilter(ProductModules.Skills, new ModuleGate(tenant, switches))));
    }

    private static async Task<IActionResult?> RunFilterAsync(IAsyncActionFilter filter)
    {
        var context = new ActionExecutingContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            [], new Dictionary<string, object?>(), controller: null!);
        var reached = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            reached = true;
            return Task.FromResult(new ActionExecutedContext(context, [], controller: null!));
        });
        return reached ? null : context.Result;
    }
}

public sealed class FakeModuleSwitchRepository : IModuleSwitchRepository
{
    private readonly HashSet<(Guid Tenant, string Module)> _on = [];

    public Task<IReadOnlySet<string>> GetSwitchedOnAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlySet<string>>(_on.Where(s => s.Tenant == tenantId).Select(s => s.Module).ToHashSet(StringComparer.Ordinal));

    public Task SetAsync(Guid tenantId, string moduleKey, bool on, int? actorMemberId, DateTime nowUtc)
    {
        if (on)
        {
            _on.Add((tenantId, moduleKey));
        }
        else
        {
            _on.Remove((tenantId, moduleKey));
        }

        return Task.CompletedTask;
    }
}
