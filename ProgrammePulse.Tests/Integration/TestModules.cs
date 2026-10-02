using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Switches benched modules on for a tenant through the same repository the
/// Settings → Modules toolbox writes to, for tests about a module's own pages
/// (roles, rendering, isolation). <see cref="OnAsync"/> switches them back off
/// when disposed, so a shared database (the Northstar demo above all, which
/// people look at) keeps the default: everything benched. The bench itself is
/// tested by ModuleBenchIntegrationTests, which starts with everything off.
/// </summary>
public static class TestModules
{
    /// <summary>Switches the modules on (all of them when none are named) until the result is disposed.</summary>
    public static async Task<IAsyncDisposable> OnAsync(IServiceProvider services, Guid tenantId, params string[] modules)
    {
        var keys = modules.Length == 0 ? ProductModules.All.Select(m => m.Key).ToArray() : modules;
        using var scope = services.CreateScope();
        var switches = scope.ServiceProvider.GetRequiredService<IModuleSwitchRepository>();
        var alreadyOn = await switches.GetSwitchedOnAsync(tenantId);
        foreach (var module in keys)
        {
            await switches.SetAsync(tenantId, module, on: true, actorMemberId: null, DateTime.UtcNow);
        }

        return new SwitchBack(services, tenantId, keys.Where(k => !alreadyOn.Contains(k)).ToArray());
    }

    /// <summary>Switches modules on and leaves them on: for a tenant a test created for itself and nobody else sees.</summary>
    public static async Task SwitchOnForTestTenantAsync(IServiceProvider services, Guid tenantId, params string[] modules) =>
        _ = await OnAsync(services, tenantId, modules);

    private sealed class SwitchBack(IServiceProvider services, Guid tenantId, string[] modules) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            using var scope = services.CreateScope();
            var switches = scope.ServiceProvider.GetRequiredService<IModuleSwitchRepository>();
            foreach (var module in modules)
            {
                await switches.SetAsync(tenantId, module, on: false, actorMemberId: null, DateTime.UtcNow);
            }
        }
    }
}
