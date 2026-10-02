using ProgrammePulse.Services.Shared;
using ProgrammePulse.Tests.Controllers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.Reporting;
using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// The rules that used to live inside StaffReportingController and
/// StaffProgrammeOverviewController, now tested without HTTP. Two behaviour
/// changes came with the move, and both are pinned here:
///
/// - A blank title or name is refused with a message. It used to be
///   silently skipped: the form redirected with nothing created and no
///   explanation.
/// - Alert detection runs after a successful sync, not on a Reporting Hub
///   page view. A detection failure never turns a published sync into a
///   reported failure.
/// </summary>
public class ReportingApplicationServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_risk_or_issue_without_a_title_is_refused_with_a_reason(string? title)
    {
        var repository = new FakeProgrammeRepository();
        var sut = new RaidService(repository, TimeProvider.System);

        var risk = await sut.CreateRiskAsync(Tenant, Guid.NewGuid(), title, null, SeverityLevel.High);
        var issue = await sut.CreateIssueAsync(Tenant, Guid.NewGuid(), title, null, SeverityLevel.High);

        Assert.False(risk.Succeeded);
        Assert.Equal(RaidService.TitleRequired, risk.Error);
        Assert.False(issue.Succeeded);
        Assert.Empty(repository.Risks);
        Assert.Empty(repository.Issues);
    }

    [Fact]
    public async Task A_new_risk_is_trimmed_open_and_scoped_to_the_callers_tenant()
    {
        var repository = new FakeProgrammeRepository();
        var sut = new RaidService(repository, TimeProvider.System);

        var outcome = await sut.CreateRiskAsync(Tenant, Guid.NewGuid(), "  Supplier slips  ", "   ", SeverityLevel.Medium);

        Assert.True(outcome.Succeeded);
        var risk = Assert.Single(repository.Risks);
        Assert.Equal("Supplier slips", risk.Title);
        Assert.Null(risk.Description);
        Assert.Equal(RiskStatus.Open, risk.Status);
        Assert.Equal(Tenant, risk.TenantId);
    }

    [Fact]
    public async Task Changing_the_status_of_another_tenants_risk_does_nothing()
    {
        var repository = new FakeProgrammeRepository();
        var sut = new RaidService(repository, TimeProvider.System);
        await sut.CreateRiskAsync(OtherTenant, Guid.NewGuid(), "Theirs", null, SeverityLevel.Low);
        var theirs = repository.Risks.Single();

        await sut.SetRiskStatusAsync(Tenant, theirs.RiskKey, RiskStatus.Closed);

        Assert.All(repository.Risks, r => Assert.Equal(RiskStatus.Open, r.Status));
    }

    [Fact]
    public async Task A_change_request_without_a_title_and_a_stakeholder_without_a_person_are_refused()
    {
        var repository = new FakeProgrammeRepository();
        var sut = new GovernanceService(repository, new FakeStaffRepository(), TimeProvider.System);

        var change = await sut.ProposeChangeAsync(Tenant, Guid.NewGuid(), " ", null, null);
        var stakeholder = await sut.AddStakeholderAsync(Tenant, Guid.NewGuid(), staffKey: null, externalName: "  ", StakeholderRole.Consulted);

        Assert.Equal(GovernanceService.TitleRequired, change.Error);
        Assert.Equal(GovernanceService.StakeholderRequired, stakeholder.Error);
        Assert.Empty(repository.ChangeRequests);
        Assert.Empty(repository.Stakeholders);
    }

    [Fact]
    public async Task Internal_stakeholders_are_chosen_from_this_tenants_active_staff()
    {
        var staff = new FakeStaffRepository();
        Models.Staff.StaffProfile Person(string name, Guid tenant, bool active = true) => new()
        {
            StaffKey = Guid.NewGuid(), MemberId = staff.Staff.Count + 1, FullName = name, Email = $"{name[..3]}@example.com",
            TenantId = tenant, IsActive = active, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        staff.Staff.Add(Person("Zoe Active", Tenant));
        staff.Staff.Add(Person("Amy Active", Tenant));
        staff.Staff.Add(Person("Lee Leaver", Tenant, active: false));
        staff.Staff.Add(Person("Oth Other", OtherTenant));

        var page = await new GovernanceService(new FakeProgrammeRepository(), staff, TimeProvider.System).BuildAsync(Tenant);

        Assert.Equal(["Amy Active", "Zoe Active"], page.StaffOptions!.Select(o => o.FullName).ToArray());
    }

    [Fact]
    public async Task A_customer_without_a_name_is_refused()
    {
        var repository = new FakeProgrammeRepository();
        var sut = new PortfolioAdminService(repository, TimeProvider.System);

        var outcome = await sut.CreateCustomerAsync(Tenant, "");

        Assert.Equal(PortfolioAdminService.CustomerNameRequired, outcome.Error);
        Assert.Empty(repository.Customers);
    }

    [Fact]
    public async Task A_successful_sync_runs_alert_detection_for_that_tenant()
    {
        var alerts = new RecordingAlertDetection();
        var clickUp = FakeSyncSource.ClickUpLike();
        var sut = SyncService(alerts, clickUp);

        var result = await sut.RunAsync(clickUp, Tenant, triggeredByMemberId: 7);

        Assert.True(result.Succeeded);
        Assert.StartsWith("ClickUp published:", result.Message, StringComparison.Ordinal);
        Assert.Equal([Tenant], alerts.DetectedFor);
    }

    [Fact]
    public async Task A_failed_sync_reports_the_failure_and_does_not_detect()
    {
        var alerts = new RecordingAlertDetection();
        var failing = new ThrowingSource(new InvalidOperationException("ClickUp token rejected"));
        var sut = SyncService(alerts, failing);

        var result = await sut.RunAsync(failing, Tenant, triggeredByMemberId: null);

        Assert.False(result.Succeeded);
        Assert.Equal("ClickUp token rejected", result.Message);
        Assert.Empty(alerts.DetectedFor);
    }

    [Fact]
    public async Task A_detection_failure_after_a_published_sync_is_logged_not_reported_as_a_failed_sync()
    {
        var alerts = new RecordingAlertDetection { Throw = true };
        var logger = new ListLogger();
        var clickUp = FakeSyncSource.ClickUpLike();
        var sut = new ProgrammeSyncService(new FakeSyncSourceRegistry(clickUp), new FakeFeatureGate(ProductFeature.ClickUpSync),
            null!, alerts, TimeProvider.System, logger);

        var result = await sut.RunAsync(clickUp, Tenant, triggeredByMemberId: null);

        Assert.True(result.Succeeded);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Alert detection after ClickUp sync failed"));
    }

    private static ProgrammeSyncService SyncService(IAlertDetectionService alerts, ISyncSource source) =>
        new(new FakeSyncSourceRegistry(source), new FakeFeatureGate(ProductFeature.ClickUpSync),
            null!, alerts, TimeProvider.System, NullLogger<ProgrammeSyncService>.Instance);

    private sealed class RecordingAlertDetection : IAlertDetectionService
    {
        public bool Throw { get; init; }

        public List<Guid> DetectedFor { get; } = [];

        public Task DetectForTenantAsync(Guid tenantId, DateTime nowUtc)
        {
            if (Throw)
            {
                throw new TimeoutException("SQL timeout");
            }

            DetectedFor.Add(tenantId);
            return Task.CompletedTask;
        }

        public Task DetectAndRaiseAsync(IReadOnlyList<ContributorCapacityRowViewModel> contributorCapacity, DateTime nowUtc, Guid tenantId) => Task.CompletedTask;

        public Task<ResultPage<Alert>> GetOpenAlertsPageAsync(Guid tenantId, PageRequest page, string? teamFilter = null, CancellationToken cancellationToken = default) => Task.FromResult(ResultPage<Alert>.Of([], page));
        public Task<int> CountOpenAlertsAsync(Guid tenantId, string? teamFilter = null, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task AcknowledgeAsync(Guid alertKey, Guid? acknowledgedByStaffKey, DateTime nowUtc, Guid tenantId) => Task.CompletedTask;
    }

    private sealed class ThrowingSource(Exception exception) : ISyncSource
    {
        public string Name => "ClickUp";
        public string DisplayName => "ClickUp";
        public string FeatureKey => ProductFeature.ClickUpSync;
        public SourceCapabilities Capabilities => SourceCapabilities.Work;

        public Task<SyncOutcome> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default) =>
            Task.FromException<SyncOutcome>(exception);
    }

    private sealed class ListLogger : ILogger<ProgrammeSyncService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
