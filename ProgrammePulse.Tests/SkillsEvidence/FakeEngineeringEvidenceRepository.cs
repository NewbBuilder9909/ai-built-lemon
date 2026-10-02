using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// In-memory stand-in for EngineeringEvidenceRepository. A fake, not a
/// mock, per the convention in CLAUDE.md.
///
/// It reproduces the four composite unique constraints the real schema
/// enforces, because the tests above it are largely *about* those
/// constraints — most of all the evidence identity key, which is what
/// makes an idempotent replay idempotent. A service bug that duplicated
/// rows would otherwise pass here and fail only against SQL Server.
/// </summary>
public sealed class FakeEngineeringEvidenceRepository : IEngineeringEvidenceRepository
{
    public readonly List<EvidenceConnection> Connections = [];
    public readonly List<EngineeringEvidence> Evidence = [];
    public readonly List<EvidenceActorLink> ActorLinks = [];
    public readonly List<UnmappedEvidenceActor> Unmapped = [];
    public readonly List<EvidenceCoverage> Coverage = [];
    public readonly List<(string EntityType, string ExternalId, string Payload)> Raw = [];

    // ---- Connections ----

    public Task<IReadOnlyList<EvidenceConnection>> GetConnectionsAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<EvidenceConnection>>(Connections.Where(c => c.TenantId == tenantId).ToList());

    public Task<EvidenceConnection?> GetConnectionAsync(Guid connectionKey, Guid tenantId) =>
        Task.FromResult(Connections.FirstOrDefault(c => c.ConnectionKey == connectionKey && c.TenantId == tenantId));

    public Task<EvidenceConnection?> GetConnectionByAccountAsync(string provider, string sourceAccountId, Guid tenantId) =>
        Task.FromResult(Connections.FirstOrDefault(c =>
            c.TenantId == tenantId && c.Provider == provider
            && string.Equals(c.SourceAccountId, sourceAccountId, StringComparison.OrdinalIgnoreCase)));

    public Task<EvidenceConnection> UpsertConnectionAsync(EvidenceConnection connection)
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

    public Task<EvidenceConnection> SetConnectionStatusAsync(
        Guid connectionKey, EvidenceConnectionStatus status, Guid tenantId, DateTime nowUtc, bool clearCredential)
    {
        var existing = Connections.FirstOrDefault(c => c.ConnectionKey == connectionKey && c.TenantId == tenantId)
            ?? throw new CrossTenantReferenceException("EvidenceConnection", connectionKey);

        var updated = existing with
        {
            Status = status,
            UpdatedAtUtc = nowUtc,
            DisconnectedAtUtc = status == EvidenceConnectionStatus.Active ? null : nowUtc,
            ProtectedCredentialJson = clearCredential ? null : existing.ProtectedCredentialJson
        };

        Connections[Connections.IndexOf(existing)] = updated;
        return Task.FromResult(updated);
    }

    // ---- Evidence ----

    public Task<EngineeringEvidence> UpsertEvidenceAsync(EngineeringEvidence evidence)
    {
        var existing = Evidence.FirstOrDefault(e =>
            e.TenantId == evidence.TenantId
            && e.ConnectionKey == evidence.ConnectionKey
            && e.SourceType == evidence.SourceType
            && string.Equals(e.ExternalId, evidence.ExternalId, StringComparison.Ordinal)
            && e.Role == evidence.Role);

        if (existing is not null)
        {
            // Stands in for the unique index: a replay updates in place and
            // keeps the original first-seen stamp.
            var merged = evidence with
            {
                EvidenceKey = existing.EvidenceKey,
                FirstIngestedAtUtc = existing.FirstIngestedAtUtc
            };
            Evidence[Evidence.IndexOf(existing)] = merged;
            return Task.FromResult(merged);
        }

        Evidence.Add(evidence);
        return Task.FromResult(evidence);
    }

    public Task<IReadOnlyList<EngineeringEvidence>> GetEvidenceForStaffAsync(Guid staffKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<EngineeringEvidence>>(
            Evidence.Where(e => e.StaffKey == staffKey && e.TenantId == tenantId)
                    .OrderByDescending(e => e.OccurredAtUtc).ToList());

    public Task<IReadOnlyList<EngineeringEvidence>> GetEvidenceForConnectionAsync(Guid connectionKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<EngineeringEvidence>>(
            Evidence.Where(e => e.ConnectionKey == connectionKey && e.TenantId == tenantId).ToList());

    public Task<int> CountUnattributedAsync(Guid tenantId) =>
        Task.FromResult(Evidence.Count(e => e.TenantId == tenantId && e.StaffKey is null && !e.ActorIsBot));

    public Task<int> AttributeEvidenceToStaffAsync(Guid connectionKey, string externalActorId, Guid staffKey, Guid tenantId, DateTime nowUtc)
    {
        var matches = Evidence.Where(e =>
            e.TenantId == tenantId && e.ConnectionKey == connectionKey && !e.ActorIsBot
            && string.Equals(e.ActorExternalId, externalActorId, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var row in matches)
        {
            Evidence[Evidence.IndexOf(row)] = row with
            {
                StaffKey = staffKey,
                AttributionStatus = EvidenceAttributionStatus.Mapped,
                UpdatedAtUtc = nowUtc
            };
        }

        return Task.FromResult(matches.Count);
    }

    public Task<int> DetachEvidenceFromStaffAsync(Guid connectionKey, string externalActorId, Guid tenantId, DateTime nowUtc)
    {
        var matches = Evidence.Where(e =>
            e.TenantId == tenantId && e.ConnectionKey == connectionKey
            && string.Equals(e.ActorExternalId, externalActorId, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var row in matches)
        {
            Evidence[Evidence.IndexOf(row)] = row with
            {
                StaffKey = null,
                AttributionStatus = EvidenceAttributionStatus.Unmapped,
                UpdatedAtUtc = nowUtc
            };
        }

        return Task.FromResult(matches.Count);
    }

    // ---- Links and queue ----

    public Task<IReadOnlyList<EvidenceActorLink>> GetActorLinksAsync(Guid connectionKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<EvidenceActorLink>>(
            ActorLinks.Where(l => l.ConnectionKey == connectionKey && l.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<EvidenceActorLink>> GetActorLinksForStaffAsync(Guid staffKey) =>
        Task.FromResult<IReadOnlyList<EvidenceActorLink>>(ActorLinks.Where(l => l.StaffKey == staffKey).ToList());

    public Task<EvidenceActorLink> CreateActorLinkAsync(EvidenceActorLink link)
    {
        if (!Connections.Any(c => c.ConnectionKey == link.ConnectionKey && c.TenantId == link.TenantId))
        {
            throw new CrossTenantReferenceException("EvidenceConnection", link.ConnectionKey);
        }

        if (ActorLinks.Any(l =>
            l.TenantId == link.TenantId && l.ConnectionKey == link.ConnectionKey
            && string.Equals(l.ExternalActorId, link.ExternalActorId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SkillAssertionValidationException(
                $"'{link.ExternalLogin ?? link.ExternalActorId}' is already mapped on this connection. Remove that mapping first.");
        }

        ActorLinks.Add(link);
        return Task.FromResult(link);
    }

    public Task DeleteActorLinkAsync(Guid linkKey, Guid tenantId)
    {
        ActorLinks.RemoveAll(l => l.LinkKey == linkKey && l.TenantId == tenantId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UnmappedEvidenceActor>> GetUnmappedActorsAsync(Guid tenantId, bool openOnly) =>
        Task.FromResult<IReadOnlyList<UnmappedEvidenceActor>>(
            Unmapped.Where(a => a.TenantId == tenantId && (!openOnly || a.IsOpen))
                    .OrderByDescending(a => a.OccurrenceCount).ToList());

    public Task<UnmappedEvidenceActor?> GetUnmappedActorAsync(Guid unmappedActorKey, Guid tenantId) =>
        Task.FromResult(Unmapped.FirstOrDefault(a => a.UnmappedActorKey == unmappedActorKey && a.TenantId == tenantId));

    public Task<UnmappedEvidenceActor> RecordUnmappedSightingAsync(UnmappedEvidenceActor sighting)
    {
        var existing = Unmapped.FirstOrDefault(a =>
            a.TenantId == sighting.TenantId && a.ConnectionKey == sighting.ConnectionKey
            && string.Equals(a.ExternalActorId, sighting.ExternalActorId, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            Unmapped.Add(sighting);
            return Task.FromResult(sighting);
        }

        var updated = existing with
        {
            OccurrenceCount = existing.OccurrenceCount + 1,
            LastSeenUtc = sighting.LastSeenUtc,
            ExternalLogin = sighting.ExternalLogin ?? existing.ExternalLogin,
            DisplayName = sighting.DisplayName ?? existing.DisplayName,
            Email = sighting.Email ?? existing.Email,
            IsBot = sighting.IsBot,
            Reason = sighting.Reason,
            SuggestedStaffKey = sighting.SuggestedStaffKey,
            ResolvedAtUtc = null
        };

        Unmapped[Unmapped.IndexOf(existing)] = updated;
        return Task.FromResult(updated);
    }

    public Task MarkUnmappedResolvedAsync(Guid unmappedActorKey, Guid tenantId, DateTime nowUtc)
    {
        var existing = Unmapped.FirstOrDefault(a => a.UnmappedActorKey == unmappedActorKey && a.TenantId == tenantId);
        if (existing is not null)
        {
            Unmapped[Unmapped.IndexOf(existing)] = existing with { ResolvedAtUtc = nowUtc };
        }

        return Task.CompletedTask;
    }

    // ---- Coverage ----

    public Task<IReadOnlyList<EvidenceCoverage>> GetCoverageAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<EvidenceCoverage>>(Coverage.Where(c => c.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<EvidenceCoverage>> GetCoverageForConnectionAsync(Guid connectionKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<EvidenceCoverage>>(
            Coverage.Where(c => c.ConnectionKey == connectionKey && c.TenantId == tenantId).ToList());

    public Task<EvidenceCoverage> UpsertCoverageAsync(EvidenceCoverage coverage)
    {
        var existing = Coverage.FirstOrDefault(c =>
            c.TenantId == coverage.TenantId && c.ConnectionKey == coverage.ConnectionKey
            && string.Equals(c.RepositoryKey, coverage.RepositoryKey, StringComparison.OrdinalIgnoreCase)
            && c.Stream == coverage.Stream);

        if (existing is null)
        {
            Coverage.Add(coverage);
            return Task.FromResult(coverage);
        }

        var merged = coverage with
        {
            CoverageKey = existing.CoverageKey,
            // Never moves backwards, same as the real repository.
            ObservedFromUtc = existing.ObservedFromUtc is null || coverage.ObservedFromUtc < existing.ObservedFromUtc
                ? coverage.ObservedFromUtc ?? existing.ObservedFromUtc
                : existing.ObservedFromUtc,
            LastSucceededAtUtc = coverage.LastSucceededAtUtc ?? existing.LastSucceededAtUtc
        };

        Coverage[Coverage.IndexOf(existing)] = merged;
        return Task.FromResult(merged);
    }

    public Task<int> MarkCoverageOutOfScopeAsync(Guid connectionKey, IReadOnlyCollection<string> stillSelected, Guid tenantId, DateTime nowUtc)
    {
        var doomed = Coverage.Where(c =>
            c.TenantId == tenantId && c.ConnectionKey == connectionKey
            && c.Status != EvidenceCoverageStatus.OutOfScope
            && !stillSelected.Contains(c.RepositoryKey, StringComparer.OrdinalIgnoreCase)).ToList();

        foreach (var row in doomed)
        {
            Coverage[Coverage.IndexOf(row)] = row with { Status = EvidenceCoverageStatus.OutOfScope, UpdatedAtUtc = nowUtc };
        }

        return Task.FromResult(doomed.Count);
    }

    // ---- Bronze ----

    public Task SaveRawAsync(Guid tenantId, Guid connectionKey, string provider, string sourceAccountId, string entityType, string externalId, string payloadJson, DateTime fetchedAtUtc)
    {
        Raw.Add((entityType, externalId, payloadJson));
        return Task.CompletedTask;
    }

    public Task<int> PurgeRawBefore(DateTime cutoffUtc) => Task.FromResult(0);

    // ---- GDPR ----

    public Task<int> DeleteActorLinksForStaffAsync(Guid staffKey) =>
        Task.FromResult(ActorLinks.RemoveAll(l => l.StaffKey == staffKey));

    public Task<int> DeleteEvidenceForStaffAsync(Guid staffKey) =>
        Task.FromResult(Evidence.RemoveAll(e => e.StaffKey == staffKey));

    public Task<EvidenceErasureCounts> EraseSubjectTracesAsync(Guid staffKey, IReadOnlyList<(Guid TenantId, Guid ConnectionKey, string ExternalActorId)> identities)
    {
        bool Traced(EngineeringEvidence e) =>
            e.StaffKey == staffKey
            || identities.Any(i => i.TenantId == e.TenantId && i.ConnectionKey == e.ConnectionKey && i.ExternalActorId == e.ActorExternalId);

        var artefacts = Evidence.Where(Traced).Select(e => e.ExternalId).ToHashSet(StringComparer.Ordinal);
        var evidence = Evidence.RemoveAll(Traced);
        var unmapped = Unmapped.RemoveAll(u => identities.Any(i => i.TenantId == u.TenantId && i.ConnectionKey == u.ConnectionKey && i.ExternalActorId == u.ExternalActorId));
        var raw = Raw.RemoveAll(r => artefacts.Contains(r.ExternalId));
        return Task.FromResult(new EvidenceErasureCounts(evidence, unmapped, raw));
    }
}
