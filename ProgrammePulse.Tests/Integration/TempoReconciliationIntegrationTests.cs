using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.Jira;
using ProgrammePulse.Services.Integrations.Tempo;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class TempoReconciliationIntegrationTests(ProgrammePulseWebApplicationFactory factory)
{
    [Fact]
    public async Task Publication_is_replayable_and_deletes_only_the_explicit_tenant_source_and_site_identity()
    {
        if (!HasSql()) return;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var tenant = await ConnectAsync(services);
        var otherTenant = await ConnectAsync(services);
        var repository = services.GetRequiredService<IProgrammeRepository>();
        var externalId = $"{tenant.Cloud:D}:2";
        await SeedAsync(repository, tenant.Id, externalId);
        await SeedAsync(repository, otherTenant.Id, externalId);
        await SeedAsync(repository, tenant.Id, externalId, "ClickUp");
        await SeedAsync(repository, tenant.Id, $"{Guid.NewGuid():D}:2");
        await SeedAsync(repository, tenant.Id, $"{tenant.Cloud:D}:99");
        using var http = Client(request => Task.FromResult(IsAudit(request) ? Page([Deletion(2), Deletion(2)]) : Page([Worklog(1), Worklog(1), Worklog(2)])));
        var source = Source(services, http, tenant.Id);

        await source.RunAsync(tenant.Id, null);
        var first = await repository.GetTimeEntriesAsync(tenant.Id);
        var imported = Assert.Single(first, row => row.ExternalSource == "Tempo" && row.ExternalId == $"{tenant.Cloud:D}:1");
        Assert.Equal(2m, imported.DurationHours);
        Assert.Equal(new DateOnly(2026, 9, 23), imported.WorkDate);
        Assert.Null(imported.StartedAtUtc);
        Assert.Null(imported.StaffKey);
        Assert.False(imported.BillabilityKnown);
        Assert.DoesNotContain(first, row => row.ExternalSource == "Tempo" && row.ExternalId == externalId);
        Assert.Equal(4, first.Count);
        Assert.Single(await repository.GetTimeEntriesAsync(otherTenant.Id));
        var runs = services.GetRequiredService<ISyncRunRepository>();
        using var published = JsonDocument.Parse((await runs.GetLatestSuccessfulAsync(tenant.Id, "Tempo"))!.SummaryJson!);
        Assert.Equal(1, published.RootElement.GetProperty("removedWorklogs").GetInt32());
        Assert.Equal(1, published.RootElement.GetProperty("suppressedWorklogs").GetInt32());
        Assert.False(published.RootElement.GetProperty("completeHistoricalCoverage").GetBoolean());

        await source.RunAsync(tenant.Id, null);
        var replay = await repository.GetTimeEntriesAsync(tenant.Id);
        Assert.Equal(4, replay.Count);
        Assert.Equal(imported.TimeEntryKey, Assert.Single(replay, row => row.ExternalId == imported.ExternalId).TimeEntryKey);
        using var repeated = JsonDocument.Parse((await runs.GetLatestSuccessfulAsync(tenant.Id, "Tempo"))!.SummaryJson!);
        Assert.Equal(0, repeated.RootElement.GetProperty("removedWorklogs").GetInt32());
    }

    [Fact]
    public async Task Disabled_audit_never_requests_deletions_or_infers_them_from_missing_worklogs()
    {
        if (!HasSql()) return;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var tenant = await ConnectAsync(services);
        var repository = services.GetRequiredService<IProgrammeRepository>();
        await SeedAsync(repository, tenant.Id, $"{tenant.Cloud:D}:2");
        using var http = Client(request => { Assert.False(IsAudit(request)); return Task.FromResult(Page([])); });
        await Source(services, http, tenant.Id, enabled: false).RunAsync(tenant.Id, null);
        Assert.Single(await repository.GetTimeEntriesAsync(tenant.Id));
        var run = await services.GetRequiredService<ISyncRunRepository>().GetLatestSuccessfulAsync(tenant.Id, "Tempo");
        using var detail = JsonDocument.Parse(run!.SummaryJson!);
        Assert.Equal("unavailable", detail.RootElement.GetProperty("deletionCoverage").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_later_audit_page_leaves_the_previous_publication_intact(bool malformed)
    {
        if (!HasSql()) return;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var tenant = await ConnectAsync(services);
        using var goodHttp = Client(_ => Task.FromResult(Page([])));
        await Source(services, goodHttp, tenant.Id).RunAsync(tenant.Id, null);
        var runs = services.GetRequiredService<ISyncRunRepository>();
        var previous = await runs.GetLatestSuccessfulAsync(tenant.Id, "Tempo");
        var repository = services.GetRequiredService<IProgrammeRepository>();
        await SeedAsync(repository, tenant.Id, $"{tenant.Cloud:D}:2");
        var auditPages = 0;
        using var http = Client(request => Task.FromResult(!IsAudit(request) ? Page([Worklog(1)])
            : ++auditPages == 1 ? Page([Deletion(2)], new { lastEvaluatedKey = "next" })
            : malformed ? Json("{\"results\":[],\"metadata\":null}") : new HttpResponseMessage(HttpStatusCode.Forbidden)));

        await Assert.ThrowsAnyAsync<Exception>(() => Source(services, http, tenant.Id).RunAsync(tenant.Id, null));
        Assert.Equal(2, auditPages);
        Assert.Equal($"{tenant.Cloud:D}:2", Assert.Single(await repository.GetTimeEntriesAsync(tenant.Id)).ExternalId);
        Assert.Equal(previous!.RunKey, (await runs.GetLatestSuccessfulAsync(tenant.Id, "Tempo"))!.RunKey);
        Assert.Equal(SyncRunStatus.Failed, (await runs.GetLatestAsync(tenant.Id, "Tempo"))!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_or_cancellation_after_writes_rolls_back_deletions_upserts_and_success_audit(bool cancel)
    {
        if (!HasSql()) return;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var tenant = await ConnectAsync(services);
        var repository = services.GetRequiredService<IProgrammeRepository>();
        await SeedAsync(repository, tenant.Id, $"{tenant.Cloud:D}:2");
        using var cancellation = new CancellationTokenSource();
        var audit = new PublicationAudit(services.GetRequiredService<IAuditLogRepository>(), () =>
        {
            if (cancel) cancellation.Cancel();
            else throw new InvalidOperationException("Injected publication failure");
        });
        using var http = Client(request => Task.FromResult(IsAudit(request) ? Page([Deletion(2)]) : Page([Worklog(1)])));
        await Assert.ThrowsAnyAsync<Exception>(() => Source(services, http, tenant.Id, audit: audit).RunAsync(tenant.Id, null, cancellation.Token));
        Assert.Equal($"{tenant.Cloud:D}:2", Assert.Single(await repository.GetTimeEntriesAsync(tenant.Id)).ExternalId);
        var runs = services.GetRequiredService<ISyncRunRepository>();
        Assert.Null(await runs.GetLatestSuccessfulAsync(tenant.Id, "Tempo"));
        Assert.Equal(SyncRunStatus.Failed, (await runs.GetLatestAsync(tenant.Id, "Tempo"))!.Status);
        var events = await audit.GetRecentAsync(20, tenant.Id);
        Assert.DoesNotContain(events, item => item.Action == "SyncCompleted");
        Assert.Contains(events, item => item.Action == "SyncFailed");
    }

    [Theory]
    [InlineData("Jira")]
    [InlineData("Tempo")]
    public async Task A_connection_changed_during_extraction_cannot_publish(string changedSource)
    {
        if (!HasSql()) return;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var tenant = await ConnectAsync(services);
        using var http = Client(async request =>
        {
            if (!IsAudit(request)) return Page([Worklog(1)]);
            await services.GetRequiredService<ISourceConnectionRepository>().SetCredentialAsync(tenant.Id, changedSource, null, DateTime.UtcNow);
            return Page([]);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Source(services, http, tenant.Id).RunAsync(tenant.Id, null));
        Assert.Empty(await services.GetRequiredService<IProgrammeRepository>().GetTimeEntriesAsync(tenant.Id));
        Assert.Null(await services.GetRequiredService<ISyncRunRepository>().GetLatestSuccessfulAsync(tenant.Id, "Tempo"));
    }

    [Fact]
    public async Task Hours_against_an_unsynced_issue_keep_its_id_and_reach_the_jira_tempo_report()
    {
        if (!HasSql()) return;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var tenant = await ConnectAsync(services);
        var quiet = await ConnectAsync(services);
        var repository = services.GetRequiredService<IProgrammeRepository>();
        using var http = Client(request => Task.FromResult(IsAudit(request) ? Page([])
            : Page([new { tempoWorklogId = 1L, timeSpentSeconds = 7200, startDate = "2026-09-23", issue = new { id = 4242L } }, Worklog(2)])));

        await Source(services, http, tenant.Id).RunAsync(tenant.Id, null);

        var entries = await repository.GetTimeEntriesAsync(tenant.Id);
        var againstIssue = Assert.Single(entries, e => e.ExternalId == $"{tenant.Cloud:D}:1");
        Assert.Null(againstIssue.WorkItemKey);
        Assert.Equal($"{tenant.Cloud:D}:4242", againstIssue.SourceWorkItemExternalId);
        Assert.Null(Assert.Single(entries, e => e.ExternalId == $"{tenant.Cloud:D}:2").SourceWorkItemExternalId);

        // Jira is connected but has synced nothing, so the report follows the
        // data: Tempo only, with the hours named against their issue.
        var reports = services.GetRequiredService<IJiraTempoReconciliationQueryService>();
        var report = await reports.BuildAsync(tenant.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.NotNull(report);
        Assert.Equal(ReconciliationShape.TempoOnly, report.Shape);
        Assert.Equal((4m, 2m, 2m), (report.Hours!.Total, report.Hours.IssueNotSynced, report.Hours.NoIssue));
        Assert.Equal("4242", Assert.Single(report.UnlinkedIssues).IssueId);

        // A tenant with connections but no data has no report and no tab.
        Assert.False(await reports.IsAvailableAsync(quiet.Id));
        Assert.Null(await reports.BuildAsync(quiet.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public async Task An_identity_only_jira_connection_imports_nothing_and_still_pairs_tempo()
    {
        if (!HasSql()) return;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var tenant = await ConnectAsync(services);
        var protector = services.GetRequiredService<ISourceCredentialProtector>();
        await services.GetRequiredService<ISourceConnectionRepository>().SetCredentialAsync(tenant.Id, "Jira",
            protector.Protect(new SourceCredential("fixture-token", tenant.Cloud.ToString("D"), "fixture-refresh",
                DateTimeOffset.UtcNow.AddHours(1), SiteUrl: "https://fixture.atlassian.net", ProjectIds: "")), DateTime.UtcNow);
        var jiraCalls = 0;
        using var jiraHttp = new HttpClient(new Handler(_ =>
        {
            jiraCalls++;
            throw new InvalidOperationException("An identity-only connection must not fetch issues.");
        })) { BaseAddress = new("https://api.atlassian.com/") };
        var jira = ActivatorUtilities.CreateInstance<JiraSyncSource>(services, new JiraApiClient(jiraHttp),
            Options.Create(new ProgrammeOpsOptions { RawPayloadRetentionDays = 30 }));

        var outcome = await jira.RunAsync(tenant.Id, null);

        Assert.Equal(0, jiraCalls);
        Assert.Contains("identity-only", outcome.Summary);
        Assert.Equal(SyncRunStatus.Succeeded, (await services.GetRequiredService<ISyncRunRepository>().GetLatestAsync(tenant.Id, "Jira"))!.Status);
        Assert.Empty(await services.GetRequiredService<IProgrammeRepository>().GetWorkItemsAsync(tenant.Id));

        // Tempo pairs with the identity-only site, and the report says why nothing links.
        using var tempoHttp = Client(request => Task.FromResult(IsAudit(request) ? Page([]) : Page([Worklog(1)])));
        await Source(services, tempoHttp, tenant.Id).RunAsync(tenant.Id, null);
        var report = await services.GetRequiredService<IJiraTempoReconciliationQueryService>()
            .BuildAsync(tenant.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Equal(ReconciliationShape.TempoOnly, report!.Shape);
        Assert.StartsWith("Jira is connected for identity only", report.Notes[0]);
    }

    [Fact]
    public async Task Retention_failure_does_not_reclassify_a_committed_publication_as_failed()
    {
        if (!HasSql()) return;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var tenant = await ConnectAsync(services);
        var raw = new FailingRetention(services.GetRequiredService<IRawConnectorPayloadRepository>());
        using var http = Client(request => Task.FromResult(IsAudit(request) ? Page([]) : Page([Worklog(1)])));
        await Source(services, http, tenant.Id, raw: raw).RunAsync(tenant.Id, null);
        Assert.True(raw.RetentionAttempted);
        Assert.Single(await services.GetRequiredService<IProgrammeRepository>().GetTimeEntriesAsync(tenant.Id));
        Assert.Equal(SyncRunStatus.Succeeded, (await services.GetRequiredService<ISyncRunRepository>().GetLatestAsync(tenant.Id, "Tempo"))!.Status);
    }

    private static bool HasSql() => ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(
        "Tempo atomic publication, cancellation, replay and tenant/source/site isolation were not verified.");

    private static async Task<(Guid Id, Guid Cloud)> ConnectAsync(IServiceProvider services)
    {
        var tenant = Guid.NewGuid();
        var cloud = Guid.NewGuid();
        await services.GetRequiredService<ITenantRepository>().CreateAsync(new Tenant
        {
            TenantKey = tenant, Name = "Tempo SQL fixture", ShortCode = tenant.ToString("N")[..12],
            IsActive = true, CreatedAtUtc = DateTime.UtcNow
        });
        var credentials = services.GetRequiredService<ISourceCredentialProtector>();
        var connections = services.GetRequiredService<ISourceConnectionRepository>();
        var tempo = new SourceCredential("fixture-token", RefreshToken: "fixture-refresh",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1), SiteUrl: "https://fixture.atlassian.net");
        await connections.SetCredentialAsync(tenant, "Tempo", credentials.Protect(tempo), DateTime.UtcNow);
        await connections.SetCredentialAsync(tenant, "Jira", credentials.Protect(tempo with { WorkspaceId = cloud.ToString("D") }), DateTime.UtcNow);
        return (tenant, cloud);
    }

    private static Task<TimeEntry> SeedAsync(IProgrammeRepository repository, Guid tenant, string externalId, string source = "Tempo") =>
        repository.UpsertTimeEntryAsync(new TimeEntry
        {
            TimeEntryKey = Guid.NewGuid(), ExternalSource = source, ExternalId = externalId, DurationHours = 1,
            WorkDate = new DateOnly(2026, 9, 22), CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        }, tenant);

    private static TempoSyncSource Source(IServiceProvider services, HttpClient http, Guid tenant, bool enabled = true,
        IAuditLogRepository? audit = null, IRawConnectorPayloadRepository? raw = null) =>
        ActivatorUtilities.CreateInstance<TempoSyncSource>(services,
            new TempoApiClient(http), Options.Create(new TempoReconciliationOptions { EnabledTenantIds = enabled ? [tenant] : [] }),
            Options.Create(new ProgrammeOpsOptions { RawPayloadRetentionDays = 30 }),
            audit ?? services.GetRequiredService<IAuditLogRepository>(), raw ?? services.GetRequiredService<IRawConnectorPayloadRepository>());

    private static object Worklog(long id) => new { tempoWorklogId = id, timeSpentSeconds = 7200, startDate = "2026-09-23" };
    private static object Deletion(long id) => new { tempoWorklogId = id, deletedAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O") };
    private static bool IsAudit(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.StartsWith("/audit/", StringComparison.Ordinal);
    private static HttpResponseMessage Page(object[] results, object? metadata = null) => Json(JsonSerializer.Serialize(new { results, metadata = metadata ?? new { } }));
    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => new(new Handler(send)) { BaseAddress = new("https://api.tempo.io/") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class PublicationAudit(IAuditLogRepository inner, Action afterSuccessAudit) : IAuditLogRepository
    {
        public async Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
        {
            await inner.LogAsync(entityType, entityId, action, actorMemberId, detailJson, timestampUtc, tenantId);
            if (action == "SyncCompleted") afterSuccessAudit();
        }
        public Task<IReadOnlyList<AuditLog>> GetRecentAsync(int take, Guid tenantId) => inner.GetRecentAsync(take, tenantId);
    }

    private sealed class FailingRetention(IRawConnectorPayloadRepository inner) : IRawConnectorPayloadRepository
    {
        public bool RetentionAttempted { get; private set; }
        public Task SaveAsync(Guid tenantId, string source, string sourceAccountId, string entityType, string externalId, string payloadJson, DateTime fetchedAtUtc) =>
            inner.SaveAsync(tenantId, source, sourceAccountId, entityType, externalId, payloadJson, fetchedAtUtc);
        public Task<int> DeleteOlderThanAsync(DateTime cutoffUtc)
        {
            RetentionAttempted = true;
            throw new InvalidOperationException("Injected retention failure");
        }
    }
}
