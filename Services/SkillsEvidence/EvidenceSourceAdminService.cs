using System.Text.Json;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.ViewModels.SkillsEvidence;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// Provider-neutral administration of engineering evidence sources: the
/// sources page, disconnecting, and the identity-mapping queue that decides
/// which external account is which person. Moved out of
/// StaffEvidenceConnectionController.
///
/// Evidence reaches a named person only through an approved link made here
/// (see EvidenceActorLink). An approval attributes evidence already ingested;
/// a revocation returns it to unmapped rather than deleting it. Every
/// decision is audited.
/// </summary>
public interface IEvidenceSourceAdminService
{
    Task<EvidenceConnectionsPageViewModel> BuildConnectionsPageAsync(Guid tenantId, bool appConfigured);

    Task<string> DisconnectAsync(Guid tenantId, Guid connectionKey, int? actorMemberId);

    Task<EvidenceActorsPageViewModel> BuildActorsPageAsync(Guid tenantId, PageRequest page);

    Task<CommandResult> ApproveActorAsync(Guid tenantId, Guid unmappedActorKey, Guid staffKey, Guid approverStaffKey, int? actorMemberId);

    Task<CommandResult> RevokeActorLinkAsync(Guid tenantId, Guid linkKey, Guid connectionKey, string externalActorId, int? actorMemberId);

    /// <summary>
    /// The repositories this tenant's connections are set to read, as plain
    /// (provider, account, repository) values, for the repository-link gap
    /// report in Programme Ops. Disconnected connections read nothing and are
    /// left out. Plain values so neither area references the other's types.
    /// </summary>
    Task<IReadOnlyList<(string Provider, string SourceAccountId, string RepositoryKey)>> GetSelectedRepositoriesAsync(Guid tenantId);
}

public sealed class EvidenceSourceAdminService(
    IEngineeringEvidenceRepository evidenceRepository,
    IEvidencePortfolioQueryService portfolioQueryService,
    IStaffRepository staffRepository,
    ISkillsEvidenceAuditLogRepository auditLog,
    TimeProvider timeProvider) : IEvidenceSourceAdminService
{
    public async Task<EvidenceConnectionsPageViewModel> BuildConnectionsPageAsync(Guid tenantId, bool appConfigured) =>
        new(await evidenceRepository.GetConnectionsAsync(tenantId), await portfolioQueryService.BuildCoverageAsync(tenantId), appConfigured);

    public async Task<string> DisconnectAsync(Guid tenantId, Guid connectionKey, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // The credential is destroyed. Evidence already collected is
        // kept — it was true when collected — but every stream goes
        // out of scope so nothing still reads as current coverage.
        await evidenceRepository.SetConnectionStatusAsync(
            connectionKey, EvidenceConnectionStatus.Disconnected, tenantId, now, clearCredential: true);
        await evidenceRepository.MarkCoverageOutOfScopeAsync(connectionKey, [], tenantId, now);

        await LogAsync(SkillsEvidenceAuditAction.EntityTypeConnection, connectionKey.ToString(),
            SkillsEvidenceAuditAction.ConnectionDisconnected, null, tenantId, actorMemberId);

        return "Disconnected. The stored credential was destroyed; collected evidence is kept but no longer counts as current coverage.";
    }

    public async Task<IReadOnlyList<(string Provider, string SourceAccountId, string RepositoryKey)>> GetSelectedRepositoriesAsync(Guid tenantId) =>
        (await evidenceRepository.GetConnectionsAsync(tenantId))
            .Where(connection => connection.Status != EvidenceConnectionStatus.Disconnected)
            .SelectMany(connection => connection.SelectedRepositories
                .Select(repository => (connection.Provider, connection.SourceAccountId, repository)))
            .ToList();

    public async Task<EvidenceActorsPageViewModel> BuildActorsPageAsync(Guid tenantId, PageRequest page)
    {
        var unmapped = await evidenceRepository.GetUnmappedActorsPageAsync(tenantId, page);
        var roster = await staffRepository.GetByTenantAsync(tenantId);
        return new EvidenceActorsPageViewModel(
            unmapped.Items,
            roster.Where(s => s.IsActive).OrderBy(s => s.FullName).ToList(),
            await evidenceRepository.GetConnectionsAsync(tenantId),
            unmapped.Links,
            await evidenceRepository.CountOpenUnmappedActorsAsync(tenantId, includeBots: false));
    }

    public async Task<CommandResult> ApproveActorAsync(Guid tenantId, Guid unmappedActorKey, Guid staffKey, Guid approverStaffKey, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var actor = await evidenceRepository.GetUnmappedActorAsync(unmappedActorKey, tenantId);
        if (actor is null)
        {
            return CommandResult.NotFound;
        }

        // The subject must be on the caller's own roster. A staff key from
        // another tenant reads as not found, never as forbidden.
        var roster = await staffRepository.GetByTenantAsync(tenantId);
        if (roster.All(s => s.StaffKey != staffKey))
        {
            return CommandResult.NotFound;
        }

        if (actor.IsBot)
        {
            return CommandResult.Refused("That account is classified as a bot. Bots are excluded from person attribution.");
        }

        try
        {
            await evidenceRepository.CreateActorLinkAsync(new EvidenceActorLink
            {
                LinkKey = Guid.NewGuid(),
                TenantId = tenantId,
                ConnectionKey = actor.ConnectionKey,
                Provider = actor.Provider,
                ExternalActorId = actor.ExternalActorId,
                ExternalLogin = actor.ExternalLogin,
                StaffKey = staffKey,
                ApprovedByStaffKey = approverStaffKey,
                ApprovedAtUtc = now
            });
        }
        catch (SkillAssertionValidationException ex)
        {
            return CommandResult.Refused(ex.Message);
        }

        // Evidence already ingested is attributed now, so approving does
        // not require a re-sync.
        var attributed = await evidenceRepository.AttributeEvidenceToStaffAsync(
            actor.ConnectionKey, actor.ExternalActorId, staffKey, tenantId, now);

        await evidenceRepository.MarkUnmappedResolvedAsync(unmappedActorKey, tenantId, now);

        await LogAsync(SkillsEvidenceAuditAction.EntityTypeActorLink, actor.ExternalActorId,
            SkillsEvidenceAuditAction.ActorLinkApproved,
            JsonSerializer.Serialize(new { staffKey, actor.ConnectionKey, attributed }), tenantId, actorMemberId);

        return CommandResult.Succeeded($"Approved. {attributed} existing evidence rows are now attributed.");
    }

    public async Task<CommandResult> RevokeActorLinkAsync(Guid tenantId, Guid linkKey, Guid connectionKey, string externalActorId, int? actorMemberId)
    {
        if (await evidenceRepository.GetConnectionAsync(connectionKey, tenantId) is null)
        {
            return CommandResult.NotFound;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await evidenceRepository.DeleteActorLinkAsync(linkKey, tenantId);

        // Evidence goes back to unmapped rather than being deleted: it was
        // really observed, it simply stops being anybody's.
        var detached = await evidenceRepository.DetachEvidenceFromStaffAsync(connectionKey, externalActorId, tenantId, now);

        await LogAsync(SkillsEvidenceAuditAction.EntityTypeActorLink, externalActorId,
            SkillsEvidenceAuditAction.ActorLinkRevoked,
            JsonSerializer.Serialize(new { connectionKey, detached }), tenantId, actorMemberId);

        return CommandResult.Succeeded($"Mapping revoked. {detached} evidence rows are unattributed again.");
    }

    private Task LogAsync(string entityType, string entityId, string action, string? detailJson, Guid tenantId, int? actorMemberId) =>
        auditLog.LogAsync(entityType, entityId, action, actorMemberId, detailJson, timeProvider.GetUtcNow().UtcDateTime, tenantId);
}
