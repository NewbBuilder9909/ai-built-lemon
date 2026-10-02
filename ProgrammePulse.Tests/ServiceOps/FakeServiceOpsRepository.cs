using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Services.ServiceOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.ServiceOps;

/// <summary>
/// In-memory stand-in for ServiceOpsRepository. A fake, not a mock, per
/// the convention in CLAUDE.md.
///
/// It reproduces the composite unique constraints the real schema
/// enforces — most importantly the case identity key, which is what
/// makes the deliberate <c>updated_since</c> overlap safe. A service bug
/// that duplicated cases on every run would otherwise pass here and
/// inflate demand only against SQL Server.
/// </summary>
public sealed class FakeServiceOpsRepository : IServiceOpsRepository
{
    public readonly List<DeskConnection> Connections = [];
    public readonly List<SupportCaseFact> Cases = [];
    public readonly List<SupportCodeLink> Links = [];
    public readonly List<SupportCaseParticipant> Participants = [];
    public readonly List<DeskAgentLink> AgentLinks = [];
    public readonly List<DeskCoverage> Coverage = [];
    public readonly List<ServiceOpsAuditLog> Audit = [];
    public readonly List<(string EntityType, string ExternalId)> Raw = [];

    // ---- Connections ----

    public Task<IReadOnlyList<DeskConnection>> GetConnectionsAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<DeskConnection>>(Connections.Where(c => c.TenantId == tenantId).ToList());

    public Task<DeskConnection?> GetConnectionAsync(Guid connectionKey, Guid tenantId) =>
        Task.FromResult(Connections.FirstOrDefault(c => c.ConnectionKey == connectionKey && c.TenantId == tenantId));

    public Task<DeskConnection?> GetConnectionByAccountAsync(string provider, string sourceAccountId, Guid tenantId) =>
        Task.FromResult(Connections.FirstOrDefault(c =>
            c.TenantId == tenantId && c.Provider == provider
            && string.Equals(c.SourceAccountId, sourceAccountId, StringComparison.OrdinalIgnoreCase)));

    public Task<DeskConnection> UpsertConnectionAsync(DeskConnection connection)
    {
        var existing = Connections.FirstOrDefault(c =>
            c.TenantId == connection.TenantId && c.Provider == connection.Provider
            && string.Equals(c.SourceAccountId, connection.SourceAccountId, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            var merged = connection with
            {
                ConnectionKey = existing.ConnectionKey,
                ProtectedCredentialJson = connection.ProtectedCredentialJson ?? existing.ProtectedCredentialJson
            };
            Connections[Connections.IndexOf(existing)] = merged;
            return Task.FromResult(merged);
        }

        Connections.Add(connection);
        return Task.FromResult(connection);
    }

    public Task<DeskConnection> SetConnectionStatusAsync(
        Guid connectionKey, DeskConnectionStatus status, Guid tenantId, DateTime nowUtc, bool clearCredential)
    {
        var existing = Connections.FirstOrDefault(c => c.ConnectionKey == connectionKey && c.TenantId == tenantId)
            ?? throw new CrossTenantReferenceException("DeskConnection", connectionKey);

        var updated = existing with
        {
            Status = status,
            UpdatedAtUtc = nowUtc,
            DisconnectedAtUtc = status == DeskConnectionStatus.Active ? null : nowUtc,
            ProtectedCredentialJson = clearCredential ? null : existing.ProtectedCredentialJson
        };

        Connections[Connections.IndexOf(existing)] = updated;
        return Task.FromResult(updated);
    }

    // ---- Cases ----

    public Task<SupportCaseFact> UpsertCaseAsync(SupportCaseFact fact)
    {
        var existing = Cases.FirstOrDefault(c =>
            c.TenantId == fact.TenantId && c.ConnectionKey == fact.ConnectionKey
            && string.Equals(c.ExternalTicketId, fact.ExternalTicketId, StringComparison.Ordinal));

        if (existing is not null)
        {
            // Stands in for UX_ServiceOps_SupportCaseFact_identity.
            var merged = fact with { CaseKey = existing.CaseKey, FirstIngestedAtUtc = existing.FirstIngestedAtUtc };
            Cases[Cases.IndexOf(existing)] = merged;
            return Task.FromResult(merged);
        }

        Cases.Add(fact);
        return Task.FromResult(fact);
    }

    public Task<SupportCaseFact?> GetCaseAsync(Guid connectionKey, string externalTicketId, Guid tenantId) =>
        Task.FromResult(Cases.FirstOrDefault(c =>
            c.ConnectionKey == connectionKey && c.TenantId == tenantId
            && string.Equals(c.ExternalTicketId, externalTicketId, StringComparison.Ordinal)));

    public Task<IReadOnlyList<SupportCaseFact>> GetCasesCreatedBetweenAsync(Guid tenantId, DateTime fromUtc, DateTime toUtc) =>
        Task.FromResult<IReadOnlyList<SupportCaseFact>>(
            Cases.Where(c => c.TenantId == tenantId && c.CreatedAtUtc >= fromUtc && c.CreatedAtUtc < toUtc).ToList());

    public Task<int> MarkCaseWithdrawnAsync(Guid connectionKey, string externalTicketId, Guid tenantId, DateTime nowUtc)
    {
        var existing = Cases.FirstOrDefault(c =>
            c.ConnectionKey == connectionKey && c.TenantId == tenantId
            && string.Equals(c.ExternalTicketId, externalTicketId, StringComparison.Ordinal));

        if (existing is null)
        {
            return Task.FromResult(0);
        }

        Cases[Cases.IndexOf(existing)] = existing with
        {
            IsWithdrawn = true,
            State = SupportCaseState.Withdrawn,
            IngestedAtUtc = nowUtc
        };
        return Task.FromResult(1);
    }

    // ---- Links ----

    public Task<IReadOnlyList<SupportCodeLink>> GetLinksForCaseAsync(Guid connectionKey, string externalTicketId, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<SupportCodeLink>>(
            Links.Where(l => l.ConnectionKey == connectionKey && l.TenantId == tenantId
                && string.Equals(l.ExternalTicketId, externalTicketId, StringComparison.Ordinal)).ToList());

    public Task<IReadOnlyList<SupportCodeLink>> GetLinksAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<SupportCodeLink>>(Links.Where(l => l.TenantId == tenantId).ToList());

    public Task<SupportCodeLink?> GetLinkAsync(Guid linkKey, Guid tenantId) =>
        Task.FromResult(Links.FirstOrDefault(l => l.LinkKey == linkKey && l.TenantId == tenantId));

    public Task<SupportCodeLink> UpsertLinkAsync(SupportCodeLink link)
    {
        if (!Cases.Any(c => c.TenantId == link.TenantId && c.ConnectionKey == link.ConnectionKey
            && string.Equals(c.ExternalTicketId, link.ExternalTicketId, StringComparison.Ordinal)))
        {
            throw new CrossTenantReferenceException("SupportCase", link.LinkKey);
        }

        var existing = Links.FirstOrDefault(l =>
            l.TenantId == link.TenantId && l.ConnectionKey == link.ConnectionKey
            && string.Equals(l.ExternalTicketId, link.ExternalTicketId, StringComparison.Ordinal)
            && l.ArtifactType == link.ArtifactType
            && string.Equals(l.ArtifactExternalId, link.ArtifactExternalId, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            var merged = link with { LinkKey = existing.LinkKey, CreatedAtUtc = existing.CreatedAtUtc };
            Links[Links.IndexOf(existing)] = merged;
            return Task.FromResult(merged);
        }

        Links.Add(link);
        return Task.FromResult(link);
    }

    public Task DeleteLinkAsync(Guid linkKey, Guid tenantId)
    {
        Links.RemoveAll(l => l.LinkKey == linkKey && l.TenantId == tenantId);
        return Task.CompletedTask;
    }

    // ---- Participants and agents ----

    public Task<IReadOnlyList<SupportCaseParticipant>> GetParticipantsForCaseAsync(Guid connectionKey, string externalTicketId, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<SupportCaseParticipant>>(
            Participants.Where(p => p.ConnectionKey == connectionKey && p.TenantId == tenantId
                && string.Equals(p.ExternalTicketId, externalTicketId, StringComparison.Ordinal)).ToList());

    public Task<IReadOnlyList<SupportCaseParticipant>> GetParticipantsForStaffAsync(Guid staffKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<SupportCaseParticipant>>(
            Participants.Where(p => p.StaffKey == staffKey && p.TenantId == tenantId).ToList());

    public Task<SupportCaseParticipant> UpsertParticipantAsync(SupportCaseParticipant participant)
    {
        var existing = Participants.FirstOrDefault(p =>
            p.TenantId == participant.TenantId && p.ConnectionKey == participant.ConnectionKey
            && string.Equals(p.ExternalTicketId, participant.ExternalTicketId, StringComparison.Ordinal)
            && string.Equals(p.ExternalAgentId, participant.ExternalAgentId, StringComparison.OrdinalIgnoreCase)
            && p.Role == participant.Role);

        if (existing is not null)
        {
            // A replay must not clear an attribution an admin approved
            // between runs.
            var merged = participant with
            {
                ParticipantKey = existing.ParticipantKey,
                StaffKey = participant.StaffKey ?? existing.StaffKey
            };
            Participants[Participants.IndexOf(existing)] = merged;
            return Task.FromResult(merged);
        }

        Participants.Add(participant);
        return Task.FromResult(participant);
    }

    public Task<IReadOnlyList<DeskAgentLink>> GetAgentLinksAsync(Guid connectionKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<DeskAgentLink>>(
            AgentLinks.Where(l => l.ConnectionKey == connectionKey && l.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<DeskAgentLink>> GetAgentLinksForStaffAsync(Guid staffKey) =>
        Task.FromResult<IReadOnlyList<DeskAgentLink>>(AgentLinks.Where(l => l.StaffKey == staffKey).ToList());

    public Task<DeskAgentLink> CreateAgentLinkAsync(DeskAgentLink link)
    {
        if (!Connections.Any(c => c.ConnectionKey == link.ConnectionKey && c.TenantId == link.TenantId))
        {
            throw new CrossTenantReferenceException("DeskConnection", link.ConnectionKey);
        }

        if (AgentLinks.Any(l => l.TenantId == link.TenantId && l.ConnectionKey == link.ConnectionKey
            && string.Equals(l.ExternalAgentId, link.ExternalAgentId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ServiceOpsValidationException(
                $"'{link.ExternalAgentName ?? link.ExternalAgentId}' is already mapped on this desk. Remove that mapping first.");
        }

        AgentLinks.Add(link);
        return Task.FromResult(link);
    }

    public Task DeleteAgentLinkAsync(Guid linkKey, Guid tenantId)
    {
        AgentLinks.RemoveAll(l => l.LinkKey == linkKey && l.TenantId == tenantId);
        return Task.CompletedTask;
    }

    public Task<int> AttributeParticipationAsync(Guid connectionKey, string externalAgentId, Guid staffKey, Guid tenantId, DateTime nowUtc)
    {
        var matches = Participants.Where(p =>
            p.TenantId == tenantId && p.ConnectionKey == connectionKey
            && string.Equals(p.ExternalAgentId, externalAgentId, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var row in matches)
        {
            Participants[Participants.IndexOf(row)] = row with { StaffKey = staffKey, IngestedAtUtc = nowUtc };
        }

        return Task.FromResult(matches.Count);
    }

    public Task<int> DetachParticipationAsync(Guid connectionKey, string externalAgentId, Guid tenantId, DateTime nowUtc)
    {
        var matches = Participants.Where(p =>
            p.TenantId == tenantId && p.ConnectionKey == connectionKey
            && string.Equals(p.ExternalAgentId, externalAgentId, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var row in matches)
        {
            Participants[Participants.IndexOf(row)] = row with { StaffKey = null, IngestedAtUtc = nowUtc };
        }

        return Task.FromResult(matches.Count);
    }

    public Task<IReadOnlyList<(string ExternalAgentId, string? DisplayName, int Cases)>> GetUnmappedAgentsAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<(string, string?, int)>>(
            Participants
                .Where(p => p.TenantId == tenantId && p.StaffKey is null)
                .GroupBy(p => p.ExternalAgentId, StringComparer.OrdinalIgnoreCase)
                .Select(g => (g.Key, g.Select(p => p.AgentDisplayName).FirstOrDefault(n => n is not null),
                    g.Select(p => p.ExternalTicketId).Distinct(StringComparer.Ordinal).Count()))
                .ToList());

    // ---- Coverage ----

    public Task<IReadOnlyList<DeskCoverage>> GetCoverageAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<DeskCoverage>>(Coverage.Where(c => c.TenantId == tenantId).ToList());

    public Task<DeskCoverage?> GetCoverageAsync(Guid connectionKey, DeskStream stream, Guid tenantId) =>
        Task.FromResult(Coverage.FirstOrDefault(c =>
            c.ConnectionKey == connectionKey && c.Stream == stream && c.TenantId == tenantId));

    public Task<DeskCoverage> UpsertCoverageAsync(DeskCoverage coverage)
    {
        var existing = Coverage.FirstOrDefault(c =>
            c.TenantId == coverage.TenantId && c.ConnectionKey == coverage.ConnectionKey && c.Stream == coverage.Stream);

        if (existing is null)
        {
            Coverage.Add(coverage);
            return Task.FromResult(coverage);
        }

        var merged = coverage with
        {
            CoverageKey = existing.CoverageKey,
            ObservedFromUtc = existing.ObservedFromUtc is null || coverage.ObservedFromUtc < existing.ObservedFromUtc
                ? coverage.ObservedFromUtc ?? existing.ObservedFromUtc
                : existing.ObservedFromUtc,
            LastSucceededAtUtc = coverage.LastSucceededAtUtc ?? existing.LastSucceededAtUtc
        };

        Coverage[Coverage.IndexOf(existing)] = merged;
        return Task.FromResult(merged);
    }

    // ---- Bronze and audit ----

    public Task SaveRawAsync(Guid tenantId, Guid connectionKey, string provider, string sourceAccountId, string entityType, string externalId, string payloadJson, DateTime fetchedAtUtc)
    {
        Raw.Add((entityType, externalId));
        return Task.CompletedTask;
    }

    public Task<int> PurgeRawBefore(DateTime cutoffUtc) => Task.FromResult(0);

    public Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
    {
        Audit.Add(new ServiceOpsAuditLog
        {
            LogKey = Guid.NewGuid(),
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ActorMemberId = actorMemberId,
            DetailJson = detailJson,
            TimestampUtc = timestampUtc
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ServiceOpsAuditLog>> GetRecentAuditAsync(int take, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<ServiceOpsAuditLog>>(
            Audit.Where(a => a.TenantId == tenantId).OrderByDescending(a => a.TimestampUtc).Take(take).ToList());

    // ---- GDPR ----

    public Task<int> DeleteAgentLinksForStaffAsync(Guid staffKey) =>
        Task.FromResult(AgentLinks.RemoveAll(l => l.StaffKey == staffKey));

    public Task<int> DetachParticipationForStaffAsync(Guid staffKey, DateTime nowUtc)
    {
        var matches = Participants.Where(p => p.StaffKey == staffKey).ToList();

        foreach (var row in matches)
        {
            Participants[Participants.IndexOf(row)] = row with
            {
                StaffKey = null,
                AgentDisplayName = null,
                IngestedAtUtc = nowUtc
            };
        }

        return Task.FromResult(matches.Count);
    }

    public Task<int> ScrubAgentNamesAsync(IReadOnlyList<(Guid TenantId, Guid ConnectionKey, string ExternalAgentId)> agents, DateTime nowUtc)
    {
        var updated = 0;
        for (var i = 0; i < Participants.Count; i++)
        {
            var p = Participants[i];
            if (p.AgentDisplayName is not null
                && agents.Any(a => a.TenantId == p.TenantId && a.ConnectionKey == p.ConnectionKey && a.ExternalAgentId == p.ExternalAgentId))
            {
                Participants[i] = p with { AgentDisplayName = null, IngestedAtUtc = nowUtc };
                updated++;
            }
        }

        return Task.FromResult(updated);
    }
}
