using System.Text.Json;
using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.SkillsEvidence;
using Umbraco.Cms.Infrastructure.Scoping;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.SkillsEvidence;

public sealed class EngineeringEvidenceRepository(IScopeProvider scopeProvider, TimeProvider clock) : IEngineeringEvidenceRepository
{
    // Hold the decision through the write transaction. Withdrawal must either
    // precede this check or wait until this already-authorized write commits.
    private async Task RequireCollectionAsync(IScope scope, Guid tenantId)
    {
        var decision = await scope.Database.FirstOrDefaultAsync<EvidenceProcessingDecisionDto>(
            $"SELECT * FROM [{EvidenceProcessingDecisionDto.TableName}] WITH (UPDLOCK, HOLDLOCK) WHERE tenantId=@0 AND withdrawnAtUtc IS NULL", tenantId);
        if (decision is null || !decision.WorkerNoticeGiven || !decision.DpiaCompleted ||
            decision.ReviewDueOn.Date < clock.GetUtcNow().UtcDateTime.Date)
            throw new SkillAssertionValidationException(EvidenceProcessingDecision.MissingReason);
    }

    private static async Task<EvidenceActorLinkDto?> CurrentLinkAsync(IScope scope, Guid tenantId, Guid connectionKey, string? actorId) =>
        await scope.Database.FirstOrDefaultAsync<EvidenceActorLinkDto>(
            $"SELECT * FROM [{EvidenceActorLinkDto.TableName}] WITH (UPDLOCK, HOLDLOCK) WHERE tenantId=@0 AND connectionKey=@1 AND externalActorId=@2",
            tenantId, connectionKey, actorId);

    // ---- Connections ----

    public async Task<IReadOnlyList<EvidenceConnection>> GetConnectionsAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<EvidenceConnectionDto>(
            Sql.Builder.Where("tenantId = @0", tenantId).OrderBy("displayName"));
        return dtos.Select(Map).ToList();
    }

    public async Task<EvidenceConnection?> GetConnectionAsync(Guid connectionKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<EvidenceConnectionDto>(
            Sql.Builder.Where("connectionKey = @0 AND tenantId = @1", connectionKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<EvidenceConnection?> GetConnectionByAccountAsync(string provider, string sourceAccountId, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<EvidenceConnectionDto>(
            Sql.Builder.Where("provider = @0 AND sourceAccountId = @1 AND tenantId = @2", provider, sourceAccountId, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<EvidenceConnection> UpsertConnectionAsync(EvidenceConnection connection)
    {
        using var scope = scopeProvider.CreateScope();
        var existing = await scope.Database.FirstOrDefaultAsync<EvidenceConnectionDto>(
            Sql.Builder.Where("tenantId = @0 AND provider = @1 AND sourceAccountId = @2",
                connection.TenantId, connection.Provider, connection.SourceAccountId));

        var dto = existing ?? new EvidenceConnectionDto
        {
            ConnectionKey = connection.ConnectionKey,
            TenantId = connection.TenantId,
            Provider = connection.Provider,
            SourceAccountId = connection.SourceAccountId,
            CreatedAtUtc = connection.CreatedAtUtc
        };

        dto.DisplayName = connection.DisplayName;
        dto.InstallationId = connection.InstallationId;
        dto.ApiBaseUrl = connection.ApiBaseUrl;
        dto.SelectedRepositoriesJson = JsonSerializer.Serialize(connection.SelectedRepositories);
        dto.Status = connection.Status.ToString();
        dto.ConnectedByStaffKey = connection.ConnectedByStaffKey;
        dto.UpdatedAtUtc = connection.UpdatedAtUtc;
        dto.DisconnectedAtUtc = connection.DisconnectedAtUtc;

        // A reconnect that supplies no new credential keeps the stored one
        // rather than silently wiping a working grant.
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

    public async Task<EvidenceConnection> SetConnectionStatusAsync(
        Guid connectionKey, EvidenceConnectionStatus status, Guid tenantId, DateTime nowUtc, bool clearCredential)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<EvidenceConnectionDto>(
            Sql.Builder.Where("connectionKey = @0 AND tenantId = @1", connectionKey, tenantId))
            ?? throw new CrossTenantReferenceException("EvidenceConnection", connectionKey);

        dto.Status = status.ToString();
        dto.UpdatedAtUtc = nowUtc;
        dto.DisconnectedAtUtc = status == EvidenceConnectionStatus.Active ? null : nowUtc;

        // Disconnecting destroys the credential. Leaving an unusable grant
        // encrypted at rest is a liability with no upside — reconnecting
        // re-authorises from scratch.
        if (clearCredential)
        {
            dto.ProtectedCredentialJson = null;
        }

        await scope.Database.UpdateAsync(dto);
        scope.Complete();
        return Map(dto);
    }

    // ---- Evidence ----

    public async Task<EngineeringEvidence> UpsertEvidenceAsync(EngineeringEvidence evidence)
    {
        using var scope = scopeProvider.CreateScope();
        await RequireCollectionAsync(scope, evidence.TenantId);
        var link = await CurrentLinkAsync(scope, evidence.TenantId, evidence.ConnectionKey, evidence.ActorExternalId);
        var existing = await scope.Database.FirstOrDefaultAsync<EngineeringEvidenceDto>(
            Sql.Builder.Where(
                "tenantId = @0 AND connectionKey = @1 AND sourceType = @2 AND externalId = @3 AND [role] = @4",
                evidence.TenantId, evidence.ConnectionKey, evidence.SourceType.ToString(),
                evidence.ExternalId, evidence.Role.ToString()));

        var dto = existing ?? new EngineeringEvidenceDto
        {
            EvidenceKey = evidence.EvidenceKey,
            TenantId = evidence.TenantId,
            ConnectionKey = evidence.ConnectionKey,
            SourceType = evidence.SourceType.ToString(),
            ExternalId = evidence.ExternalId,
            Role = evidence.Role.ToString(),
            // Preserved across every later replay: when we first saw this.
            FirstIngestedAtUtc = evidence.FirstIngestedAtUtc
        };

        dto.Provider = evidence.Provider;
        dto.SourceAccountId = evidence.SourceAccountId;
        dto.ActorExternalId = evidence.ActorExternalId;
        dto.ActorLogin = evidence.ActorLogin;
        dto.ActorEmail = evidence.ActorEmail;
        dto.ActorIsBot = evidence.ActorIsBot;
        dto.StaffKey = !evidence.ActorIsBot && link?.Provider == evidence.Provider ? link.StaffKey : null;
        dto.AttributionStatus = (evidence.ActorIsBot ? EvidenceAttributionStatus.Bot :
            dto.StaffKey is not null ? EvidenceAttributionStatus.Mapped :
            evidence.AttributionStatus == EvidenceAttributionStatus.Ambiguous ? EvidenceAttributionStatus.Ambiguous : EvidenceAttributionStatus.Unmapped).ToString();
        dto.RepositoryKey = evidence.RepositoryKey;
        dto.Title = Truncate(evidence.Title, 512);
        dto.SourceUrl = Truncate(evidence.SourceUrl, 512);
        dto.OccurredAtUtc = evidence.OccurredAtUtc;
        dto.LanguageHintsJson = evidence.LanguageHints.Count == 0 ? null : JsonSerializer.Serialize(evidence.LanguageHints);
        dto.AuthorshipVerified = evidence.AuthorshipVerified;
        dto.ObservedInRunKey = evidence.ObservedInRunKey;
        dto.SchemaVersion = evidence.SchemaVersion;
        dto.UpdatedAtUtc = evidence.UpdatedAtUtc;

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

    public async Task<IReadOnlyList<EngineeringEvidence>> GetEvidenceForStaffAsync(Guid staffKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<EngineeringEvidenceDto>(
            Sql.Builder.Where("staffKey = @0 AND tenantId = @1", staffKey, tenantId).OrderBy("occurredAtUtc DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<ResultPage<EngineeringEvidence>> GetEvidencePageForStaffAsync(Guid staffKey, Guid tenantId, PageRequest page)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<EngineeringEvidenceDto>(
            Sql.Builder.Where("staffKey = @0 AND tenantId = @1", staffKey, tenantId).OrderBy("occurredAtUtc DESC", "id DESC").ForPage(page));
        return ResultPage<EngineeringEvidence>.From(dtos.Select(Map).ToList(), page);
    }

    public async Task<EvidenceSummary> GetEvidenceSummaryForStaffAsync(Guid staffKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var byRole = await scope.Database.FetchAsync<RoleCountRow>(
            $"SELECT [role] AS Role, COUNT(*) AS Count, MIN(occurredAtUtc) AS EarliestUtc, MAX(occurredAtUtc) AS LatestUtc FROM {EngineeringEvidenceDto.TableName} WHERE staffKey = @0 AND tenantId = @1 GROUP BY [role]",
            staffKey, tenantId);
        // Distinct stored hint lists, not rows: a person's artefacts repeat
        // the same few combinations, so this stays small however long the record.
        var hintLists = await scope.Database.FetchAsync<string>(
            $"SELECT DISTINCT languageHintsJson FROM {EngineeringEvidenceDto.TableName} WHERE staffKey = @0 AND tenantId = @1 AND languageHintsJson IS NOT NULL",
            staffKey, tenantId);

        return new EvidenceSummary(
            byRole.Sum(r => r.Count),
            byRole.ToDictionary(r => Enum.Parse<EvidenceRole>(r.Role), r => r.Count),
            [.. hintLists.SelectMany(Deserialize).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            byRole.Count == 0 ? null : byRole.Min(r => r.EarliestUtc),
            byRole.Count == 0 ? null : byRole.Max(r => r.LatestUtc));
    }

    private sealed class RoleCountRow
    {
        public string Role { get; set; } = string.Empty;

        public int Count { get; set; }

        public DateTime EarliestUtc { get; set; }

        public DateTime LatestUtc { get; set; }
    }

    public async Task<IReadOnlyList<EngineeringEvidence>> GetEvidenceForConnectionAsync(Guid connectionKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<EngineeringEvidenceDto>(
            Sql.Builder.Where("connectionKey = @0 AND tenantId = @1", connectionKey, tenantId).OrderBy("occurredAtUtc DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<int> CountUnattributedAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return await scope.Database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM [{EngineeringEvidenceDto.TableName}] WHERE [tenantId] = @0 AND [staffKey] IS NULL AND [actorIsBot] = 0",
            tenantId);
    }

    public async Task<int> AttributeEvidenceToStaffAsync(Guid connectionKey, string externalActorId, Guid staffKey, Guid tenantId, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var link = await CurrentLinkAsync(scope, tenantId, connectionKey, externalActorId);
        if (link?.StaffKey != staffKey) throw new UnauthorizedAccessException();
        var updated = await scope.Database.ExecuteAsync(
            $"UPDATE [{EngineeringEvidenceDto.TableName}] SET [staffKey] = @0, [attributionStatus] = @1, [updatedAtUtc] = @2 " +
            "WHERE [tenantId] = @3 AND [connectionKey] = @4 AND [actorExternalId] = @5 AND [actorIsBot] = 0",
            staffKey, nameof(EvidenceAttributionStatus.Mapped), nowUtc, tenantId, connectionKey, externalActorId);
        scope.Complete();
        return updated;
    }

    public async Task<int> DetachEvidenceFromStaffAsync(Guid connectionKey, string externalActorId, Guid tenantId, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var updated = await scope.Database.ExecuteAsync(
            $"UPDATE [{EngineeringEvidenceDto.TableName}] SET [staffKey] = NULL, [attributionStatus] = @0, [updatedAtUtc] = @1 " +
            "WHERE [tenantId] = @2 AND [connectionKey] = @3 AND [actorExternalId] = @4",
            nameof(EvidenceAttributionStatus.Unmapped), nowUtc, tenantId, connectionKey, externalActorId);
        scope.Complete();
        return updated;
    }

    // ---- Actor links and queue ----

    public async Task<IReadOnlyList<EvidenceActorLink>> GetActorLinksAsync(Guid connectionKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<EvidenceActorLinkDto>(
            Sql.Builder.Where("connectionKey = @0 AND tenantId = @1", connectionKey, tenantId));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<EvidenceActorLink>> GetActorLinksForStaffAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<EvidenceActorLinkDto>(Sql.Builder.Where("staffKey = @0", staffKey));
        return dtos.Select(Map).ToList();
    }

    public async Task<EvidenceActorLink> CreateActorLinkAsync(EvidenceActorLink link)
    {
        using var scope = scopeProvider.CreateScope();

        // The connection has to be the caller's. A link is the thing that
        // publishes evidence to a person, so a forged connection key here
        // would be the most damaging possible cross-tenant write.
        var connection = await scope.Database.FirstOrDefaultAsync<EvidenceConnectionDto>(
            Sql.Builder.Where("connectionKey = @0 AND tenantId = @1", link.ConnectionKey, link.TenantId));
        if (connection is null)
        {
            throw new CrossTenantReferenceException("EvidenceConnection", link.ConnectionKey);
        }

        var clash = await scope.Database.FirstOrDefaultAsync<EvidenceActorLinkDto>(
            Sql.Builder.Where("tenantId = @0 AND connectionKey = @1 AND externalActorId = @2",
                link.TenantId, link.ConnectionKey, link.ExternalActorId));
        if (clash is not null)
        {
            throw new SkillAssertionValidationException(
                $"'{link.ExternalLogin ?? link.ExternalActorId}' is already mapped on this connection. Remove that mapping first.");
        }

        var dto = new EvidenceActorLinkDto
        {
            LinkKey = link.LinkKey,
            TenantId = link.TenantId,
            ConnectionKey = link.ConnectionKey,
            Provider = link.Provider,
            ExternalActorId = link.ExternalActorId,
            ExternalLogin = link.ExternalLogin,
            StaffKey = link.StaffKey,
            ApprovedByStaffKey = link.ApprovedByStaffKey,
            ApprovedAtUtc = link.ApprovedAtUtc
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();
        return Map(dto);
    }

    public async Task DeleteActorLinkAsync(Guid linkKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var link = await scope.Database.FirstOrDefaultAsync<EvidenceActorLinkDto>(
            $"SELECT * FROM [{EvidenceActorLinkDto.TableName}] WITH (UPDLOCK, HOLDLOCK) WHERE linkKey=@0 AND tenantId=@1", linkKey, tenantId);
        if (link is null) return;
        await scope.Database.ExecuteAsync(
            $"DELETE FROM [{EvidenceActorLinkDto.TableName}] WHERE [linkKey] = @0 AND [tenantId] = @1", linkKey, tenantId);
        await scope.Database.ExecuteAsync(
            $"UPDATE [{EngineeringEvidenceDto.TableName}] SET staffKey=NULL, attributionStatus=@0 WHERE tenantId=@1 AND connectionKey=@2 AND actorExternalId=@3",
            nameof(EvidenceAttributionStatus.Unmapped), tenantId, link.ConnectionKey, link.ExternalActorId);
        scope.Complete();
    }

    public async Task<IReadOnlyList<UnmappedEvidenceActor>> GetUnmappedActorsAsync(Guid tenantId, bool openOnly)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var sql = openOnly
            ? Sql.Builder.Where("tenantId = @0 AND resolvedAtUtc IS NULL", tenantId)
            : Sql.Builder.Where("tenantId = @0", tenantId);
        var dtos = await scope.Database.FetchAsync<UnmappedEvidenceActorDto>(sql.OrderBy("occurrenceCount DESC", "lastSeenUtc DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<ResultPage<UnmappedEvidenceActor>> GetUnmappedActorsPageAsync(Guid tenantId, PageRequest page)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<UnmappedEvidenceActorDto>(
            Sql.Builder.Where("tenantId = @0 AND resolvedAtUtc IS NULL", tenantId)
                .OrderBy("occurrenceCount DESC", "lastSeenUtc DESC", "id DESC")
                .ForPage(page));
        return ResultPage<UnmappedEvidenceActor>.From(dtos.Select(Map).ToList(), page);
    }

    public async Task<int> CountOpenUnmappedActorsAsync(Guid tenantId, bool includeBots)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return await scope.Database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {UnmappedEvidenceActorDto.TableName} WHERE tenantId = @0 AND resolvedAtUtc IS NULL AND (@1 = 1 OR isBot = 0)",
            tenantId, includeBots ? 1 : 0);
    }

    public async Task<UnmappedEvidenceActor?> GetUnmappedActorAsync(Guid unmappedActorKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<UnmappedEvidenceActorDto>(
            Sql.Builder.Where("unmappedActorKey = @0 AND tenantId = @1", unmappedActorKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<UnmappedEvidenceActor> RecordUnmappedSightingAsync(UnmappedEvidenceActor sighting)
    {
        using var scope = scopeProvider.CreateScope();
        await RequireCollectionAsync(scope, sighting.TenantId);
        var existing = await scope.Database.FirstOrDefaultAsync<UnmappedEvidenceActorDto>(
            Sql.Builder.Where("tenantId = @0 AND connectionKey = @1 AND externalActorId = @2",
                sighting.TenantId, sighting.ConnectionKey, sighting.ExternalActorId));

        if (existing is null)
        {
            var inserted = new UnmappedEvidenceActorDto
            {
                UnmappedActorKey = sighting.UnmappedActorKey,
                TenantId = sighting.TenantId,
                ConnectionKey = sighting.ConnectionKey,
                Provider = sighting.Provider,
                ExternalActorId = sighting.ExternalActorId,
                ExternalLogin = sighting.ExternalLogin,
                DisplayName = sighting.DisplayName,
                Email = sighting.Email,
                IsBot = sighting.IsBot,
                Reason = sighting.Reason.ToString(),
                SuggestedStaffKey = sighting.SuggestedStaffKey,
                OccurrenceCount = 1,
                FirstSeenUtc = sighting.FirstSeenUtc,
                LastSeenUtc = sighting.LastSeenUtc
            };

            await scope.Database.InsertAsync(inserted);
            scope.Complete();
            return Map(inserted);
        }

        existing.OccurrenceCount += 1;
        existing.LastSeenUtc = sighting.LastSeenUtc;
        existing.ExternalLogin = sighting.ExternalLogin ?? existing.ExternalLogin;
        existing.DisplayName = sighting.DisplayName ?? existing.DisplayName;
        existing.Email = sighting.Email ?? existing.Email;
        existing.IsBot = sighting.IsBot;
        existing.Reason = sighting.Reason.ToString();
        existing.SuggestedStaffKey = sighting.SuggestedStaffKey;
        // Seen again after being resolved means the link that resolved it no
        // longer covers this actor. Re-open rather than absorb.
        existing.ResolvedAtUtc = null;

        await scope.Database.UpdateAsync(existing);
        scope.Complete();
        return Map(existing);
    }

    public async Task MarkUnmappedResolvedAsync(Guid unmappedActorKey, Guid tenantId, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"UPDATE [{UnmappedEvidenceActorDto.TableName}] SET [resolvedAtUtc] = @0 WHERE [unmappedActorKey] = @1 AND [tenantId] = @2",
            nowUtc, unmappedActorKey, tenantId);
        scope.Complete();
    }

    // ---- Coverage ----

    public async Task<IReadOnlyList<EvidenceCoverage>> GetCoverageAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<EvidenceCoverageDto>(
            Sql.Builder.Where("tenantId = @0", tenantId).OrderBy("repositoryKey", "stream"));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<EvidenceCoverage>> GetCoverageForConnectionAsync(Guid connectionKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<EvidenceCoverageDto>(
            Sql.Builder.Where("connectionKey = @0 AND tenantId = @1", connectionKey, tenantId).OrderBy("repositoryKey", "stream"));
        return dtos.Select(Map).ToList();
    }

    public async Task<EvidenceCoverage> UpsertCoverageAsync(EvidenceCoverage coverage)
    {
        using var scope = scopeProvider.CreateScope();
        await RequireCollectionAsync(scope, coverage.TenantId);
        var existing = await scope.Database.FirstOrDefaultAsync<EvidenceCoverageDto>(
            Sql.Builder.Where("tenantId = @0 AND connectionKey = @1 AND repositoryKey = @2 AND stream = @3",
                coverage.TenantId, coverage.ConnectionKey, coverage.RepositoryKey, coverage.Stream.ToString()));

        var dto = existing ?? new EvidenceCoverageDto
        {
            CoverageKey = coverage.CoverageKey,
            TenantId = coverage.TenantId,
            ConnectionKey = coverage.ConnectionKey,
            RepositoryKey = coverage.RepositoryKey,
            Stream = coverage.Stream.ToString()
        };

        dto.Cursor = Truncate(coverage.Cursor, 512);
        // Never moves backwards: the earliest point ever observed is a fact
        // about history, not about this run.
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

    public async Task<int> MarkCoverageOutOfScopeAsync(Guid connectionKey, IReadOnlyCollection<string> stillSelected, Guid tenantId, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();

        var sql = $"UPDATE [{EvidenceCoverageDto.TableName}] SET [status] = @0, [updatedAtUtc] = @1 " +
                  "WHERE [tenantId] = @2 AND [connectionKey] = @3 AND [status] <> @0";
        var args = new List<object> { nameof(EvidenceCoverageStatus.OutOfScope), nowUtc, tenantId, connectionKey };

        if (stillSelected.Count > 0)
        {
            // Parameterised IN list — repository keys are validated by
            // EvidenceHostPolicy, but they still never reach SQL as text.
            var placeholders = string.Join(",", stillSelected.Select((_, index) => $"@{args.Count + index}"));
            sql += $" AND [repositoryKey] NOT IN ({placeholders})";
            args.AddRange(stillSelected);
        }

        var updated = await scope.Database.ExecuteAsync(sql, [.. args]);
        scope.Complete();
        return updated;
    }

    // ---- Bronze ----

    public async Task SaveRawAsync(Guid tenantId, Guid connectionKey, string provider, string sourceAccountId, string entityType, string externalId, string payloadJson, DateTime fetchedAtUtc)
    {
        using var scope = scopeProvider.CreateScope();
        await RequireCollectionAsync(scope, tenantId);
        await scope.Database.InsertAsync(new RawEvidencePayloadDto
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
            $"DELETE FROM [{RawEvidencePayloadDto.TableName}] WHERE [fetchedAtUtc] < @0", cutoffUtc);
        scope.Complete();
        return deleted;
    }

    // ---- GDPR ----

    public async Task<int> DeleteActorLinksForStaffAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope();
        var deleted = await scope.Database.ExecuteAsync(
            $"DELETE FROM [{EvidenceActorLinkDto.TableName}] WHERE [staffKey] = @0", staffKey);
        scope.Complete();
        return deleted;
    }

    public async Task<int> DeleteEvidenceForStaffAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope();
        var deleted = await scope.Database.ExecuteAsync(
            $"DELETE FROM [{EngineeringEvidenceDto.TableName}] WHERE [staffKey] = @0", staffKey);
        scope.Complete();
        return deleted;
    }


    public async Task<EvidenceErasureCounts> EraseSubjectTracesAsync(Guid staffKey, IReadOnlyList<(Guid TenantId, Guid ConnectionKey, string ExternalActorId)> identities)
    {
        using var scope = scopeProvider.CreateScope();
        var db = scope.Database;

        // The artefacts first, so their Bronze pages can be found after the rows go.
        var artefacts = (await db.FetchAsync<ArtefactRow>(
                $"SELECT DISTINCT [tenantId] AS TenantId, [connectionKey] AS ConnectionKey, [externalId] AS ExternalId FROM [{EngineeringEvidenceDto.TableName}] WHERE [staffKey] = @0",
                staffKey))
            .ToList();
        foreach (var (tenantId, connectionKey, actorId) in identities)
        {
            artefacts.AddRange(await db.FetchAsync<ArtefactRow>(
                $"SELECT DISTINCT [tenantId] AS TenantId, [connectionKey] AS ConnectionKey, [externalId] AS ExternalId FROM [{EngineeringEvidenceDto.TableName}] " +
                "WHERE [tenantId] = @0 AND [connectionKey] = @1 AND [actorExternalId] = @2",
                tenantId, connectionKey, actorId));
        }

        var evidence = await db.ExecuteAsync($"DELETE FROM [{EngineeringEvidenceDto.TableName}] WHERE [staffKey] = @0", staffKey);
        var unmapped = 0;
        foreach (var (tenantId, connectionKey, actorId) in identities)
        {
            evidence += await db.ExecuteAsync(
                $"DELETE FROM [{EngineeringEvidenceDto.TableName}] WHERE [tenantId] = @0 AND [connectionKey] = @1 AND [actorExternalId] = @2",
                tenantId, connectionKey, actorId);
            unmapped += await db.ExecuteAsync(
                $"DELETE FROM [{UnmappedEvidenceActorDto.TableName}] WHERE [tenantId] = @0 AND [connectionKey] = @1 AND [externalActorId] = @2",
                tenantId, connectionKey, actorId);
        }

        var raw = 0;
        foreach (var group in artefacts.DistinctBy(a => (a.TenantId, a.ConnectionKey, a.ExternalId)).GroupBy(a => (a.TenantId, a.ConnectionKey)))
        {
            foreach (var chunk in group.Select(a => a.ExternalId).Chunk(SqlInList.ChunkSize))
            {
                raw += await db.ExecuteAsync(
                    $"DELETE FROM [{RawEvidencePayloadDto.TableName}] WHERE [tenantId] = @0 AND [connectionKey] = @1 AND [externalId] IN (@2)",
                    group.Key.TenantId, group.Key.ConnectionKey, chunk);
            }
        }

        scope.Complete();
        return new EvidenceErasureCounts(evidence, unmapped, raw);
    }

    private sealed class ArtefactRow
    {
        public Guid TenantId { get; set; }
        public Guid ConnectionKey { get; set; }
        public string ExternalId { get; set; } = null!;
    }
    // ---- mapping ----

    private static DateTime? Earliest(DateTime? left, DateTime? right) =>
        left is null ? right : right is null ? left : left < right ? left : right;

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static EvidenceConnection Map(EvidenceConnectionDto dto) => new()
    {
        ConnectionKey = dto.ConnectionKey,
        TenantId = dto.TenantId,
        Provider = dto.Provider,
        SourceAccountId = dto.SourceAccountId,
        DisplayName = dto.DisplayName,
        InstallationId = dto.InstallationId,
        ApiBaseUrl = dto.ApiBaseUrl,
        SelectedRepositories = Deserialize(dto.SelectedRepositoriesJson),
        Status = Enum.Parse<EvidenceConnectionStatus>(dto.Status),
        ProtectedCredentialJson = dto.ProtectedCredentialJson,
        ConnectedByStaffKey = dto.ConnectedByStaffKey,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc,
        DisconnectedAtUtc = dto.DisconnectedAtUtc
    };

    private static EngineeringEvidence Map(EngineeringEvidenceDto dto) => new()
    {
        EvidenceKey = dto.EvidenceKey,
        TenantId = dto.TenantId,
        ConnectionKey = dto.ConnectionKey,
        Provider = dto.Provider,
        SourceAccountId = dto.SourceAccountId,
        SourceType = Enum.Parse<EvidenceSourceType>(dto.SourceType),
        ExternalId = dto.ExternalId,
        Role = Enum.Parse<EvidenceRole>(dto.Role),
        ActorExternalId = dto.ActorExternalId,
        ActorLogin = dto.ActorLogin,
        ActorEmail = dto.ActorEmail,
        ActorIsBot = dto.ActorIsBot,
        StaffKey = dto.StaffKey,
        AttributionStatus = Enum.Parse<EvidenceAttributionStatus>(dto.AttributionStatus),
        RepositoryKey = dto.RepositoryKey,
        Title = dto.Title,
        SourceUrl = dto.SourceUrl,
        OccurredAtUtc = dto.OccurredAtUtc,
        LanguageHints = Deserialize(dto.LanguageHintsJson),
        AuthorshipVerified = dto.AuthorshipVerified,
        ObservedInRunKey = dto.ObservedInRunKey,
        SchemaVersion = dto.SchemaVersion,
        FirstIngestedAtUtc = dto.FirstIngestedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static EvidenceActorLink Map(EvidenceActorLinkDto dto) => new()
    {
        LinkKey = dto.LinkKey,
        TenantId = dto.TenantId,
        ConnectionKey = dto.ConnectionKey,
        Provider = dto.Provider,
        ExternalActorId = dto.ExternalActorId,
        ExternalLogin = dto.ExternalLogin,
        StaffKey = dto.StaffKey,
        ApprovedByStaffKey = dto.ApprovedByStaffKey,
        ApprovedAtUtc = dto.ApprovedAtUtc
    };

    private static UnmappedEvidenceActor Map(UnmappedEvidenceActorDto dto) => new()
    {
        UnmappedActorKey = dto.UnmappedActorKey,
        TenantId = dto.TenantId,
        ConnectionKey = dto.ConnectionKey,
        Provider = dto.Provider,
        ExternalActorId = dto.ExternalActorId,
        ExternalLogin = dto.ExternalLogin,
        DisplayName = dto.DisplayName,
        Email = dto.Email,
        IsBot = dto.IsBot,
        Reason = Enum.Parse<UnmappedActorReason>(dto.Reason),
        SuggestedStaffKey = dto.SuggestedStaffKey,
        OccurrenceCount = dto.OccurrenceCount,
        FirstSeenUtc = dto.FirstSeenUtc,
        LastSeenUtc = dto.LastSeenUtc,
        ResolvedAtUtc = dto.ResolvedAtUtc
    };

    private static EvidenceCoverage Map(EvidenceCoverageDto dto) => new()
    {
        CoverageKey = dto.CoverageKey,
        TenantId = dto.TenantId,
        ConnectionKey = dto.ConnectionKey,
        RepositoryKey = dto.RepositoryKey,
        Stream = Enum.Parse<EvidenceStream>(dto.Stream),
        Cursor = dto.Cursor,
        ObservedFromUtc = dto.ObservedFromUtc,
        CompleteThroughUtc = dto.CompleteThroughUtc,
        Status = Enum.Parse<EvidenceCoverageStatus>(dto.Status),
        StatusDetail = dto.StatusDetail,
        LastRunKey = dto.LastRunKey,
        LastAttemptedAtUtc = dto.LastAttemptedAtUtc,
        LastSucceededAtUtc = dto.LastSucceededAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static IReadOnlyList<string> Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];
}
