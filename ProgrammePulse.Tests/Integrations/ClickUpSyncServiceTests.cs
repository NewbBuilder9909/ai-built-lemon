using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Tests.ProgrammeOps;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Integrations.ClickUp.Raw;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Integrations.ClickUp;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.Integrations;

/// <summary>
/// The operational contract of a sync run (see ClickUpSyncService's doc
/// comment): concurrency refusal, failure auditing with a re-runnable
/// message, success auditing, and the Bronze retention purge. Mapping
/// correctness itself is covered by ClickUpMappingServiceTests.
/// </summary>
public class ClickUpSyncServiceTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeClickUpApiClient : IClickUpApiClient
    {
        public List<RawEntity<ClickUpSpaceDto>> Spaces { get; } = [];
        /// <summary>Overrides Spaces for a specific workspaceId — used to prove two tenants resolve independent accounts.</summary>
        public Dictionary<string, List<RawEntity<ClickUpSpaceDto>>> SpacesByWorkspace { get; } = [];
        public Dictionary<string, List<RawEntity<ClickUpFolderDto>>> FoldersBySpace { get; } = [];
        public Dictionary<string, List<RawEntity<ClickUpListDto>>> ListsByFolder { get; } = [];
        public Dictionary<string, List<RawEntity<ClickUpTaskDto>>> TasksByList { get; } = [];
        public Exception? ThrowWhenFetchingTasks { get; set; }
        /// <summary>Runs once the tasks of a list are fetched, before any of them is written — where a stall would happen.</summary>
        public Func<Task>? AfterTasksFetched { get; set; }
        public int Calls { get; private set; }
        public string? LastWorkspaceIdRequested { get; private set; }
        public string? LastCredentialUsed { get; private set; }

        public IClickUpApiClient WithCredential(string apiToken) { LastCredentialUsed = apiToken; return this; }

        public Task<IReadOnlyList<RawEntity<ClickUpUserDto>>> GetWorkspaceMembersAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Count<ClickUpUserDto>([]);

        public Task<IReadOnlyList<RawEntity<ClickUpSpaceDto>>> GetSpacesAsync(string workspaceId, CancellationToken cancellationToken = default)
        {
            LastWorkspaceIdRequested = workspaceId;
            return Count(SpacesByWorkspace.GetValueOrDefault(workspaceId, Spaces));
        }

        public Task<IReadOnlyList<RawEntity<ClickUpFolderDto>>> GetFoldersAsync(string spaceId, CancellationToken cancellationToken = default) =>
            Count(FoldersBySpace.GetValueOrDefault(spaceId, []));

        public Task<IReadOnlyList<RawEntity<ClickUpListDto>>> GetListsAsync(string folderId, CancellationToken cancellationToken = default) =>
            Count(ListsByFolder.GetValueOrDefault(folderId, []));

        public Task<IReadOnlyList<RawEntity<ClickUpListDto>>> GetFolderlessListsAsync(string spaceId, CancellationToken cancellationToken = default) =>
            Count<ClickUpListDto>([]);

        public async Task<IReadOnlyList<RawEntity<ClickUpTaskDto>>> GetTasksAsync(string listId, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (ThrowWhenFetchingTasks is not null)
            {
                throw ThrowWhenFetchingTasks;
            }

            if (AfterTasksFetched is not null)
            {
                await AfterTasksFetched();
            }

            return TasksByList.GetValueOrDefault(listId, []);
        }

        public Task<IReadOnlyList<RawEntity<ClickUpTimeEntryDto>>> GetTimeEntriesAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Count<ClickUpTimeEntryDto>([]);

        private Task<IReadOnlyList<RawEntity<T>>> Count<T>(List<RawEntity<T>> items)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<RawEntity<T>>>(items);
        }
    }

    private sealed class FakeRawPayloadRepository : IClickUpRawPayloadRepository
    {
        public List<(string EntityType, string ExternalId)> Saved { get; } = [];
        public DateTime? PurgedBefore { get; private set; }
        public Exception? ThrowOnPurge { get; set; }

        public Task SaveAsync(string entityType, string externalId, string workspaceId, string payloadJson, DateTime fetchedAtUtc, Guid tenantId)
        {
            Saved.Add((entityType, externalId));
            return Task.CompletedTask;
        }

        public Task<int> DeleteOlderThanAsync(DateTime cutoffUtc)
        {
            if (ThrowOnPurge is not null)
            {
                return Task.FromException<int>(ThrowOnPurge);
            }

            PurgedBefore = cutoffUtc;
            return Task.FromResult(0);
        }
    }

    private sealed class FakeMappingService : IClickUpMappingService
    {
        public int WorkItemsMapped { get; private set; }

        public Task<Programme> MapProgrammeAsync(ClickUpSpaceDto space, Guid tenantId) =>
            Task.FromResult(new Programme { ProgrammeKey = Guid.NewGuid(), TenantId = tenantId, Name = space.Name, ExternalSource = "ClickUp", ExternalId = space.Id, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime });

        public Task<Project> MapProjectAsync(ClickUpFolderDto folder, Guid programmeKey, Guid tenantId) =>
            Task.FromResult(new Project { ProjectKey = Guid.NewGuid(), TenantId = tenantId, ProgrammeKey = programmeKey, Name = folder.Name, ExternalSource = "ClickUp", ExternalId = folder.Id, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime });

        public Task<Workstream> MapWorkstreamAsync(ClickUpListDto list, Guid projectKey, Guid tenantId) =>
            Task.FromResult(new Workstream { WorkstreamKey = Guid.NewGuid(), TenantId = tenantId, ProjectKey = projectKey, Name = list.Name, ExternalSource = "ClickUp", ExternalId = list.Id, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime });

        public Task<WorkItem> MapWorkItemAsync(ClickUpTaskDto task, Guid workstreamKey, Guid tenantId)
        {
            WorkItemsMapped++;
            return Task.FromResult(new WorkItem { WorkItemKey = Guid.NewGuid(), TenantId = tenantId, WorkstreamKey = workstreamKey, Title = task.Name, Stage = WorkItemLifecycleStage.Backlog, ExternalSource = "ClickUp", ExternalId = task.Id, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime });
        }

        public Task<TimeEntry> MapTimeEntryAsync(ClickUpTimeEntryDto entry, Guid tenantId) =>
            Task.FromResult(new TimeEntry { TimeEntryKey = Guid.NewGuid(), TenantId = tenantId, DurationHours = 1m, ExternalSource = "ClickUp", ExternalId = entry.Id, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime });
    }

    private sealed class FakeAuditLogRepository : IAuditLogRepository
    {
        public List<(string EntityType, string EntityId, string Action, string? Detail)> Entries { get; } = [];

        public Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
        {
            Entries.Add((entityType, entityId, action, detailJson));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditLog>> GetRecentAsync(int take, Guid tenantId) => throw new NotSupportedException();

    }

    private sealed class FakeSourceConnectionRepository : ISourceConnectionRepository
    {
        public List<SourceConnection> Connections { get; } = [];

        public Task<SourceConnection?> GetActiveForTenantAsync(Guid tenantId, string source) =>
            Task.FromResult(Connections.FirstOrDefault(c => c.TenantId == tenantId && c.Source == source && c.IsActive));

        public Task<SourceConnection> GetOrCreateActiveAsync(Guid tenantId, string source, string? externalAccountId, DateTime nowUtc)
        {
            var existing = Connections.FirstOrDefault(c => c.TenantId == tenantId && c.Source == source && c.IsActive);
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            var created = new SourceConnection
            {
                ConnectionKey = Guid.NewGuid(),
                TenantId = tenantId,
                Source = source,
                DisplayName = source,
                ExternalAccountId = externalAccountId,
                IsActive = true,
                CreatedAtUtc = nowUtc
            };
            Connections.Add(created);
            return Task.FromResult(created);
        }

        public Task<SourceConnection> SetCredentialAsync(Guid tenantId, string source, string? protectedCredentialJson, DateTime nowUtc)
        {
            var existing = Connections.FirstOrDefault(c => c.TenantId == tenantId && c.Source == source && c.IsActive);
            existing ??= new SourceConnection
            {
                ConnectionKey = Guid.NewGuid(), TenantId = tenantId, Source = source, DisplayName = source,
                IsActive = true, CreatedAtUtc = nowUtc
            };

            var updated = existing with { ProtectedCredentialJson = protectedCredentialJson };
            Connections.Remove(existing);
            Connections.Add(updated);
            return Task.FromResult(updated);
        }
    }

    private sealed class FakeSourceCredentialProtector : ISourceCredentialProtector
    {
        public string Protect(SourceCredential credential) => System.Text.Json.JsonSerializer.Serialize(credential);

        public SourceCredential? Unprotect(string? protectedCredentialJson) =>
            protectedCredentialJson is null ? null : System.Text.Json.JsonSerializer.Deserialize<SourceCredential>(protectedCredentialJson);
    }

    private static FakeClickUpApiClient WorkspaceWithOneTask()
    {
        var api = new FakeClickUpApiClient();
        api.Spaces.Add(new RawEntity<ClickUpSpaceDto>(new ClickUpSpaceDto { Id = "space-1", Name = "Drama" }, "{}"));
        api.FoldersBySpace["space-1"] = [new RawEntity<ClickUpFolderDto>(new ClickUpFolderDto { Id = "folder-1", Name = "Series 1" }, "{}")];
        api.ListsByFolder["folder-1"] = [new RawEntity<ClickUpListDto>(new ClickUpListDto { Id = "list-1", Name = "Edit" }, "{}")];
        api.TasksByList["list-1"] = [new RawEntity<ClickUpTaskDto>(new ClickUpTaskDto { Id = "task-1", Name = "Rough cut" }, "{}")];
        return api;
    }

    private static ClickUpSyncService BuildSut(
        FakeClickUpApiClient api,
        FakeRawPayloadRepository raw,
        FakeMappingService mapping,
        FakeAuditLogRepository audit,
        SyncRunGuard? guard = null,
        int retentionDays = 90,
        string workspaceId = "ws-1",
        string apiToken = "test-token",
        FakeSyncRunRepository? runs = null,
        FakeIdentityResolutionRepository? identity = null,
        TimeProvider? time = null,
        FakeSourceConnectionRepository? connections = null,
        ISourceCredentialProtector? protector = null,
        string environment = "Development")
    {
        time ??= new FixedTimeProvider(Now);
        var programmeOptions = Options.Create(new ProgrammeOpsOptions { RawPayloadRetentionDays = retentionDays, AllowSharedSourceCredentials = true });
        runs ??= new FakeSyncRunRepository();
        identity ??= new FakeIdentityResolutionRepository();
        return new ClickUpSyncService(api, raw, mapping,
            new StaffIdentityResolver(identity, new FakeStaffRepository(), time),
            identity, audit, runs,
            connections ?? new FakeSourceConnectionRepository(),
            protector ?? new FakeSourceCredentialProtector(),
            Options.Create(new ClickUpOptions { WorkspaceId = workspaceId, ApiToken = apiToken }),
            programmeOptions,
            new SyncRunCoordinator(guard ?? new SyncRunGuard(), runs, programmeOptions, time),
            time, new TestHostEnvironment { EnvironmentName = environment });
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Shared_credentials_fail_closed_at_runtime_outside_development(string environment)
    {
        var api = WorkspaceWithOneTask();
        var sut = BuildSut(api, new FakeRawPayloadRepository(), new FakeMappingService(), new FakeAuditLogRepository(), environment: environment);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAsync(TestTenantId, null));
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public async Task Successful_run_audits_SyncCompleted_and_purges_bronze_rows_older_than_retention()
    {
        var api = WorkspaceWithOneTask();
        var raw = new FakeRawPayloadRepository();
        var mapping = new FakeMappingService();
        var audit = new FakeAuditLogRepository();

        var result = await BuildSut(api, raw, mapping, audit, retentionDays: 30).RunAsync(TestTenantId, triggeredByMemberId: 5);

        Assert.Equal(1, result.Programmes);
        Assert.Equal(1, result.Projects);
        Assert.Equal(1, result.Workstreams);
        Assert.Equal(1, result.WorkItems);
        Assert.Equal(1, mapping.WorkItemsMapped);
        Assert.Contains(raw.Saved, s => s == ("task", "task-1"));

        var entry = Assert.Single(audit.Entries);
        Assert.Equal((ClickUpSyncService.AuditEntityType, "ws-1", "SyncCompleted"), (entry.EntityType, entry.EntityId, entry.Action));
        Assert.Equal(Now.UtcDateTime.AddDays(-30), raw.PurgedBefore);
    }

    [Fact]
    public async Task Successful_run_records_a_succeeded_sync_run_and_purges_stale_unresolved_identities_and_old_runs()
    {
        var runs = new FakeSyncRunRepository();
        var identity = new FakeIdentityResolutionRepository();

        await BuildSut(WorkspaceWithOneTask(), new FakeRawPayloadRepository(), new FakeMappingService(), new FakeAuditLogRepository(), retentionDays: 30, runs: runs, identity: identity).RunAsync(TestTenantId, 9);

        var run = runs.Single(ClickUpSyncService.SourceName);
        Assert.Equal(SyncRunStatus.Succeeded, run.Status);
        Assert.Equal(9, run.TriggeredByMemberId);
        Assert.Contains("1 work items", run.Summary);
        Assert.Null(runs.Leases[(TestTenantId, ClickUpSyncService.SourceName)].Owner);
        Assert.Equal(Now.UtcDateTime.AddDays(-30), identity.LastPurgeCutoff);
        Assert.Equal(Now.UtcDateTime.AddDays(-180), runs.LastPurgeCutoff);
    }

    [Fact]
    public async Task A_run_is_refused_while_another_instance_holds_the_database_lease()
    {
        var runs = new FakeSyncRunRepository();
        runs.Leases[(TestTenantId, ClickUpSyncService.SourceName)] = ("other-instance", Now.UtcDateTime.AddMinutes(4), Guid.NewGuid());
        var api = WorkspaceWithOneTask();
        var guard = new SyncRunGuard();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildSut(api, new FakeRawPayloadRepository(), new FakeMappingService(), new FakeAuditLogRepository(), guard, runs: runs).RunAsync(TestTenantId, null));

        Assert.Contains("already running", ex.Message);
        Assert.Equal(0, api.Calls);
        Assert.False(guard.IsRunning(TestTenantId, ClickUpSyncService.SourceName));
    }

    [Fact]
    public async Task A_failed_run_records_a_failed_sync_run_with_the_stage_reached()
    {
        var runs = new FakeSyncRunRepository();
        var api = WorkspaceWithOneTask();
        api.ThrowWhenFetchingTasks = new HttpRequestException("503 Service Unavailable");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildSut(api, new FakeRawPayloadRepository(), new FakeMappingService(), new FakeAuditLogRepository(), runs: runs).RunAsync(TestTenantId, null));

        var run = runs.Single(ClickUpSyncService.SourceName);
        Assert.Equal(SyncRunStatus.Failed, run.Status);
        Assert.Equal("syncing space 'Drama'", run.Stage);
        Assert.Contains("503", run.Error);
        Assert.Null(runs.Leases[(TestTenantId, ClickUpSyncService.SourceName)].Owner);
    }

    [Fact]
    public async Task A_worker_that_lost_its_lease_mid_run_writes_nothing_more_and_does_not_report_success()
    {
        var runs = new FakeSyncRunRepository();
        var time = new MutableTimeProvider(Now);
        var api = WorkspaceWithOneTask();
        var mapping = new FakeMappingService();
        var audit = new FakeAuditLogRepository();
        SyncRun? takeover = null;
        api.AfterTasksFetched = async () =>
        {
            // This worker stalls past the (5 min default) lease; another
            // instance takes the source over in the meantime.
            time.Now = Now.AddMinutes(6);
            takeover = await runs.TryAcquireAsync(TestTenantId, ClickUpSyncService.SourceName, "other-instance", null, time.Now.UtcDateTime, TimeSpan.FromMinutes(5));
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildSut(api, new FakeRawPayloadRepository(), mapping, audit, runs: runs, time: time).RunAsync(TestTenantId, null));

        Assert.IsType<SyncLeaseLostException>(ex.InnerException);
        Assert.Contains("stopped while syncing space 'Drama'", ex.Message);
        Assert.DoesNotContain("re-running is safe", ex.Message);
        Assert.Equal(0, mapping.WorkItemsMapped);

        var stalled = runs.Runs.Single(r => r.InstanceId == SyncRunCoordinator.InstanceId);
        Assert.Equal(SyncRunStatus.Failed, stalled.Status);
        Assert.Equal(SyncRunRepository.AbandonedError, stalled.Error);
        Assert.Equal(SyncRunStatus.Running, runs.Runs.Single(r => r.RunKey == takeover!.RunKey).Status);
        Assert.Equal("other-instance", runs.Leases[(TestTenantId, ClickUpSyncService.SourceName)].Owner);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("SyncLeaseLost", entry.Action);
        Assert.Contains("\"Workstreams\":1", entry.Detail);
    }

    [Fact]
    public async Task Retention_of_zero_disables_the_purge()
    {
        var raw = new FakeRawPayloadRepository();

        await BuildSut(WorkspaceWithOneTask(), raw, new FakeMappingService(), new FakeAuditLogRepository(), retentionDays: 0).RunAsync(TestTenantId, null);

        Assert.Null(raw.PurgedBefore);
    }

    [Fact]
    public async Task Failure_mid_run_audits_SyncFailed_with_stage_and_progress_then_throws_a_rerunnable_error()
    {
        var api = WorkspaceWithOneTask();
        api.ThrowWhenFetchingTasks = new HttpRequestException("503 Service Unavailable");
        var raw = new FakeRawPayloadRepository();
        var audit = new FakeAuditLogRepository();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildSut(api, raw, new FakeMappingService(), audit).RunAsync(TestTenantId, null));

        Assert.Contains("ClickUp sync failed while syncing space 'Drama'", ex.Message);
        Assert.Contains("re-running is safe", ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("SyncFailed", entry.Action);
        Assert.Contains("\"error\":\"HttpRequestException\"", entry.Detail);
        Assert.Contains("\"reachedStage\":\"syncing space", entry.Detail);
        Assert.Contains("\"Workstreams\":1", entry.Detail);
        Assert.Null(raw.PurgedBefore);
    }

    [Fact]
    public async Task A_second_run_is_refused_while_one_is_in_progress_and_makes_no_upstream_calls()
    {
        var guard = new SyncRunGuard();
        using var inFlight = guard.TryEnter(TestTenantId, ClickUpSyncService.SourceName);
        var api = WorkspaceWithOneTask();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildSut(api, new FakeRawPayloadRepository(), new FakeMappingService(), new FakeAuditLogRepository(), guard).RunAsync(TestTenantId, null));

        Assert.Contains("already running", ex.Message);
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public async Task The_lease_is_released_after_a_failed_run_so_a_retry_can_start()
    {
        var guard = new SyncRunGuard();
        var api = WorkspaceWithOneTask();
        api.ThrowWhenFetchingTasks = new HttpRequestException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildSut(api, new FakeRawPayloadRepository(), new FakeMappingService(), new FakeAuditLogRepository(), guard).RunAsync(TestTenantId, null));

        Assert.False(guard.IsRunning(TestTenantId, ClickUpSyncService.SourceName));
    }

    [Fact]
    public async Task Missing_workspace_id_fails_before_taking_the_lease()
    {
        var guard = new SyncRunGuard();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildSut(WorkspaceWithOneTask(), new FakeRawPayloadRepository(), new FakeMappingService(), new FakeAuditLogRepository(), guard, workspaceId: "").RunAsync(TestTenantId, null));

        Assert.False(guard.IsRunning(TestTenantId, ClickUpSyncService.SourceName));
    }

    [Fact]
    public async Task A_purge_failure_is_audited_but_does_not_fail_the_sync()
    {
        var raw = new FakeRawPayloadRepository { ThrowOnPurge = new InvalidOperationException("deadlock") };
        var audit = new FakeAuditLogRepository();

        var result = await BuildSut(WorkspaceWithOneTask(), raw, new FakeMappingService(), audit).RunAsync(TestTenantId, null);

        Assert.Equal(1, result.WorkItems);
        Assert.Equal(["SyncCompleted", "RetentionPurgeFailed"], audit.Entries.Select(e => e.Action).ToArray());
    }

    /// <summary>
    /// The Part 2 defect this phase closed: a tenant that configures its own
    /// ClickUp connection (workspace + token, encrypted via
    /// ISourceCredentialProtector) must have its sync use that account, not
    /// the deployment-wide ClickUp:WorkspaceId/ClickUp:ApiToken — even though
    /// this tenant's own workspace happens to reuse the exact same upstream
    /// space/folder/list/task ids as the deployment default.
    /// </summary>
    [Fact]
    public async Task A_tenant_with_its_own_ClickUp_connection_uses_its_own_workspace_and_token_not_the_deployment_default()
    {
        var api = WorkspaceWithOneTask();
        api.SpacesByWorkspace["ws-tenant-b"] = [new RawEntity<ClickUpSpaceDto>(new ClickUpSpaceDto { Id = "space-1", Name = "Tenant B's own space" }, "{}")];
        var protector = new FakeSourceCredentialProtector();
        var connections = new FakeSourceConnectionRepository();
        connections.Connections.Add(new SourceConnection
        {
            ConnectionKey = Guid.NewGuid(),
            TenantId = TenantB,
            Source = ClickUpSyncService.SourceName,
            DisplayName = "ClickUp",
            ExternalAccountId = "ws-tenant-b",
            IsActive = true,
            CreatedAtUtc = Now.UtcDateTime,
            ProtectedCredentialJson = protector.Protect(new SourceCredential("tenant-b-token", "ws-tenant-b"))
        });

        await BuildSut(api, new FakeRawPayloadRepository(), new FakeMappingService(), new FakeAuditLogRepository(),
            connections: connections, protector: protector, apiToken: "deployment-default-token")
            .RunAsync(TenantB, null);

        Assert.Equal("ws-tenant-b", api.LastWorkspaceIdRequested);
        Assert.Equal("tenant-b-token", api.LastCredentialUsed);
    }

    [Fact]
    public async Task A_tenant_without_its_own_connection_falls_back_to_the_deployment_wide_workspace_and_token()
    {
        var api = WorkspaceWithOneTask();

        await BuildSut(api, new FakeRawPayloadRepository(), new FakeMappingService(), new FakeAuditLogRepository(),
            workspaceId: "ws-1", apiToken: "deployment-default-token")
            .RunAsync(TestTenantId, null);

        Assert.Equal("ws-1", api.LastWorkspaceIdRequested);
        Assert.Equal("deployment-default-token", api.LastCredentialUsed);
    }

    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
}
