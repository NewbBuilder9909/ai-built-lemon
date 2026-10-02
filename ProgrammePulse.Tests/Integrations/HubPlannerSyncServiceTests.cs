using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Tests.ProgrammeOps;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Integrations.HubPlanner.Raw;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Integrations.ClickUp;
using ProgrammePulse.Services.Integrations.HubPlanner;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.Integrations;

/// <summary>
/// End-to-end through the real mapper and identity resolver (only the API
/// client, Bronze store and audit log are faked): bookings land as planned
/// allocations, an unmatched resource is queued rather than dropped, and
/// the run leaves a durable Succeeded/Failed row behind.
/// </summary>
public class HubPlannerSyncServiceTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeHubPlannerApiClient : IHubPlannerApiClient
    {
        public List<RawEntity<HubPlannerProjectDto>> Projects { get; } = [];
        public List<RawEntity<HubPlannerResourceDto>> Resources { get; } = [];
        public List<RawEntity<HubPlannerBookingDto>> Bookings { get; } = [];
        public Exception? ThrowWhenFetchingBookings { get; set; }
        public string? LastCredentialUsed { get; private set; }

        public IHubPlannerApiClient WithCredential(string apiToken) { LastCredentialUsed = apiToken; return this; }

        public Task<IReadOnlyList<RawEntity<HubPlannerProjectDto>>> GetProjectsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RawEntity<HubPlannerProjectDto>>>(Projects);

        public Task<IReadOnlyList<RawEntity<HubPlannerResourceDto>>> GetResourcesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RawEntity<HubPlannerResourceDto>>>(Resources);

        public Task<IReadOnlyList<RawEntity<HubPlannerBookingDto>>> GetBookingsAsync(CancellationToken cancellationToken = default) =>
            ThrowWhenFetchingBookings is not null
                ? Task.FromException<IReadOnlyList<RawEntity<HubPlannerBookingDto>>>(ThrowWhenFetchingBookings)
                : Task.FromResult<IReadOnlyList<RawEntity<HubPlannerBookingDto>>>(Bookings);

        public Task<IReadOnlyList<RawEntity<HubPlannerClientDto>>> GetClientsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RawEntity<HubPlannerClientDto>>>([]);
    }

    private sealed class FakeRawPayloadRepository : IHubPlannerRawPayloadRepository
    {
        public List<(string EntityType, string ExternalId)> Saved { get; } = [];

        public Task SaveAsync(string entityType, string externalId, string payloadJson, DateTime fetchedAtUtc, Guid tenantId)
        {
            Saved.Add((entityType, externalId));
            return Task.CompletedTask;
        }

        public Task<int> DeleteOlderThanAsync(DateTime cutoffUtc) => Task.FromResult(0);
    }

    private sealed class FakeAuditLogRepository : IAuditLogRepository
    {
        public List<(string Action, string? Detail)> Entries { get; } = [];

        public Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
        {
            Entries.Add((action, detailJson));
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

    private sealed class Harness
    {
        public FakeHubPlannerApiClient Api { get; } = new();
        public FakeProgrammeRepository Programme { get; } = new();
        public FakeStaffRepository Staff { get; } = new();
        public FakeIdentityResolutionRepository Identity { get; } = new();
        public FakeSyncRunRepository Runs { get; } = new();
        public FakeAuditLogRepository Audit { get; } = new();
        public FakeRawPayloadRepository Raw { get; } = new();
        public FakeSourceConnectionRepository Connections { get; } = new();
        public ISourceCredentialProtector Protector { get; set; } = new FakeSourceCredentialProtector();
        public string ApiKey { get; set; } = "test-key";
        public SyncRunGuard Guard { get; } = new();

        public HubPlannerSyncService Build(string environment = "Development")
        {
            var time = new FixedTimeProvider(Now);
            var options = Options.Create(new ProgrammeOpsOptions { AllowSharedSourceCredentials = true });
            return new HubPlannerSyncService(
                Api, Raw,
                new HubPlannerMappingService(Programme, time),
                new StaffIdentityResolver(Identity, Staff, time),
                Identity, Audit, Runs,
                Connections,
                Protector,
                Options.Create(new HubPlannerOptions { ApiKey = ApiKey }),
                options,
                new SyncRunCoordinator(Guard, Runs, options, time),
                time, new TestHostEnvironment { EnvironmentName = environment });
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Shared_credentials_fail_closed_at_runtime_outside_development(string environment)
    {
        var harness = new Harness();
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Build(environment).RunAsync(TestTenantId, null));
        Assert.Null(harness.Api.LastCredentialUsed);
    }

    private static Harness OneProjectTwoBookingsOneKnownResource()
    {
        var h = new Harness();
        var jamie = Guid.NewGuid();
        h.Staff.Staff.Add(new StaffProfile
        {
            StaffKey = jamie, TenantId = TestTenantId, MemberId = 1, FullName = "Jamie Gardner", Email = "jamie@example.com",
            CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime
        });
        // "Known" means an Admin approved the link: an email match alone attributes nobody.
        h.Identity.Links.Add(new ExternalIdentityLink
        {
            LinkKey = Guid.NewGuid(), TenantId = TestTenantId, ExternalSource = "HubPlanner", ExternalUserId = "r-known", StaffKey = jamie, CreatedAtUtc = Now.UtcDateTime
        });
        h.Api.Projects.Add(new RawEntity<HubPlannerProjectDto>(new HubPlannerProjectDto { Id = "p1", Name = "Drama" }, "{}"));
        h.Api.Resources.Add(new RawEntity<HubPlannerResourceDto>(new HubPlannerResourceDto { Id = "r-known", Email = "JAMIE@example.com", FirstName = "Jamie", LastName = "Gardner" }, "{}"));
        h.Api.Resources.Add(new RawEntity<HubPlannerResourceDto>(new HubPlannerResourceDto { Id = "r-unknown", Email = "freelancer@agency.example", FirstName = "Sam", LastName = "Freelance" }, "{}"));
        h.Api.Bookings.Add(new RawEntity<HubPlannerBookingDto>(new HubPlannerBookingDto { Id = "b1", Project = "p1", Resource = "r-known", Start = "2026-01-01", End = "2026-01-02", State = "STATE_HOURS", StateValue = 8 }, "{}"));
        h.Api.Bookings.Add(new RawEntity<HubPlannerBookingDto>(new HubPlannerBookingDto { Id = "b2", Project = "p1", Resource = "r-unknown", Start = "2026-01-01", End = "2026-01-02" }, "{}"));
        h.Api.Bookings.Add(new RawEntity<HubPlannerBookingDto>(new HubPlannerBookingDto { Id = "b3", Project = "p-archived", Resource = "r-known" }, "{}"));
        return h;
    }

    [Fact]
    public async Task Bookings_become_planned_allocations_and_no_work_items_or_workstreams_are_created()
    {
        var h = OneProjectTwoBookingsOneKnownResource();

        var result = await h.Build().RunAsync(TestTenantId, triggeredByMemberId: 5);

        Assert.Equal(1, result.Projects);
        Assert.Equal(2, result.PlannedAllocations);
        Assert.Equal(1, result.SkippedBookings);
        Assert.Equal(2, h.Programme.PlannedAllocations.Count);
        Assert.Empty(h.Programme.WorkItems);
        Assert.Empty(h.Programme.Workstreams);
        Assert.Contains(h.Raw.Saved, s => s == ("booking", "b3"));
    }

    [Fact]
    public async Task An_unmatched_resource_is_queued_and_counted_not_silently_dropped()
    {
        var h = OneProjectTwoBookingsOneKnownResource();

        var result = await h.Build().RunAsync(TestTenantId, null);

        Assert.Equal(1, result.UnresolvedResources);
        var queued = Assert.Single(h.Identity.Unresolved);
        Assert.Equal("HubPlanner", queued.ExternalSource);
        Assert.Equal("r-unknown", queued.ExternalUserId);
        Assert.Equal("freelancer@agency.example", queued.Email);
        Assert.Equal("Sam Freelance", queued.DisplayName);
        Assert.Equal("booking resource", queued.Context);

        var known = h.Programme.PlannedAllocations.Single(a => a.ExternalId == "b1");
        var unknown = h.Programme.PlannedAllocations.Single(a => a.ExternalId == "b2");
        Assert.Equal(h.Staff.Staff[0].StaffKey, known.StaffKey);
        Assert.Null(unknown.StaffKey);
        Assert.Equal("r-unknown", unknown.ExternalResourceId);
        Assert.Contains("1 unmatched people", result.Describe());
    }

    [Fact]
    public async Task A_resource_whose_email_matches_staff_is_only_suggested_until_approved()
    {
        var h = OneProjectTwoBookingsOneKnownResource();
        h.Identity.Links.Clear();

        var result = await h.Build().RunAsync(TestTenantId, null);

        Assert.Equal(2, result.UnresolvedResources);
        Assert.Null(h.Programme.PlannedAllocations.Single(a => a.ExternalId == "b1").StaffKey);
        var suggested = h.Identity.Unresolved.Single(u => u.ExternalUserId == "r-known");
        Assert.Equal(h.Staff.Staff[0].StaffKey, suggested.SuggestedStaffKey);
        Assert.Null(h.Identity.Unresolved.Single(u => u.ExternalUserId == "r-unknown").SuggestedStaffKey);
    }

    [Fact]
    public async Task A_resource_whose_email_matches_two_active_staff_is_queued_as_ambiguous_not_assigned_to_the_first()
    {
        var h = OneProjectTwoBookingsOneKnownResource();
        h.Identity.Links.Clear();
        h.Staff.Staff.Add(new StaffProfile
        {
            StaffKey = Guid.NewGuid(), TenantId = TestTenantId, MemberId = 2, FullName = "Jamie Gardner (duplicate)", Email = "jamie@example.com",
            CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime
        });

        var result = await h.Build().RunAsync(TestTenantId, null);

        Assert.Equal(2, result.UnresolvedResources);
        Assert.Equal(1, result.AmbiguousResources);
        Assert.Contains("2 unmatched people (1 ambiguous)", result.Describe());
        Assert.Null(h.Programme.PlannedAllocations.Single(a => a.ExternalId == "b1").StaffKey);
        var ambiguous = h.Identity.Unresolved.Single(u => u.ExternalUserId == "r-known");
        Assert.Contains("ambiguous: 2 staff profiles share this email", ambiguous.Context);
    }

    [Fact]
    public async Task An_explicit_identity_link_resolves_a_resource_the_roster_does_not_know()
    {
        var h = OneProjectTwoBookingsOneKnownResource();
        var linkedStaffKey = Guid.NewGuid();
        h.Identity.Links.Add(new ExternalIdentityLink
        {
            LinkKey = Guid.NewGuid(), TenantId = TestTenantId, ExternalSource = "HubPlanner", ExternalUserId = "r-unknown", StaffKey = linkedStaffKey, CreatedAtUtc = Now.UtcDateTime
        });

        var result = await h.Build().RunAsync(TestTenantId, null);

        Assert.Equal(0, result.UnresolvedResources);
        Assert.Empty(h.Identity.Unresolved);
        Assert.Equal(linkedStaffKey, h.Programme.PlannedAllocations.Single(a => a.ExternalId == "b2").StaffKey);
    }

    [Fact]
    public async Task A_successful_run_leaves_a_succeeded_run_row_with_a_summary_and_releases_the_lease()
    {
        var h = OneProjectTwoBookingsOneKnownResource();

        await h.Build().RunAsync(TestTenantId, 7);

        var run = h.Runs.Single("HubPlanner");
        Assert.Equal(SyncRunStatus.Succeeded, run.Status);
        Assert.Equal(7, run.TriggeredByMemberId);
        Assert.Equal(Now.UtcDateTime, run.FinishedAtUtc);
        Assert.Contains("2 bookings as planned allocations", run.Summary);
        Assert.Null(h.Runs.Leases[(TestTenantId, "HubPlanner")].Owner);
        Assert.False(h.Guard.IsRunning(TestTenantId, "HubPlanner"));
        Assert.Contains(h.Audit.Entries, e => e.Action == "SyncCompleted");
    }

    [Fact]
    public async Task A_failed_run_leaves_a_failed_run_row_with_the_stage_and_error_and_releases_the_lease()
    {
        var h = OneProjectTwoBookingsOneKnownResource();
        h.Api.ThrowWhenFetchingBookings = new HttpRequestException("429 Too Many Requests");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Build().RunAsync(TestTenantId, null));

        Assert.Contains("failed while syncing bookings", ex.Message);
        var run = h.Runs.Single("HubPlanner");
        Assert.Equal(SyncRunStatus.Failed, run.Status);
        Assert.Equal("syncing bookings", run.Stage);
        Assert.Contains("429", run.Error);
        Assert.Null(h.Runs.Leases[(TestTenantId, "HubPlanner")].Owner);
        Assert.False(h.Guard.IsRunning(TestTenantId, "HubPlanner"));
        Assert.Contains(h.Audit.Entries, e => e.Action == "SyncFailed");
    }

    [Fact]
    public async Task A_run_is_refused_while_another_instance_holds_the_database_lease()
    {
        var h = OneProjectTwoBookingsOneKnownResource();
        h.Runs.Leases[(TestTenantId, "HubPlanner")] = ("other-instance", Now.UtcDateTime.AddMinutes(4), Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Build().RunAsync(TestTenantId, null));

        Assert.Contains("already running", ex.Message);
        Assert.Empty(h.Programme.PlannedAllocations);
        Assert.False(h.Guard.IsRunning(TestTenantId, "HubPlanner"));
    }

    /// <summary>
    /// The Part 2 defect this phase closed, mirrored from
    /// ClickUpSyncServiceTests: a tenant with its own Hub Planner connection
    /// (an API key, encrypted via ISourceCredentialProtector) must use that
    /// key, not the deployment-wide HubPlanner:ApiKey.
    /// </summary>
    [Fact]
    public async Task A_tenant_with_its_own_HubPlanner_connection_uses_its_own_key_not_the_deployment_default()
    {
        var h = OneProjectTwoBookingsOneKnownResource();
        h.ApiKey = "deployment-default-key";
        var protector = new FakeSourceCredentialProtector();
        h.Connections.Connections.Add(new SourceConnection
        {
            ConnectionKey = Guid.NewGuid(),
            TenantId = TestTenantId,
            Source = "HubPlanner",
            DisplayName = "Hub Planner",
            IsActive = true,
            CreatedAtUtc = Now.UtcDateTime,
            ProtectedCredentialJson = protector.Protect(new SourceCredential("tenant-own-key"))
        });
        h.Protector = protector;

        await h.Build().RunAsync(TestTenantId, null);

        Assert.Equal("tenant-own-key", h.Api.LastCredentialUsed);
    }

    [Fact]
    public async Task A_tenant_without_its_own_connection_falls_back_to_the_deployment_wide_key()
    {
        var h = OneProjectTwoBookingsOneKnownResource();
        h.ApiKey = "deployment-default-key";

        await h.Build().RunAsync(TestTenantId, null);

        Assert.Equal("deployment-default-key", h.Api.LastCredentialUsed);
    }
}
