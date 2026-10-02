using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Programme;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class SyncRunRepository(IScopeProvider scopeProvider) : ISyncRunRepository
{
    public const string AbandonedError = "Abandoned: the process running this sync stopped heartbeating before it finished. Re-run the sync.";

    public async Task<SyncRun?> TryAcquireAsync(Guid tenantId, string source, string instanceId, int? triggeredByMemberId, DateTime nowUtc, TimeSpan leaseDuration)
    {
        await EnsureLeaseRowExistsAsync(tenantId, source);

        var runKey = Guid.NewGuid();
        var leaseSeconds = LeaseSeconds(leaseDuration);

        using var scope = scopeProvider.CreateScope();

        // The whole concurrency story is this one conditional UPDATE: it
        // either wins the row (1 affected) or somebody else holds a live
        // lease (0 affected). No read-then-write, so two instances racing
        // cannot both win. Expiry is set and compared on the database
        // clock so two nodes with skewed clocks agree on who is live.
        // Keyed on (tenantId, source) so one tenant's sync never serializes
        // against another's.
        var acquired = await scope.Database.ExecuteAsync(
            $"UPDATE [{SyncLeaseDto.TableName}] SET [ownerInstanceId] = @0, [leaseExpiresAtUtc] = DATEADD(second, @1, SYSUTCDATETIME()), [runKey] = @2 " +
            "WHERE [tenantId] = @3 AND [source] = @4 AND ([ownerInstanceId] IS NULL OR [leaseExpiresAtUtc] IS NULL OR [leaseExpiresAtUtc] < SYSUTCDATETIME())",
            instanceId, leaseSeconds, runKey, tenantId, source);

        if (acquired == 0)
        {
            scope.Complete();
            return null;
        }

        await scope.Database.ExecuteAsync(
            $"UPDATE [{SyncRunDto.TableName}] SET [status] = @0, [error] = @1, [finishedAtUtc] = @2 WHERE [tenantId] = @3 AND [source] = @4 AND [status] = @5",
            SyncRunStatus.Failed.ToString(), AbandonedError, nowUtc, tenantId, source, SyncRunStatus.Running.ToString());

        var dto = new SyncRunDto
        {
            RunKey = runKey,
            TenantId = tenantId,
            Source = source,
            Status = SyncRunStatus.Running.ToString(),
            StartedAtUtc = nowUtc,
            HeartbeatAtUtc = nowUtc,
            TriggeredByMemberId = triggeredByMemberId,
            InstanceId = instanceId,
            Stage = "starting"
        };
        await scope.Database.InsertAsync(dto);

        scope.Complete();
        return Map(dto);
    }

    public async Task<bool> HeartbeatAsync(Guid runKey, Guid tenantId, string source, string instanceId, string? stage, DateTime nowUtc, TimeSpan leaseDuration)
    {
        using var scope = scopeProvider.CreateScope();
        var stillOwner = await RenewIfOwnerAsync(scope, tenantId, source, instanceId, runKey, LeaseSeconds(leaseDuration));

        if (stillOwner)
        {
            await scope.Database.ExecuteAsync(
                $"UPDATE [{SyncRunDto.TableName}] SET [heartbeatAtUtc] = @0, [stage] = @1 WHERE [runKey] = @2 AND [status] = @3",
                nowUtc, Truncate(stage, 256), runKey, SyncRunStatus.Running.ToString());
        }

        scope.Complete();
        return stillOwner;
    }

    public async Task<bool> CompleteAsync(Guid runKey, Guid tenantId, string source, string instanceId, string summary, string summaryJson, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();

        // Fence: the completion is only valid if this worker still owns the
        // lease for this exact run. Renewing (rather than just reading) the
        // lease row makes the check a conditional write, the same shape as
        // acquisition, so it cannot race a takeover.
        var completed = false;
        if (await RenewIfOwnerAsync(scope, tenantId, source, instanceId, runKey, leaseSeconds: 60))
        {
            completed = await scope.Database.ExecuteAsync(
                $"UPDATE [{SyncRunDto.TableName}] SET [status] = @0, [finishedAtUtc] = @1, [heartbeatAtUtc] = @1, [summary] = @2, [summaryJson] = @3, [stage] = NULL WHERE [runKey] = @4 AND [status] = @5",
                SyncRunStatus.Succeeded.ToString(), nowUtc, Truncate(summary, 512), Truncate(summaryJson, 2000), runKey, SyncRunStatus.Running.ToString()) > 0;
        }

        scope.Complete();
        return completed;
    }

    public async Task FailAsync(Guid runKey, string? stage, string error, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"UPDATE [{SyncRunDto.TableName}] SET [status] = @0, [finishedAtUtc] = @1, [heartbeatAtUtc] = @1, [stage] = @2, [error] = @3 WHERE [runKey] = @4 AND [status] = @5",
            SyncRunStatus.Failed.ToString(), nowUtc, Truncate(stage, 256), Truncate(error, 2000), runKey, SyncRunStatus.Running.ToString());
        scope.Complete();
    }

    public async Task ReleaseAsync(Guid tenantId, string source, string instanceId, Guid runKey)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"UPDATE [{SyncLeaseDto.TableName}] SET [ownerInstanceId] = NULL, [leaseExpiresAtUtc] = NULL, [runKey] = NULL WHERE [tenantId] = @0 AND [source] = @1 AND [ownerInstanceId] = @2 AND [runKey] = @3",
            tenantId, source, instanceId, runKey);
        scope.Complete();
    }

    public Task<SyncRun?> GetLatestAsync(Guid tenantId, string source) =>
        FirstAsync(Sql.Builder.Where("tenantId = @0 AND source = @1", tenantId, source).OrderBy("startedAtUtc DESC", "id DESC"));

    public Task<SyncRun?> GetLatestSuccessfulAsync(Guid tenantId, string source) =>
        FirstAsync(Sql.Builder.Where("tenantId = @0 AND source = @1 AND status = @2", tenantId, source, SyncRunStatus.Succeeded.ToString()).OrderBy("finishedAtUtc DESC", "id DESC"));

    public Task<SyncRun?> GetRunningAsync(Guid tenantId, string source) =>
        FirstAsync(Sql.Builder.Where("tenantId = @0 AND source = @1 AND status = @2", tenantId, source, SyncRunStatus.Running.ToString()).OrderBy("startedAtUtc DESC", "id DESC"));

    public Task<SyncRun?> GetLatestAcrossTenantsAsync(string source) =>
        FirstAsync(Sql.Builder.Where("source = @0", source).OrderBy("startedAtUtc DESC", "id DESC"));

    public Task<SyncRun?> GetLatestSuccessfulAcrossTenantsAsync(string source) =>
        FirstAsync(Sql.Builder.Where("source = @0 AND status = @1", source, SyncRunStatus.Succeeded.ToString()).OrderBy("finishedAtUtc DESC", "id DESC"));

    public async Task<int> DeleteFinishedOlderThanAsync(DateTime cutoffUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var deleted = await scope.Database.ExecuteAsync(
            $"DELETE FROM [{SyncRunDto.TableName}] WHERE [status] <> @0 AND [finishedAtUtc] < @1",
            SyncRunStatus.Running.ToString(), cutoffUtc);
        scope.Complete();
        return deleted;
    }

    /// <summary>
    /// The ownership fence shared by heartbeat and completion: one
    /// conditional UPDATE on (tenant, source, owner, runKey); 1 row means
    /// still ours.
    /// </summary>
    private static async Task<bool> RenewIfOwnerAsync(IScope scope, Guid tenantId, string source, string instanceId, Guid runKey, int leaseSeconds)
    {
        var affected = await scope.Database.ExecuteAsync(
            $"UPDATE [{SyncLeaseDto.TableName}] SET [leaseExpiresAtUtc] = DATEADD(second, @0, SYSUTCDATETIME()) WHERE [tenantId] = @1 AND [source] = @2 AND [ownerInstanceId] = @3 AND [runKey] = @4",
            leaseSeconds, tenantId, source, instanceId, runKey);
        return affected > 0;
    }

    private static int LeaseSeconds(TimeSpan leaseDuration) => (int)Math.Max(1, Math.Ceiling(leaseDuration.TotalSeconds));

    /// <summary>
    /// Lazily seeds the per-(tenant, source) lease row. Two instances doing
    /// this at once race on the unique index; the loser's insert fails and
    /// that is fine — the row exists either way. Kept in its own scope so
    /// the failed insert never poisons the acquisition transaction.
    /// </summary>
    private async Task EnsureLeaseRowExistsAsync(Guid tenantId, string source)
    {
        using var scope = scopeProvider.CreateScope();
        var exists = await scope.Database.FirstOrDefaultAsync<SyncLeaseDto>(Sql.Builder.Where("tenantId = @0 AND source = @1", tenantId, source));
        if (exists is null)
        {
            try
            {
                await scope.Database.InsertAsync(new SyncLeaseDto { TenantId = tenantId, Source = source });
            }
            catch (Exception)
            {
                // Lost the seeding race — the other instance's row is there.
            }
        }

        scope.Complete();
    }

    private async Task<SyncRun?> FirstAsync(Sql sql)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var page = await scope.Database.PageAsync<SyncRunDto>(1, 1,
            Sql.Builder.Select("*").From(SyncRunDto.TableName).Append(sql));
        var dto = page.Items.FirstOrDefault();
        return dto is null ? null : Map(dto);
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static SyncRun Map(SyncRunDto dto) => new()
    {
        RunKey = dto.RunKey,
        TenantId = dto.TenantId,
        Source = dto.Source,
        Status = Enum.Parse<SyncRunStatus>(dto.Status),
        StartedAtUtc = dto.StartedAtUtc,
        HeartbeatAtUtc = dto.HeartbeatAtUtc,
        FinishedAtUtc = dto.FinishedAtUtc,
        TriggeredByMemberId = dto.TriggeredByMemberId,
        InstanceId = dto.InstanceId,
        Stage = dto.Stage,
        Summary = dto.Summary,
        SummaryJson = dto.SummaryJson,
        Error = dto.Error
    };
}
