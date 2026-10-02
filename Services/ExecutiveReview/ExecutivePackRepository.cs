using System.Data;
using System.Text.Json;
using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Services.ProgrammeOps;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ExecutiveReview;

public interface IExecutivePackRepository
{
    Task<IReadOnlyList<ExecutivePack>> GetRecentAsync(Guid tenantId);
    Task<ExecutivePack?> GetAsync(Guid tenantId, Guid packKey);
    Task<ExecutivePack> CaptureAsync(ReviewActor actor, string source, int maximumAgeHours);
}

public sealed class ExecutivePackRepository(
    IScopeProvider scopes, IMarketSettingsRepository markets, IProgrammeReadRepository programmes,
    ISyncRunRepository runs, ISourceConnectionRepository connections, TimeProvider clock) : IExecutivePackRepository
{
    public async Task<IReadOnlyList<ExecutivePack>> GetRecentAsync(Guid tenantId)
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        var rows = await scope.Database.FetchAsync<ExecutivePackDto>(
            "SELECT TOP 50 * FROM ExecutiveReview_Pack WHERE tenantId = @0 ORDER BY id DESC", tenantId);
        return rows.Select(Read).ToArray();
    }

    public async Task<ExecutivePack?> GetAsync(Guid tenantId, Guid packKey)
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        var row = await scope.Database.FirstOrDefaultAsync<ExecutivePackDto>(
            Sql.Builder.Where("tenantId = @0 AND packKey = @1", tenantId, packKey));
        return row is null ? null : Read(row);
    }

    public async Task<ExecutivePack> CaptureAsync(ReviewActor actor, string source, int maximumAgeHours)
    {
        // Hold the publication range and evidence rows until the snapshot is inserted.
        // Concurrent ingestion cannot start/finish a publication across this read.
        using var scope = scopes.CreateScope(isolationLevel: IsolationLevel.Serializable);
        await MarketSettingsRepository.LockTenantAsync(scope, actor.TenantId);
        var now = clock.GetUtcNow().UtcDateTime;
        var publication = await runs.GetLatestAsync(actor.TenantId, source);
        ExecutivePackPolicy.RequirePublication(publication, now, maximumAgeHours);
        var connection = await connections.GetActiveForTenantAsync(actor.TenantId, source);
        if (connection is null || string.IsNullOrWhiteSpace(connection.ExternalAccountId))
            throw new ReviewValidationException("Review.ProvenanceRequired");
        var evidence = ExecutivePackPolicy.Evidence(await programmes.GetWorkItemsAsync(actor.TenantId), actor.TenantId, source);
        // The audit actor stays in settings history, outside the read-only pack payload.
        var market = (await markets.GetAsync(actor.TenantId)) with { ChangedByMemberId = null };
        var previousRow = await scope.Database.FirstOrDefaultAsync<ExecutivePackDto>(
            "SELECT TOP 1 * FROM ExecutiveReview_Pack WHERE tenantId = @0 AND JSON_VALUE(payloadJson, '$.Source') = @1 ORDER BY id DESC",
            actor.TenantId, source);
        var previous = previousRow is null ? null : Read(previousRow);
        var pack = new ExecutivePack(Guid.NewGuid(), previous?.PackKey, now, "operational-evidence-v1", market,
            source, connection.ConnectionKey, connection.ExternalAccountId, publication!.RunKey,
            DateTime.SpecifyKind(publication.FinishedAtUtc!.Value, DateTimeKind.Utc), maximumAgeHours, evidence,
            ExecutivePackPolicy.Summarize(evidence, now));
        await scope.Database.InsertAsync(new ExecutivePackDto
        {
            PackKey = pack.PackKey, TenantId = actor.TenantId, CapturedAtUtc = now,
            CapturedByMemberId = actor.MemberId, PayloadJson = JsonSerializer.Serialize(pack)
        });
        scope.Complete();
        return pack;
    }

    private static ExecutivePack Read(ExecutivePackDto row) => JsonSerializer.Deserialize<ExecutivePack>(row.PayloadJson)!;
}
