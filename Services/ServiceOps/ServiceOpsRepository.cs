using System.Text.Json;
using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.ServiceOps;
using Umbraco.Cms.Infrastructure.Scoping;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ServiceOps;

public sealed class ServiceOpsRepository(IScopeProvider scopeProvider) : IServiceOpsRepository
{
    // ---- Connections ----

    public async Task<IReadOnlyList<DeskConnection>> GetConnectionsAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<DeskConnectionDto>(
            Sql.Builder.Where("tenantId = @0", tenantId).OrderBy("displayName"));
        return dtos.Select(Map).ToList();
    }

    public async Task<DeskConnection?> GetConnectionAsync(Guid connectionKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<DeskConnectionDto>(
            Sql.Builder.Where("connectionKey = @0 AND tenantId = @1", connectionKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<DeskConnection?> GetConnectionByAccountAsync(string provider, string sourceAccountId, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<DeskConnectionDto>(
            Sql.Builder.Where("provider = @0 AND sourceAccountId = @1 AND tenantId = @2", provider, sourceAccountId, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<DeskConnection> UpsertConnectionAsync(DeskConnection connection)
    {
        using var scope = scopeProvider.CreateScope();
        var existing = await scope.Database.FirstOrDefaultAsync<DeskConnectionDto>(
            Sql.Builder.Where("tenantId = @0 AND provider = @1 AND sourceAccountId = @2",
                connection.TenantId, connection.Provider, connection.SourceAccountId));

        var dto = existing ?? new DeskConnectionDto
        {
            ConnectionKey = connection.ConnectionKey,
            TenantId = connection.TenantId,
            Provider = connection.Provider,
            SourceAccountId = connection.SourceAccountId,
            CreatedAtUtc = connection.CreatedAtUtc
        };

        dto.DisplayName = connection.DisplayName;
        dto.ApiBaseUrl = connection.ApiBaseUrl;
        dto.ApprovedComponentsJson = JsonSerializer.Serialize(connection.ApprovedComponents);
        dto.Status = connection.Status.ToString();
        dto.ConnectedByStaffKey = connection.ConnectedByStaffKey;
        dto.UpdatedAtUtc = connection.UpdatedAtUtc;
        dto.DisconnectedAtUtc = connection.DisconnectedAtUtc;

        // A reconnect that supplies no new credential keeps the stored one.
        if (connection.ProtectedCredentialJson is not null)
        {
            dto.ProtectedCredentialJson = connection.ProtectedCredentialJson;
        }

        if (existing is null)
        {
            await scope.Database.InsertAsync(dto);
        }
        else
        {
            await scope.Database.UpdateAsync(dto);
        }

        scope.Complete();
        return Map(dto);
    }

    public async Task<DeskConnection> SetConnectionStatusAsync(
        Guid connectionKey, DeskConnectionStatus status, Guid tenantId, DateTime nowUtc, bool clearCredential)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<DeskConnectionDto>(
            Sql.Builder.Where("connectionKey = @0 AND tenantId = @1", connectionKey, tenantId))
            ?? throw new CrossTenantReferenceException("DeskConnection", connectionKey);

        dto.Status = status.ToString();
        dto.UpdatedAtUtc = nowUtc;
        dto.DisconnectedAtUtc = status == DeskConnectionStatus.Active ? null : nowUtc;

        if (clearCredential)
        {
            dto.ProtectedCredentialJson = null;
        }

        await scope.Database.UpdateAsync(dto);
        scope.Complete();
        return Map(dto);
    }

    // ---- Cases ----

    public async Task<SupportCaseFact> UpsertCaseAsync(SupportCaseFact fact)
    {
        using var scope = scopeProvider.CreateScope();
        var existing = await scope.Database.FirstOrDefaultAsync<SupportCaseFactDto>(
            Sql.Builder.Where("tenantId = @0 AND connectionKey = @1 AND externalTicketId = @2",
                fact.TenantId, fact.ConnectionKey, fact.ExternalTicketId));

        var dto = existing ?? new SupportCaseFactDto
        {
            CaseKey = fact.CaseKey,
            TenantId = fact.TenantId,
            ConnectionKey = fact.ConnectionKey,
            ExternalTicketId = fact.ExternalTicketId,
            FirstIngestedAtUtc = fact.FirstIngestedAtUtc
        };

        dto.Provider = fact.Provider;
        dto.SourceAccountId = fact.SourceAccountId;
        dto.CreatedAtUtc = fact.CreatedAtUtc;
        dto.UpdatedAtUtc = fact.UpdatedAtUtc;
        dto.ResolvedAtUtc = fact.ResolvedAtUtc;
        dto.ClosedAtUtc = fact.ClosedAtUtc;
        dto.ReopenedAtUtc = fact.ReopenedAtUtc;
        dto.State = fact.State.ToString();
        dto.ProviderStatus = Truncate(fact.ProviderStatus, 64);
        dto.Priority = fact.Priority.ToString();
        dto.ComponentKey = Truncate(fact.ComponentKey, 64);
        dto.RawComponentTag = Truncate(fact.RawComponentTag, 128);
        dto.CaseType = Truncate(fact.CaseType, 64);
        dto.SourceUrl = Truncate(fact.SourceUrl, 512);
        dto.IsReopened = fact.IsReopened;
        dto.IsWithdrawn = fact.IsWithdrawn;
        dto.ObservedInRunKey = fact.ObservedInRunKey;
        dto.SchemaVersion = fact.SchemaVersion;
        dto.IngestedAtUtc = fact.IngestedAtUtc;

        if (existing is null)
        {
            await scope.Database.InsertAsync(dto);
        }
        else
        {
            await scope.Database.UpdateAsync(dto);
        }

        scope.Complete();
        return Map(dto);
    }

    public async Task<SupportCaseFact?> GetCaseAsync(Guid connectionKey, string externalTicketId, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<SupportCaseFactDto>(
            Sql.Builder.Where("connectionKey = @0 AND externalTicketId = @1 AND tenantId = @2",
                connectionKey, externalTicketId, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyList<SupportCaseFact>> GetCasesCreatedBetweenAsync(Guid tenantId, DateTime fromUtc, DateTime toUtc)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<SupportCaseFactDto>(
            Sql.Builder
                .Where("tenantId = @0 AND createdAtUtc >= @1 AND createdAtUtc < @2", tenantId, fromUtc, toUtc)
                .OrderBy("createdAtUtc"));
        return dtos.Select(Map).ToList();
    }

    public async Task<int> MarkCaseWithdrawnAsync(Guid connectionKey, string externalTicketId, Guid tenantId, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var updated = await scope.Database.ExecuteAsync(
            $"UPDATE [{SupportCaseFactDto.TableName}] SET [isWithdrawn] = 1, [state] = @0, [ingestedAtUtc] = @1 " +
            "WHERE [tenantId] = @2 AND [connectionKey] = @3 AND [externalTicketId] = @4",
            nameof(SupportCaseState.Withdrawn), nowUtc, tenantId, connectionKey, externalTicketId);
        scope.Complete();
        return updated;
    }

    // ---- Links ----

    public async Task<IReadOnlyList<SupportCodeLink>> GetLinksForCaseAsync(Guid connectionKey, string externalTicketId, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<SupportCodeLinkDto>(
            Sql.Builder.Where("connectionKey = @0 AND externalTicketId = @1 AND tenantId = @2",
                connectionKey, externalTicketId, tenantId));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<SupportCodeLink>> GetLinksAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<SupportCodeLinkDto>(Sql.Builder.Where("tenantId = @0", tenantId));
        return dtos.Select(Map).ToList();
    }

    public async Task<SupportCodeLink?> GetLinkAsync(Guid linkKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<SupportCodeLinkDto>(
            Sql.Builder.Where("linkKey = @0 AND tenantId = @1", linkKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<SupportCodeLink> UpsertLinkAsync(SupportCodeLink link)
    {
        using var scope = scopeProvider.CreateScope();

        // The case has to be one of this tenant's, on this connection.
        var caseRow = await scope.Database.FirstOrDefaultAsync<SupportCaseFactDto>(
            Sql.Builder.Where("tenantId = @0 AND connectionKey = @1 AND externalTicketId = @2",
                link.TenantId, link.ConnectionKey, link.ExternalTicketId));
        if (caseRow is null)
        {
            throw new CrossTenantReferenceException("SupportCase", link.LinkKey);
        }

        var existing = await scope.Database.FirstOrDefaultAsync<SupportCodeLinkDto>(
            Sql.Builder.Where("tenantId = @0 AND connectionKey = @1 AND externalTicketId = @2 AND artifactType = @3 AND artifactExternalId = @4",
                link.TenantId, link.ConnectionKey, link.ExternalTicketId,
                link.ArtifactType.ToString(), link.ArtifactExternalId));

        var dto = existing ?? new SupportCodeLinkDto
        {
            LinkKey = link.LinkKey,
            TenantId = link.TenantId,
            ConnectionKey = link.ConnectionKey,
            ExternalTicketId = link.ExternalTicketId,
            ArtifactType = link.ArtifactType.ToString(),
            ArtifactExternalId = link.ArtifactExternalId,
            CreatedAtUtc = link.CreatedAtUtc
        };

        dto.ArtifactSource = Truncate(link.ArtifactSource, 256);
        dto.ArtifactUrl = Truncate(link.ArtifactUrl, 512);
        dto.Method = link.Method.ToString();
        dto.ReviewedByStaffKey = link.ReviewedByStaffKey;
        dto.ReviewedAtUtc = link.ReviewedAtUtc;
        dto.ReviewNote = Truncate(link.ReviewNote, 1000);
        dto.UpdatedAtUtc = link.UpdatedAtUtc;

        if (existing is null)
        {
            await scope.Database.InsertAsync(dto);
        }
        else
        {
            await scope.Database.UpdateAsync(dto);
        }

        scope.Complete();
        return Map(dto);
    }

    public async Task DeleteLinkAsync(Guid linkKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"DELETE FROM [{SupportCodeLinkDto.TableName}] WHERE [linkKey] = @0 AND [tenantId] = @1", linkKey, tenantId);
        scope.Complete();
    }

    // ---- Participants and agents ----

    public async Task<IReadOnlyList<SupportCaseParticipant>> GetParticipantsForCaseAsync(Guid connectionKey, string externalTicketId, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<SupportCaseParticipantDto>(
            Sql.Builder.Where("connectionKey = @0 AND externalTicketId = @1 AND tenantId = @2",
                connectionKey, externalTicketId, tenantId));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<SupportCaseParticipant>> GetParticipantsForStaffAsync(Guid staffKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<SupportCaseParticipantDto>(
            Sql.Builder.Where("staffKey = @0 AND tenantId = @1", staffKey, tenantId).OrderBy("occurredAtUtc DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<SupportCaseParticipant> UpsertParticipantAsync(SupportCaseParticipant participant)
    {
        using var scope = scopeProvider.CreateScope();
        var existing = await scope.Database.FirstOrDefaultAsync<SupportCaseParticipantDto>(
            Sql.Builder.Where("tenantId = @0 AND connectionKey = @1 AND externalTicketId = @2 AND externalAgentId = @3 AND [role] = @4",
                participant.TenantId, participant.ConnectionKey, participant.ExternalTicketId,
                participant.ExternalAgentId, participant.Role.ToString()));

        var dto = existing ?? new SupportCaseParticipantDto
        {
            ParticipantKey = participant.ParticipantKey,
            TenantId = participant.TenantId,
            ConnectionKey = participant.ConnectionKey,
            ExternalTicketId = participant.ExternalTicketId,
            ExternalAgentId = participant.ExternalAgentId,
            Role = participant.Role.ToString()
        };

        dto.AgentDisplayName = Truncate(participant.AgentDisplayName, 256);
        dto.StaffKey = participant.StaffKey;
        dto.OccurredAtUtc = participant.OccurredAtUtc;
        dto.IngestedAtUtc = participant.IngestedAtUtc;

        if (existing is null)
        {
            await scope.Database.InsertAsync(dto);
        }
        else
        {
            await scope.Database.UpdateAsync(dto);
        }

        scope.Complete();
        return Map(dto);
    }

    public async Task<IReadOnlyList<DeskAgentLink>> GetAgentLinksAsync(Guid connectionKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<DeskAgentLinkDto>(
            Sql.Builder.Where("connectionKey = @0 AND tenantId = @1", connectionKey, tenantId));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<DeskAgentLink>> GetAgentLinksForStaffAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<DeskAgentLinkDto>(Sql.Builder.Where("staffKey = @0", staffKey));
        return dtos.Select(Map).ToList();
    }

    public async Task<DeskAgentLink> CreateAgentLinkAsync(DeskAgentLink link)
    {
        using var scope = scopeProvider.CreateScope();

        var connection = await scope.Database.FirstOrDefaultAsync<DeskConnectionDto>(
            Sql.Builder.Where("connectionKey = @0 AND tenantId = @1", link.ConnectionKey, link.TenantId));
        if (connection is null)
        {
            throw new CrossTenantReferenceException("DeskConnection", link.ConnectionKey);
        }

        var clash = await scope.Database.FirstOrDefaultAsync<DeskAgentLinkDto>(
            Sql.Builder.Where("tenantId = @0 AND connectionKey = @1 AND externalAgentId = @2",
                link.TenantId, link.ConnectionKey, link.ExternalAgentId));
        if (clash is not null)
        {
            throw new ServiceOpsValidationException(
                $"'{link.ExternalAgentName ?? link.ExternalAgentId}' is already mapped on this desk. Remove that mapping first.");
        }

        var dto = new DeskAgentLinkDto
        {
            LinkKey = link.LinkKey,
            TenantId = link.TenantId,
            ConnectionKey = link.ConnectionKey,
            Provider = link.Provider,
            ExternalAgentId = link.ExternalAgentId,
            ExternalAgentName = Truncate(link.ExternalAgentName, 256),
            StaffKey = link.StaffKey,
            ApprovedByStaffKey = link.ApprovedByStaffKey,
            ApprovedAtUtc = link.ApprovedAtUtc
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();
        return Map(dto);
    }

    public async Task DeleteAgentLinkAsync(Guid linkKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"DELETE FROM [{DeskAgentLinkDto.TableName}] WHERE [linkKey] = @0 AND [tenantId] = @1", linkKey, tenantId);
        scope.Complete();
    }

    public async Task<int> AttributeParticipationAsync(Guid connectionKey, string externalAgentId, Guid staffKey, Guid tenantId, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var updated = await scope.Database.ExecuteAsync(
            $"UPDATE [{SupportCaseParticipantDto.TableName}] SET [staffKey] = @0, [ingestedAtUtc] = @1 " +
            "WHERE [tenantId] = @2 AND [connectionKey] = @3 AND [externalAgentId] = @4",
            staffKey, nowUtc, tenantId, connectionKey, externalAgentId);
        scope.Complete();
        return updated;
    }

    public async Task<int> DetachParticipationAsync(Guid connectionKey, string externalAgentId, Guid tenantId, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var updated = await scope.Database.ExecuteAsync(
            $"UPDATE [{SupportCaseParticipantDto.TableName}] SET [staffKey] = NULL, [ingestedAtUtc] = @0 " +
            "WHERE [tenantId] = @1 AND [connectionKey] = @2 AND [externalAgentId] = @3",
            nowUtc, tenantId, connectionKey, externalAgentId);
        scope.Complete();
        return updated;
    }

    public async Task<IReadOnlyList<(string ExternalAgentId, string? DisplayName, int Cases)>> GetUnmappedAgentsAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var rows = await scope.Database.FetchAsync<UnmappedAgentRow>(
            $"SELECT [externalAgentId] AS ExternalAgentId, MAX([agentDisplayName]) AS DisplayName, COUNT(DISTINCT [externalTicketId]) AS Cases " +
            $"FROM [{SupportCaseParticipantDto.TableName}] WHERE [tenantId] = @0 AND [staffKey] IS NULL " +
            "GROUP BY [externalAgentId] ORDER BY COUNT(DISTINCT [externalTicketId]) DESC",
            tenantId);

        return rows.Select(r => (r.ExternalAgentId, r.DisplayName, r.Cases)).ToList();
    }

    private sealed class UnmappedAgentRow
    {
        public string ExternalAgentId { get; set; } = null!;
        public string? DisplayName { get; set; }
        public int Cases { get; set; }
    }

    // ---- Coverage ----

    public async Task<IReadOnlyList<DeskCoverage>> GetCoverageAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<DeskCoverageDto>(Sql.Builder.Where("tenantId = @0", tenantId));
        return dtos.Select(Map).ToList();
    }

    public async Task<DeskCoverage?> GetCoverageAsync(Guid connectionKey, DeskStream stream, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<DeskCoverageDto>(
            Sql.Builder.Where("connectionKey = @0 AND stream = @1 AND tenantId = @2", connectionKey, stream.ToString(), tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<DeskCoverage> UpsertCoverageAsync(DeskCoverage coverage)
    {
        using var scope = scopeProvider.CreateScope();
        var existing = await scope.Database.FirstOrDefaultAsync<DeskCoverageDto>(
            Sql.Builder.Where("tenantId = @0 AND connectionKey = @1 AND stream = @2",
                coverage.TenantId, coverage.ConnectionKey, coverage.Stream.ToString()));

        var dto = existing ?? new DeskCoverageDto
        {
            CoverageKey = coverage.CoverageKey,
            TenantId = coverage.TenantId,
            ConnectionKey = coverage.ConnectionKey,
            Stream = coverage.Stream.ToString()
        };

        dto.Cursor = coverage.Cursor;
        dto.ObservedFromUtc = Earliest(dto.ObservedFromUtc, coverage.ObservedFromUtc);
        dto.CompleteThroughUtc = coverage.CompleteThroughUtc;
        dto.Status = coverage.Status.ToString();
        dto.StatusDetail = Truncate(coverage.StatusDetail, 512);
        dto.LastRunKey = coverage.LastRunKey;
        dto.LastAttemptedAtUtc = coverage.LastAttemptedAtUtc;
        dto.LastSucceededAtUtc = coverage.LastSucceededAtUtc ?? dto.LastSucceededAtUtc;
        dto.UpdatedAtUtc = coverage.UpdatedAtUtc;

        if (existing is null)
        {
            await scope.Database.InsertAsync(dto);
        }
        else
        {
            await scope.Database.UpdateAsync(dto);
        }

        scope.Complete();
        return Map(dto);
    }

    // ---- Bronze and audit ----

    public async Task SaveRawAsync(Guid tenantId, Guid connectionKey, string provider, string sourceAccountId, string entityType, string externalId, string payloadJson, DateTime fetchedAtUtc)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.InsertAsync(new RawDeskPayloadDto
        {
            TenantId = tenantId,
            ConnectionKey = connectionKey,
            Provider = provider,
            SourceAccountId = sourceAccountId,
            EntityType = entityType,
            ExternalId = externalId,
            PayloadJson = payloadJson,
            FetchedAtUtc = fetchedAtUtc
        });
        scope.Complete();
    }

    public async Task<int> PurgeRawBefore(DateTime cutoffUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var deleted = await scope.Database.ExecuteAsync(
            $"DELETE FROM [{RawDeskPayloadDto.TableName}] WHERE [fetchedAtUtc] < @0", cutoffUtc);
        scope.Complete();
        return deleted;
    }

    public async Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.InsertAsync(new ServiceOpsAuditLogDto
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
        scope.Complete();
    }

    public async Task<IReadOnlyList<ServiceOpsAuditLog>> GetRecentAuditAsync(int take, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var page = await scope.Database.PageAsync<ServiceOpsAuditLogDto>(1, take,
            Sql.Builder.Select("*").From(ServiceOpsAuditLogDto.TableName)
                .Where("tenantId = @0", tenantId)
                .OrderBy("timestampUtc DESC", "id DESC"));
        return page.Items.Select(Map).ToList();
    }

    // ---- GDPR ----

    public async Task<int> DeleteAgentLinksForStaffAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope();
        var deleted = await scope.Database.ExecuteAsync(
            $"DELETE FROM [{DeskAgentLinkDto.TableName}] WHERE [staffKey] = @0", staffKey);
        scope.Complete();
        return deleted;
    }

    public async Task<int> DetachParticipationForStaffAsync(Guid staffKey, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var updated = await scope.Database.ExecuteAsync(
            $"UPDATE [{SupportCaseParticipantDto.TableName}] SET [staffKey] = NULL, [agentDisplayName] = NULL, [ingestedAtUtc] = @0 " +
            "WHERE [staffKey] = @1",
            nowUtc, staffKey);
        scope.Complete();
        return updated;
    }

    public async Task<int> ScrubAgentNamesAsync(IReadOnlyList<(Guid TenantId, Guid ConnectionKey, string ExternalAgentId)> agents, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var updated = 0;
        foreach (var (tenantId, connectionKey, agentId) in agents)
        {
            updated += await scope.Database.ExecuteAsync(
                $"UPDATE [{SupportCaseParticipantDto.TableName}] SET [agentDisplayName] = NULL, [ingestedAtUtc] = @0 " +
                "WHERE [tenantId] = @1 AND [connectionKey] = @2 AND [externalAgentId] = @3 AND [agentDisplayName] IS NOT NULL",
                nowUtc, tenantId, connectionKey, agentId);
        }

        scope.Complete();
        return updated;
    }

    // ---- mapping ----

    private static DateTime? Earliest(DateTime? left, DateTime? right) =>
        left is null ? right : right is null ? left : left < right ? left : right;

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static DeskConnection Map(DeskConnectionDto dto) => new()
    {
        ConnectionKey = dto.ConnectionKey,
        TenantId = dto.TenantId,
        Provider = dto.Provider,
        SourceAccountId = dto.SourceAccountId,
        DisplayName = dto.DisplayName,
        ApiBaseUrl = dto.ApiBaseUrl,
        ApprovedComponents = Deserialize(dto.ApprovedComponentsJson),
        Status = Enum.Parse<DeskConnectionStatus>(dto.Status),
        ProtectedCredentialJson = dto.ProtectedCredentialJson,
        ConnectedByStaffKey = dto.ConnectedByStaffKey,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc,
        DisconnectedAtUtc = dto.DisconnectedAtUtc
    };

    private static SupportCaseFact Map(SupportCaseFactDto dto) => new()
    {
        CaseKey = dto.CaseKey,
        TenantId = dto.TenantId,
        ConnectionKey = dto.ConnectionKey,
        Provider = dto.Provider,
        SourceAccountId = dto.SourceAccountId,
        ExternalTicketId = dto.ExternalTicketId,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc,
        ResolvedAtUtc = dto.ResolvedAtUtc,
        ClosedAtUtc = dto.ClosedAtUtc,
        ReopenedAtUtc = dto.ReopenedAtUtc,
        State = Enum.Parse<SupportCaseState>(dto.State),
        ProviderStatus = dto.ProviderStatus,
        Priority = Enum.Parse<SupportCasePriority>(dto.Priority),
        ComponentKey = dto.ComponentKey,
        RawComponentTag = dto.RawComponentTag,
        CaseType = dto.CaseType,
        SourceUrl = dto.SourceUrl,
        IsReopened = dto.IsReopened,
        IsWithdrawn = dto.IsWithdrawn,
        ObservedInRunKey = dto.ObservedInRunKey,
        SchemaVersion = dto.SchemaVersion,
        FirstIngestedAtUtc = dto.FirstIngestedAtUtc,
        IngestedAtUtc = dto.IngestedAtUtc
    };

    private static SupportCodeLink Map(SupportCodeLinkDto dto) => new()
    {
        LinkKey = dto.LinkKey,
        TenantId = dto.TenantId,
        ConnectionKey = dto.ConnectionKey,
        ExternalTicketId = dto.ExternalTicketId,
        ArtifactType = Enum.Parse<LinkedArtifactType>(dto.ArtifactType),
        ArtifactExternalId = dto.ArtifactExternalId,
        ArtifactSource = dto.ArtifactSource,
        ArtifactUrl = dto.ArtifactUrl,
        Method = Enum.Parse<SupportLinkMethod>(dto.Method),
        ReviewedByStaffKey = dto.ReviewedByStaffKey,
        ReviewedAtUtc = dto.ReviewedAtUtc,
        ReviewNote = dto.ReviewNote,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static SupportCaseParticipant Map(SupportCaseParticipantDto dto) => new()
    {
        ParticipantKey = dto.ParticipantKey,
        TenantId = dto.TenantId,
        ConnectionKey = dto.ConnectionKey,
        ExternalTicketId = dto.ExternalTicketId,
        ExternalAgentId = dto.ExternalAgentId,
        AgentDisplayName = dto.AgentDisplayName,
        Role = Enum.Parse<SupportCaseRole>(dto.Role),
        StaffKey = dto.StaffKey,
        OccurredAtUtc = dto.OccurredAtUtc,
        IngestedAtUtc = dto.IngestedAtUtc
    };

    private static DeskAgentLink Map(DeskAgentLinkDto dto) => new()
    {
        LinkKey = dto.LinkKey,
        TenantId = dto.TenantId,
        ConnectionKey = dto.ConnectionKey,
        Provider = dto.Provider,
        ExternalAgentId = dto.ExternalAgentId,
        ExternalAgentName = dto.ExternalAgentName,
        StaffKey = dto.StaffKey,
        ApprovedByStaffKey = dto.ApprovedByStaffKey,
        ApprovedAtUtc = dto.ApprovedAtUtc
    };

    private static DeskCoverage Map(DeskCoverageDto dto) => new()
    {
        CoverageKey = dto.CoverageKey,
        TenantId = dto.TenantId,
        ConnectionKey = dto.ConnectionKey,
        Stream = Enum.Parse<DeskStream>(dto.Stream),
        Cursor = dto.Cursor,
        ObservedFromUtc = dto.ObservedFromUtc,
        CompleteThroughUtc = dto.CompleteThroughUtc,
        Status = Enum.Parse<DeskCoverageStatus>(dto.Status),
        StatusDetail = dto.StatusDetail,
        LastRunKey = dto.LastRunKey,
        LastAttemptedAtUtc = dto.LastAttemptedAtUtc,
        LastSucceededAtUtc = dto.LastSucceededAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static ServiceOpsAuditLog Map(ServiceOpsAuditLogDto dto) => new()
    {
        LogKey = dto.LogKey,
        TenantId = dto.TenantId,
        EntityType = dto.EntityType,
        EntityId = dto.EntityId,
        Action = dto.Action,
        ActorMemberId = dto.ActorMemberId,
        DetailJson = dto.DetailJson,
        TimestampUtc = dto.TimestampUtc
    };

    private static IReadOnlyList<string> Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];
}

/// <summary>
/// A Service Ops request was well-formed but not allowed by the domain
/// rules — confirming a root cause with no rationale, an unapproved
/// component, a duplicate agent mapping.
///
/// Distinct from CrossTenantReferenceException, which callers turn into
/// 404 so another tenant's keys are indistinguishable from keys that
/// never existed. This one is safe to show the user.
/// </summary>
public sealed class ServiceOpsValidationException(string message) : InvalidOperationException(message);
