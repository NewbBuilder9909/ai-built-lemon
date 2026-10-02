using ProgrammePulse.Tests.Controllers;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

public class SyncStatusQueryServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static SyncRun Run(string source, SyncRunStatus status, DateTime startedAtUtc, DateTime? finishedAtUtc = null, string? summary = null, string? error = null, string? stage = null) => new()
    {
        RunKey = Guid.NewGuid(), TenantId = TestTenantId, Source = source, Status = status, StartedAtUtc = startedAtUtc, HeartbeatAtUtc = startedAtUtc,
        FinishedAtUtc = finishedAtUtc, InstanceId = "test", Summary = summary, Error = error, Stage = stage
    };

    /// <summary>
    /// The service takes its source list from ISyncSourceRegistry, so the
    /// fake registry here stands in for what the composer registers.
    /// </summary>
    private static SyncStatusQueryService Build(FakeSyncRunRepository runs, params ISyncSource[] sources) =>
        new(runs,
            new FakeSyncSourceRegistry(sources.Length > 0 ? sources : [FakeSyncSource.ClickUpLike(), FakeSyncSource.HubPlannerLike()]),
            new FixedTimeProvider(Now));

    [Fact]
    public async Task Every_known_source_is_reported_even_when_it_has_never_run()
    {
        var states = await Build(new FakeSyncRunRepository()).GetPublicationStatesAsync(TestTenantId);

        Assert.Equal(["ClickUp", "HubPlanner"], states.Select(s => s.Source).ToArray());
        Assert.All(states, s =>
        {
            Assert.Equal("Never published", s.StatusLabel);
            Assert.Equal("never", s.FreshnessLabel);
            Assert.False(s.IsRunning);
            Assert.False(s.LastRunFailed);
        });
    }

    [Fact]
    public async Task A_failed_latest_run_reports_the_last_complete_publication_and_says_the_data_may_be_mixed()
    {
        var runs = new FakeSyncRunRepository();
        runs.Runs.Add(Run("ClickUp", SyncRunStatus.Succeeded, Now.UtcDateTime.AddHours(-3), Now.UtcDateTime.AddHours(-3).AddMinutes(2), summary: "2 programmes"));
        runs.Runs.Add(Run("ClickUp", SyncRunStatus.Failed, Now.UtcDateTime.AddMinutes(-30), Now.UtcDateTime.AddMinutes(-29), error: "HttpRequestException: 503", stage: "syncing space 'Drama'"));

        var state = (await Build(runs).GetPublicationStatesAsync(TestTenantId)).Single(s => s.Source == "ClickUp");

        Assert.Equal("Failed", state.StatusLabel);
        Assert.True(state.LastRunFailed);
        Assert.Equal("HttpRequestException: 503", state.LastFailureError);
        Assert.Equal("syncing space 'Drama'", state.LastFailureStage);
        Assert.Equal(Now.UtcDateTime.AddHours(-3).AddMinutes(2), state.LastPublishedAtUtc);
        Assert.Equal("2 programmes", state.LastPublishedSummary);
        Assert.Equal("2 h ago", state.FreshnessLabel);

        // The failed run committed whatever it upserted before it stopped;
        // the page must not claim it shows only the last success.
        Assert.True(state.CurrentDataMayBePartial);
        Assert.Contains("may mix the last complete publication (2026-09-16 09:02 UTC)", state.PartialDataNote);
        Assert.Contains("while syncing space 'Drama'", state.PartialDataNote);
    }

    [Fact]
    public async Task A_failure_with_no_prior_success_says_the_data_is_only_what_the_failed_run_wrote()
    {
        var runs = new FakeSyncRunRepository();
        runs.Runs.Add(Run("ClickUp", SyncRunStatus.Failed, Now.UtcDateTime.AddMinutes(-30), Now.UtcDateTime.AddMinutes(-29), error: "boom"));

        var state = (await Build(runs).GetPublicationStatesAsync(TestTenantId)).Single(s => s.Source == "ClickUp");

        Assert.Null(state.LastPublishedAtUtc);
        Assert.True(state.CurrentDataMayBePartial);
        Assert.StartsWith("No complete publication yet", state.PartialDataNote);
    }

    [Fact]
    public async Task A_running_run_reports_its_stage_and_start_and_flags_the_data_as_changing()
    {
        var runs = new FakeSyncRunRepository();
        runs.Runs.Add(Run("HubPlanner", SyncRunStatus.Running, Now.UtcDateTime.AddMinutes(-1), stage: "syncing bookings"));

        var state = (await Build(runs).GetPublicationStatesAsync(TestTenantId)).Single(s => s.Source == "HubPlanner");

        Assert.Equal("Running", state.StatusLabel);
        Assert.Equal("syncing bookings", state.RunningStage);
        Assert.Equal(Now.UtcDateTime.AddMinutes(-1), state.RunningSinceUtc);
        Assert.True(state.CurrentDataMayBePartial);
        Assert.StartsWith("A run is in progress", state.PartialDataNote);
    }

    [Fact]
    public async Task A_succeeded_latest_run_is_not_flagged_as_partial()
    {
        var runs = new FakeSyncRunRepository();
        runs.Runs.Add(Run("ClickUp", SyncRunStatus.Succeeded, Now.UtcDateTime.AddMinutes(-10), Now.UtcDateTime.AddMinutes(-9), summary: "ok"));

        var state = (await Build(runs).GetPublicationStatesAsync(TestTenantId)).Single(s => s.Source == "ClickUp");

        Assert.False(state.CurrentDataMayBePartial);
        Assert.Equal("Published", state.StatusLabel);
    }

    /// <summary>
    /// The registry is the only source list: a source nobody has taught
    /// this service about still appears, as "never published". That is
    /// what replaced the hand-maintained KnownSources array, and it is the
    /// guarantee that a new integration needs no Gold-layer edit.
    /// </summary>
    [Fact]
    public async Task A_source_this_service_has_never_heard_of_is_reported_from_the_registry_alone()
    {
        var jira = new FakeSyncSource("Jira", "Jira", "jira-sync");

        var states = await Build(new FakeSyncRunRepository(), jira).GetPublicationStatesAsync(TestTenantId);

        var state = Assert.Single(states);
        Assert.Equal("Jira", state.Source);
        Assert.Equal("Never published", state.StatusLabel);
    }

    /// <summary>
    /// File import publishes SyncRun rows but is not an ISyncSource. Before it
    /// was reported, the overview told a customer who had just imported their
    /// data that no source had ever published.
    /// </summary>
    [Fact]
    public async Task An_upload_source_is_reported_after_the_sync_sources_from_its_own_runs()
    {
        var runs = new FakeSyncRunRepository();
        runs.Runs.Add(Run("FileImport", SyncRunStatus.Succeeded, Now.UtcDateTime.AddHours(-2), Now.UtcDateTime.AddHours(-2), summary: "36 work items imported"));
        var service = new SyncStatusQueryService(
            runs,
            new FakeSyncSourceRegistry([FakeSyncSource.ClickUpLike()]),
            new FixedTimeProvider(Now),
            [new UploadSource("FileImport", "File import")]);

        var states = await service.GetPublicationStatesAsync(TestTenantId);

        Assert.Equal(["ClickUp", "FileImport"], states.Select(s => s.Source).ToArray());
        var upload = states.Single(s => s.Source == "FileImport");
        Assert.Equal("Published", upload.StatusLabel);
        Assert.Equal("36 work items imported", upload.LastPublishedSummary);
        Assert.True(upload.HasActivity);
        Assert.False(states.Single(s => s.Source == "ClickUp").HasActivity);
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(5, "5 min ago")]
    [InlineData(60, "1 h ago")]
    [InlineData(60 * 26, "1 d ago")]
    public void Freshness_labels_scale_with_age(int minutesAgo, string expected) =>
        Assert.Equal(expected, SyncStatusQueryService.FreshnessLabel(Now.UtcDateTime.AddMinutes(-minutesAgo), Now.UtcDateTime));
}
