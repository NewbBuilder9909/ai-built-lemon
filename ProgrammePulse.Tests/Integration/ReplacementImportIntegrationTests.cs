using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.FileImport;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Tests.Personas;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// A replacement import over real SQL Server and real HTTP: the second cycle
/// of a diagnostic, where a corrected export must replace the first, not sit
/// beside it. Proves the staged file round-trips through its table, the
/// removals apply in one transaction, removed work items keep their time
/// (unlinked), and another tenant's identical ids are never touched.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed partial class ReplacementImportIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "a replacement import was never previewed, confirmed and reconciled against SQL Server.";

    private const string WorkItems = "Project,WorkItemId,Title,Status\nRollout,R-1,Keep me,In progress\nRollout,R-2,Deleted at source,In progress\n";
    private const string Time = "WorkItemId,Date,Hours\nR-1,2026-09-01,3\nR-2,2026-09-02,5\n";

    [Fact]
    public async Task A_second_cycle_replaces_the_first_and_touches_no_other_tenant()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        var tenant = await CreateTenantAsync();
        var other = await CreateTenantAsync();
        foreach (var t in new[] { tenant, other })
        {
            Assert.True((await ImportAsync(t, DeliveryExportKind.WorkItems, WorkItems)).Imported);
            Assert.True((await ImportAsync(t, DeliveryExportKind.TimeEntries, Time)).Imported);
        }

        // Cycle two: R-2 deleted at source, and R-1's hours corrected from 3 to 4.
        var items = await ImportAsync(tenant, DeliveryExportKind.WorkItems, "Project,WorkItemId,Title,Status\nRollout,R-1,Keep me,In progress\n");
        Assert.True(items.AwaitingConfirmation);
        Assert.Equal(5m, items.Plan!.HoursUnlinkedByRemoval);
        using (var scope = factory.Services.CreateScope())
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IImportStagingRepository>().GetAsync(tenant, items.StagingKey!.Value));
        Assert.True((await ConfirmAsync(tenant, items.StagingKey!.Value)).Imported);

        var time = await ImportAsync(tenant, DeliveryExportKind.TimeEntries, "WorkItemId,Date,Hours\nR-1,2026-09-01,4\n");
        Assert.True(time.AwaitingConfirmation);
        Assert.Equal((8m, 4m), (time.Plan!.HoursBefore, time.Plan.HoursAfter));
        Assert.True((await ConfirmAsync(tenant, time.StagingKey!.Value)).Imported);

        using var check = factory.Services.CreateScope();
        var repository = check.ServiceProvider.GetRequiredService<IProgrammeRepository>();
        Assert.Equal(["R-1"], (await repository.GetWorkItemsAsync(tenant)).Select(i => i.ExternalId));
        var entries = await repository.GetTimeEntriesAsync(tenant);
        Assert.Equal(4m, Assert.Single(entries).DurationHours);
        Assert.NotNull(entries[0].WorkItemKey);
        Assert.Null(await check.ServiceProvider.GetRequiredService<IImportStagingRepository>().GetAsync(tenant, time.StagingKey.Value));

        // The other tenant, with identical ids, is exactly as it was.
        Assert.Equal(2, (await repository.GetWorkItemsAsync(other)).Count);
        Assert.Equal(8m, (await repository.GetTimeEntriesAsync(other)).Sum(e => e.DurationHours));
    }

    [Fact]
    public async Task Two_scoped_cycles_reconcile_replacements_replays_staleness_and_record_movement()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(
                "two realistic scoped cycles were never reconciled through replacement import and recorded review.", output)) return;

        var tenant = await CreateTenantAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var oldDueDate = today.AddDays(-2).ToString("yyyy-MM-dd");
        var personEmail = $"cycle-{Guid.NewGuid():N}@northstar.test";
        var personKey = Guid.NewGuid();
        var person = NorthstarPersonas.Developer with
        {
            StaffKey = personKey,
            TenantKey = tenant,
            Email = personEmail,
            FullName = "Diagnostic test owner"
        };
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([person]);
        using (var identityScope = factory.Services.CreateScope())
        {
            await identityScope.ServiceProvider.GetRequiredService<IIdentityResolutionRepository>().CreateLinkAsync(
                new ExternalIdentityLink
                {
                    LinkKey = Guid.NewGuid(),
                    TenantId = tenant,
                    ExternalSource = DeliveryExportImportService.SourceName,
                    ExternalUserId = personEmail,
                    Email = personEmail,
                    StaffKey = personKey,
                    CreatedAtUtc = DateTime.UtcNow
                }, tenant);
        }

        var workItemsFirst =
            "Programme,Project,WorkItemId,Title,Status,DueDate,EstimatedHours,Assignee,AssigneeEmail\n" +
            $"Rollout,Portal,R-KEEP,Keep item,In progress,{oldDueDate},10,Diagnostic test owner,{personEmail}\n" +
            $"Rollout,Portal,R-REMOVE,Remove item,In progress,{oldDueDate},10,Diagnostic test owner,{personEmail}\n";
        var timeFirst =
            "EntryId,WorkItemId,Person,PersonEmail,Date,Hours,Billable\n" +
            $"T-KEEP,R-KEEP,Diagnostic test owner,{personEmail},{today.AddDays(-1):yyyy-MM-dd},3,yes\n" +
            $"T-REMOVE,R-REMOVE,Diagnostic test owner,{personEmail},{today.AddDays(-1):yyyy-MM-dd},5,yes\n";

        Assert.True((await ImportAsync(tenant, DeliveryExportKind.WorkItems, workItemsFirst)).Imported);
        Assert.True((await ImportAsync(tenant, DeliveryExportKind.TimeEntries, timeFirst)).Imported);

        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var repository = services.GetRequiredService<IProgrammeRepository>();
        var programmeKey = (await repository.GetProgrammesAsync(tenant)).Single(p => p.Name == "Rollout").ProgrammeKey;
        var check = services.GetRequiredService<IEvidenceCheckService>();
        async Task<EvidenceScope> ResolveScopeAsync(DateOnly extractedOn)
        {
            var resolution = await check.ResolveScopeAsync(tenant, new EvidenceScopeRequest(
                ProgrammeKey: programmeKey,
                From: today.AddDays(-6),
                To: today,
                ExtractedOn: extractedOn,
                Decision: "Weekly steering review"));
            Assert.NotNull(resolution.Scope);
            return resolution.Scope!;
        }

        var firstScope = await ResolveScopeAsync(today.AddDays(-21));
        var reviews = services.GetRequiredService<IEvidenceReviewService>();
        var first = (await reviews.RecordAsync(tenant, firstScope, new EvidenceReviewActor(null, null)))!;

        Assert.Equal(EvidenceReadiness.UseWithCaveats, first.Readiness);
        Assert.Contains(first.SourceNotes, note => note.Contains("more than 7 days ago", StringComparison.Ordinal));
        Assert.Equal(2, first.WorkItems);
        Assert.Equal(8m, first.RecordedHours);
        Assert.Equal(2, Assert.Single(first.Findings, f => f.FindingKey == "overdue").Numerator);

        var workItemsSecond =
            "Programme,Project,WorkItemId,Title,Status,DueDate,EstimatedHours,Assignee,AssigneeEmail\n" +
            $"Rollout,Portal,R-KEEP,Keep item,Done,{oldDueDate},10,Diagnostic test owner,{personEmail}\n" +
            $"Rollout,Portal,R-NEW,New item,In progress,{oldDueDate},10,Diagnostic test owner,{personEmail}\n";
        var timeSecond =
            "EntryId,WorkItemId,Person,PersonEmail,Date,Hours,Billable\n" +
            $"T-KEEP,R-KEEP,Diagnostic test owner,{personEmail},{today.AddDays(-1):yyyy-MM-dd},4,yes\n";

        var replacementItems = await ImportAsync(tenant, DeliveryExportKind.WorkItems, workItemsSecond);
        Assert.True(replacementItems.AwaitingConfirmation);
        Assert.True((await ConfirmAsync(tenant, replacementItems.StagingKey!.Value)).Imported);
        var replacementTime = await ImportAsync(tenant, DeliveryExportKind.TimeEntries, timeSecond);
        Assert.True(replacementTime.AwaitingConfirmation);
        Assert.Equal((8m, 4m), (replacementTime.Plan!.HoursBefore, replacementTime.Plan.HoursAfter));
        Assert.True((await ConfirmAsync(tenant, replacementTime.StagingKey!.Value)).Imported);

        // Replaying the replacement exports must not add rows or hours.
        Assert.True((await ImportAsync(tenant, DeliveryExportKind.WorkItems, workItemsSecond)).Imported);
        Assert.True((await ImportAsync(tenant, DeliveryExportKind.TimeEntries, timeSecond)).Imported);
        Assert.Equal(["R-KEEP", "R-NEW"], (await repository.GetWorkItemsAsync(tenant))
            .Select(item => item.ExternalId).Order(StringComparer.Ordinal));
        Assert.Equal(4m, (await repository.GetTimeEntriesAsync(tenant)).Sum(entry => entry.DurationHours));

        var secondScope = await ResolveScopeAsync(today);
        var second = (await reviews.RecordAsync(tenant, secondScope, new EvidenceReviewActor(null, null)))!;
        Assert.Equal(2, second.WorkItems);
        Assert.Equal(4m, second.RecordedHours);
        Assert.Equal(EvidenceReadiness.DecisionReady, second.Readiness);

        var secondPage = (await reviews.GetReviewAsync(tenant, second.ReviewKey))!;
        var overdue = secondPage.SincePrevious!.Findings.Single(f => f.FindingKey == "overdue");
        Assert.Equal(FindingTrend.Improved, overdue.Trend);
        Assert.Equal(new RecordMovement(0, 1, 2), overdue.Records);
        Assert.Equal(["R-NEW"], second.Findings.Single(f => f.FindingKey == "overdue")
            .Records.Select(record => record.ExternalId));

        var frozenFirst = (await reviews.GetReviewAsync(tenant, first.ReviewKey))!.Review;
        Assert.Equal(8m, frozenFirst.RecordedHours);
        Assert.Equal(["R-KEEP", "R-REMOVE"], frozenFirst.Findings.Single(f => f.FindingKey == "overdue")
            .Records.Select(record => record.ExternalId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_admin_previews_the_removals_in_the_browser_and_confirms_them()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        var tenant = await CreateTenantAsync();
        Assert.True((await ImportAsync(tenant, DeliveryExportKind.TimeEntries, "EntryId,Date,Hours\nT1,2026-09-01,2\nT2,2026-09-02,5\n")).Imported);

        var key = Guid.NewGuid();
        var admin = NorthstarPersonas.TenantAdmin with { StaffKey = key, TenantKey = tenant, Email = $"replace-{key:N}@northstar.test" };
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([admin]);
        var session = await PersonaSignIn.SignInAsync(factory, admin);

        var token = await session.TryGetAntiForgeryTokenAsync("/staffops/programme/import");
        Assert.NotNull(token);
        using var upload = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes("EntryId,Date,Hours\nT1,2026-09-01,2\n")), "file", "time.csv" }
        };
        var preview = await session.Client.PostAsync("/staffops/programme/import/time-entries", upload);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var html = await preview.Content.ReadAsStringAsync();
        Assert.Contains("confirm the replacement", html);
        Assert.Contains("2026-09-02, 5 h", html);

        var confirmPath = ConfirmAction().Match(html).Groups[1].Value;
        Assert.StartsWith("/staffops/programme/import/confirm/", confirmPath);
        var confirmed = await session.Client.PostAsync(confirmPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiForgeryToken().Match(html).Groups[1].Value
        }));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Contains("imported", await confirmed.Content.ReadAsStringAsync());

        using var check = factory.Services.CreateScope();
        Assert.Equal(["T1"], (await check.ServiceProvider.GetRequiredService<IProgrammeRepository>().GetTimeEntriesAsync(tenant)).Select(e => e.ExternalId));
    }

    [GeneratedRegex(@"action=""(/staffops/programme/import/confirm/[0-9a-f-]+)""")]
    private static partial Regex ConfirmAction();

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiForgeryToken();

    private async Task<FileImportResult> ImportAsync(Guid tenant, DeliveryExportKind kind, string csv)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDeliveryExportImportService>().ImportAsync(tenant, kind, csv, null);
    }

    private async Task<FileImportResult> ConfirmAsync(Guid tenant, Guid stagingKey)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDeliveryExportImportService>().ConfirmAsync(tenant, stagingKey, null);
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var key = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantRepository>().CreateAsync(new Tenant
        {
            TenantKey = key, Name = "Replacement import test", ShortCode = "ri-" + key.ToString("N")[..20],
            IsActive = true, CreatedAtUtc = DateTime.UtcNow
        });
        return key;
    }
}
