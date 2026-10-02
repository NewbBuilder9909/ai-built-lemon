using System.Text.Json;
using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Models.ViewModels.ServiceOps;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ServiceOps;

/// <summary>
/// Provider-neutral support-desk administration: the desks page, approved
/// components, disconnecting, and the agent-mapping queue. Moved out of
/// StaffServiceController. Every change is audited.
///
/// Like evidence actors, a desk agent reaches a named person only through a
/// link approved here. The approval attributes case participation already
/// imported, so no re-sync is needed.
/// </summary>
public interface IDeskAdminService
{
    Task<DesksPageViewModel> BuildDesksPageAsync(Guid tenantId);

    Task<CommandResult> SetComponentsAsync(Guid tenantId, Guid connectionKey, string? approvedComponents, int? actorMemberId);

    Task<string> DisconnectAsync(Guid tenantId, Guid connectionKey, int? actorMemberId);

    Task<SupportLinksPageViewModel> BuildLinksPageAsync(Guid tenantId, bool canReviewRootCause);

    Task<DeskAgentsPageViewModel> BuildAgentsPageAsync(Guid tenantId);

    Task<CommandResult> ApproveAgentAsync(Guid tenantId, Guid connectionKey, string externalAgentId, string? agentName, Guid staffKey, Guid approverStaffKey, int? actorMemberId);
}

public sealed class DeskAdminService(
    IServiceOpsRepository serviceOpsRepository,
    IStaffRepository staffRepository,
    TimeProvider timeProvider) : IDeskAdminService
{
    public const string InvalidComponents = "Component names may use letters, digits, spaces, hyphens, underscores and slashes only.";

    public async Task<DesksPageViewModel> BuildDesksPageAsync(Guid tenantId) =>
        new(await serviceOpsRepository.GetConnectionsAsync(tenantId), await serviceOpsRepository.GetCoverageAsync(tenantId));

    public async Task<CommandResult> SetComponentsAsync(Guid tenantId, Guid connectionKey, string? approvedComponents, int? actorMemberId)
    {
        var existing = await serviceOpsRepository.GetConnectionAsync(connectionKey, tenantId);
        if (existing is null)
        {
            return CommandResult.NotFound;
        }

        var components = ParseComponents(approvedComponents);
        if (components is null)
        {
            return CommandResult.Refused(InvalidComponents);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await serviceOpsRepository.UpsertConnectionAsync(existing with { ApprovedComponents = components, UpdatedAtUtc = now });

        await serviceOpsRepository.LogAsync(
            ServiceOpsAuditAction.EntityTypeConnection, connectionKey.ToString(), ServiceOpsAuditAction.ComponentsApproved,
            actorMemberId, JsonSerializer.Serialize(new { components = components.Count }), now, tenantId);

        return CommandResult.Succeeded("Approved components updated. Cases already imported keep their tags until the next sync re-evaluates them.");
    }

    public async Task<string> DisconnectAsync(Guid tenantId, Guid connectionKey, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await serviceOpsRepository.SetConnectionStatusAsync(
            connectionKey, DeskConnectionStatus.Disconnected, tenantId, now, clearCredential: true);

        await serviceOpsRepository.LogAsync(
            ServiceOpsAuditAction.EntityTypeConnection, connectionKey.ToString(),
            ServiceOpsAuditAction.ConnectionDisconnected, actorMemberId, null, now, tenantId);

        return "Disconnected. The stored API key was destroyed; imported case metadata is kept but no longer updates.";
    }

    public async Task<SupportLinksPageViewModel> BuildLinksPageAsync(Guid tenantId, bool canReviewRootCause)
    {
        var desks = (await serviceOpsRepository.GetConnectionsAsync(tenantId))
            .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new DeskOption(c.ConnectionKey, c.DisplayName, c.Status == DeskConnectionStatus.Disconnected))
            .ToList();
        return new(await serviceOpsRepository.GetLinksAsync(tenantId), canReviewRootCause, desks);
    }

    public async Task<DeskAgentsPageViewModel> BuildAgentsPageAsync(Guid tenantId)
    {
        var roster = await staffRepository.GetByTenantAsync(tenantId);
        return new DeskAgentsPageViewModel(
            await serviceOpsRepository.GetUnmappedAgentsAsync(tenantId),
            roster.Where(s => s.IsActive).OrderBy(s => s.FullName).ToList(),
            await serviceOpsRepository.GetConnectionsAsync(tenantId));
    }

    public async Task<CommandResult> ApproveAgentAsync(Guid tenantId, Guid connectionKey, string externalAgentId, string? agentName, Guid staffKey, Guid approverStaffKey, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (await serviceOpsRepository.GetConnectionAsync(connectionKey, tenantId) is null)
        {
            return CommandResult.NotFound;
        }

        // The subject must be on the caller's own roster. A staff key
        // from another tenant reads as not found, never as forbidden.
        var roster = await staffRepository.GetByTenantAsync(tenantId);
        if (roster.All(s => s.StaffKey != staffKey))
        {
            return CommandResult.NotFound;
        }

        // Only an agent the desk actually reported, and still unmapped, can be
        // approved; its name comes from the desk, not the form. Otherwise an
        // approver could mint a link for an id no ticket ever carried, or
        // relabel one (Aikido: business logic bypass).
        var queued = (await serviceOpsRepository.GetUnmappedAgentsAsync(tenantId))
            .FirstOrDefault(a => string.Equals(a.ExternalAgentId, externalAgentId, StringComparison.Ordinal));
        if (queued.ExternalAgentId is null)
        {
            return CommandResult.NotFound;
        }

        agentName = queued.DisplayName;

        try
        {
            await serviceOpsRepository.CreateAgentLinkAsync(new DeskAgentLink
            {
                LinkKey = Guid.NewGuid(),
                TenantId = tenantId,
                ConnectionKey = connectionKey,
                Provider = DeskHostPolicy.FreshdeskProvider,
                ExternalAgentId = externalAgentId,
                ExternalAgentName = agentName,
                StaffKey = staffKey,
                ApprovedByStaffKey = approverStaffKey,
                ApprovedAtUtc = now
            });
        }
        catch (ServiceOpsValidationException ex)
        {
            return CommandResult.Refused(ex.Message);
        }

        var attributed = await serviceOpsRepository.AttributeParticipationAsync(
            connectionKey, externalAgentId, staffKey, tenantId, now);

        await serviceOpsRepository.LogAsync(
            ServiceOpsAuditAction.EntityTypeAgentLink, externalAgentId, ServiceOpsAuditAction.AgentLinkApproved,
            actorMemberId, JsonSerializer.Serialize(new { staffKey, connectionKey, attributed }), now, tenantId);

        return CommandResult.Succeeded($"Approved. {attributed} case participation records are now attributed.");
    }

    /// <summary>
    /// Parses the admin's approved component list. Null means at least
    /// one entry was not a usable component name — rejected rather than
    /// silently dropped, because a component that quietly disappears
    /// makes every case tagged with it look unmapped.
    /// </summary>
    public static IReadOnlyList<string>? ParseComponents(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var parts = raw.Split([',', '\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var accepted = new List<string>();

        foreach (var part in parts)
        {
            if (!DeskHostPolicy.IsValidComponentKey(part))
            {
                return null;
            }

            var normalized = DeskHostPolicy.NormalizeComponent(part);
            if (!accepted.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                accepted.Add(normalized);
            }
        }

        return accepted.Count <= 200 ? accepted : null;
    }
}
