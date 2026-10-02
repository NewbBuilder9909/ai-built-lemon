using ProgrammePulse.Tests.ProgrammeOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Integrations.Resilience;

namespace ProgrammePulse.Tests.Integrations;

public class IntegrationRunStatusQueryServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static SyncRun Run(string source, SyncRunStatus status, DateTime startedAtUtc, DateTime? finishedAtUtc = null, string? summary = null, string? error = null, string? stage = null) => new()
    {
        RunKey = Guid.NewGuid(),
        TenantId = TenantId,
        Source = source,
        Status = status,
        StartedAtUtc = startedAtUtc,
        HeartbeatAtUtc = startedAtUtc,
        FinishedAtUtc = finishedAtUtc,
        InstanceId = "test",
        Summary = summary,
        Error = error,
        Stage = stage
    };

    [Fact]
    public async Task Every_requested_source_is_reported_even_when_it_has_never_run()
    {
        var service = new IntegrationRunStatusQueryService(new FakeSyncRunRepository(), new FixedTimeProvider(Now));

        var states = await service.GetStatesAsync(TenantId,
        [
            ("GitHubEvidence", "GitHub evidence"),
            ("FreshdeskSupport", "Freshdesk service health")
        ]);

        Assert.Equal(["GitHubEvidence", "FreshdeskSupport"], states.Select(s => s.Source).ToArray());
        Assert.All(states, state => Assert.Equal("Never published", state.StatusLabel));
    }

    [Fact]
    public async Task Failed_and_running_runs_keep_their_honesty_signals()
    {
        var runs = new FakeSyncRunRepository();
        runs.Runs.Add(Run("GitHubEvidence", SyncRunStatus.Succeeded, Now.UtcDateTime.AddHours(-3), Now.UtcDateTime.AddHours(-3).AddMinutes(1), summary: "ok"));
        runs.Runs.Add(Run("GitHubEvidence", SyncRunStatus.Failed, Now.UtcDateTime.AddMinutes(-20), Now.UtcDateTime.AddMinutes(-19), error: "boom", stage: "syncing acme/web"));
        runs.Runs.Add(Run("FreshdeskSupport", SyncRunStatus.Running, Now.UtcDateTime.AddMinutes(-2), stage: "syncing withdrawn tickets"));

        var service = new IntegrationRunStatusQueryService(runs, new FixedTimeProvider(Now));
        var states = await service.GetStatesAsync(TenantId,
        [
            ("GitHubEvidence", "GitHub evidence"),
            ("FreshdeskSupport", "Freshdesk service health")
        ]);

        var gitHub = states.Single(s => s.Source == "GitHubEvidence");
        Assert.True(gitHub.LastRunFailed);
        Assert.True(gitHub.CurrentDataMayBePartial);
        Assert.Contains("syncing acme/web", gitHub.PartialDataNote);

        var freshdesk = states.Single(s => s.Source == "FreshdeskSupport");
        Assert.True(freshdesk.IsRunning);
        Assert.Equal("syncing withdrawn tickets", freshdesk.RunningStage);
        Assert.True(freshdesk.CurrentDataMayBePartial);
    }
}
