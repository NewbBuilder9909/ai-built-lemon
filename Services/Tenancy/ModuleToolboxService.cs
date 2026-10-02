using System.Text.Json;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.Tenancy;

/// <summary>One row of the module toolbox.</summary>
public sealed record ModuleToolboxRow(ProductModule Module, bool IsOn, bool InPlan, bool AvailableOnDeployment)
{
    /// <summary>Whether switching it on would show anything: the plan includes it and the deployment allows it.</summary>
    public bool CanSwitchOn => InPlan && AvailableOnDeployment;
}

public interface IModuleToolboxService
{
    Task<IReadOnlyList<ModuleToolboxRow>> ListAsync(Guid tenantId, Func<string, bool> availableOnDeployment);

    Task<CommandResult> SetAsync(Guid tenantId, string? moduleKey, bool on, int? actorMemberId, Func<string, bool> availableOnDeployment);
}

/// <summary>
/// The only way a benched module comes on (Settings → Modules). Switching a
/// module on is refused when the plan doesn't include it or the deployment
/// doesn't allow it, because the switch would show nothing. Switching off is
/// always allowed. Every change is audited.
/// </summary>
public sealed class ModuleToolboxService(
    IModuleSwitchRepository repository,
    IFeatureGate featureGate,
    IStaffAuditLogRepository auditLog,
    TimeProvider timeProvider) : IModuleToolboxService
{
    public async Task<IReadOnlyList<ModuleToolboxRow>> ListAsync(Guid tenantId, Func<string, bool> availableOnDeployment)
    {
        var on = await repository.GetSwitchedOnAsync(tenantId);
        var rows = new List<ModuleToolboxRow>();
        foreach (var module in ProductModules.All)
        {
            var inPlan = module.RequiresFeature is null || await featureGate.IsEnabledAsync(module.RequiresFeature);
            rows.Add(new ModuleToolboxRow(module, on.Contains(module.Key), inPlan, availableOnDeployment(module.Key)));
        }

        return rows;
    }

    public async Task<CommandResult> SetAsync(Guid tenantId, string? moduleKey, bool on, int? actorMemberId, Func<string, bool> availableOnDeployment)
    {
        var module = ProductModules.Find(moduleKey);
        if (module is null)
        {
            return CommandResult.Refused("That module doesn't exist.");
        }

        if (on)
        {
            if (module.RequiresFeature is { } feature && !await featureGate.IsEnabledAsync(feature))
            {
                return CommandResult.Refused($"{module.Name} isn't in your plan, so switching it on would show nothing.");
            }

            if (!availableOnDeployment(module.Key))
            {
                return CommandResult.Refused($"{module.Name} isn't available on this deployment.");
            }
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        await repository.SetAsync(tenantId, module.Key, on, actorMemberId, nowUtc);
        await auditLog.LogAsync(
            "Module", module.Key, on ? "ModuleSwitchedOn" : "ModuleSwitchedOff", actorMemberId,
            JsonSerializer.Serialize(new { module = module.Key, on }), nowUtc, tenantId);

        return CommandResult.Succeeded($"{module.Name} is {(on ? "on" : "off")}.");
    }
}
