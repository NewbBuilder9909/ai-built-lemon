using ProgrammePulse.Models.Tenancy;

namespace ProgrammePulse.Services.Tenancy;

/// <summary>
/// "Has this tenant's Admin switched this module on?" Asked by
/// [RequireModule] before an action and by the layout and pages before
/// showing a link. Off unless a switch row exists; an unresolved tenant has
/// every module off. Request-scoped, so the layout, the page and the filter
/// share one read.
/// </summary>
public interface IModuleGate
{
    Task<bool> IsOnAsync(string moduleKey);

    Task<IReadOnlySet<string>> GetSwitchedOnAsync();
}

public sealed class ModuleGate(ITenantContext tenantContext, IModuleSwitchRepository repository) : IModuleGate
{
    private IReadOnlySet<string>? _switchedOn;

    public async Task<bool> IsOnAsync(string moduleKey) => (await GetSwitchedOnAsync()).Contains(moduleKey);

    public async Task<IReadOnlySet<string>> GetSwitchedOnAsync()
    {
        if (_switchedOn is not null)
        {
            return _switchedOn;
        }

        await tenantContext.EnsureResolvedAsync();
        _switchedOn = tenantContext.IsResolved && tenantContext.CurrentTenantId is { } tenantId
            ? Known(await repository.GetSwitchedOnAsync(tenantId))
            : new HashSet<string>(StringComparer.Ordinal);
        return _switchedOn;
    }

    /// <summary>A stored key for a module that no longer exists switches nothing on.</summary>
    public static IReadOnlySet<string> Known(IEnumerable<string> keys) =>
        new HashSet<string>(keys.Where(k => ProductModules.Find(k) is not null), StringComparer.Ordinal);
}
