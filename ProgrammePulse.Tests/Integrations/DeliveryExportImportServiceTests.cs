using ProgrammePulse.Tests.ProgrammeOps;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Integrations.FileImport;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.Integrations;

/// <summary>
/// End-to-end through the real parser, status mapper, identity resolver and
/// run coordinator (only persistence is faked). The rules under test are
/// the ones a paid diagnostic's credibility rests on: a bad file writes
/// nothing, a re-upload never duplicates, an unmatched person is queued not
/// dropped, an unrecognised status is Unmapped not guessed, and hours never
/// silently detach from their work item.
/// </summary>
public class DeliveryExportImportServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private const string WorkItemsCsv =
        "Project,Task ID,Task Name,Status,Workstream,Assignee,Assignee Email,Due Date,Estimated Hours,Milestone,Parent ID\r\n" +
        "Website rebuild,WEB-1,\"Checkout, payments\",In progress,Payments,Jamie Gardner,JAMIE@example.com,2026-10-31,16,no,\r\n" +
        "Website rebuild,WEB-2,Launch,Done,,,,31/10/2026,,yes,WEB-1\r\n" +
        "Data platform,DATA-7,Warehouse load,Waiting on client,,Sam Freelance,sam@agency.example,,,,\r\n";

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeRawRepository : IRawConnectorPayloadRepository
    {
        public List<(Guid Tenant, string EntityType, string ExternalId)> Saved { get; } = [];

        public Task SaveAsync(Guid tenantId, string source, string sourceAccountId, string entityType, string externalId, string payloadJson, DateTime fetchedAtUtc)
        {
            Saved.Add((tenantId, entityType, externalId));
            return Task.CompletedTask;
        }

        public Task<int> DeleteOlderThanAsync(DateTime cutoffUtc) => Task.FromResult(0);
    }

    private sealed class FakeAudit : IAuditLogRepository
    {
        public List<string> Actions { get; } = [];

        public Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditLog>> GetRecentAsync(int take, Guid tenantId) => throw new NotSupportedException();
    }

    private sealed class Harness
    {
        public FakeProgrammeRepository Programme { get; } = new();
        public FakeStaffRepository Staff { get; } = new();
        public FakeIdentityResolutionRepository Identity { get; } = new();
        public FakeSyncRunRepository Runs { get; } = new();
        public FakeRawRepository Raw { get; } = new();
        public FakeAudit Audit { get; } = new();
        public FakeImportStagingRepository Staging { get; } = new();
        public SyncRunGuard Guard { get; } = new();

        public Harness()
        {
            Staff.Staff.Add(new StaffProfile
            {
                StaffKey = JamieKey, TenantId = Tenant, MemberId = 1, FullName = "Jamie Gardner", Email = "jamie@example.com",
                CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime
            });
            // An Admin has approved Jamie's link: an email match alone attributes nobody.
            Identity.Links.Add(new ExternalIdentityLink
            {
                LinkKey = Guid.NewGuid(), TenantId = Tenant, ExternalSource = "FileImport", ExternalUserId = "jamie@example.com",
                Email = "jamie@example.com", StaffKey = JamieKey, CreatedAtUtc = Now.UtcDateTime
            });
        }

        public static readonly Guid JamieKey = Guid.NewGuid();

        /// <summary>The service's clock; move it forward to expire a preview.</summary>
        public DateTimeOffset Clock { get; set; } = Now;

        // A fresh service per request, as each HTTP request gets its own
        // scoped identity resolver.
        private DeliveryExportImportService Service()
        {
            var time = new FixedTimeProvider(Clock);
            var options = Options.Create(new ProgrammeOpsOptions());
            return new DeliveryExportImportService(
                Programme, Raw, new StaffIdentityResolver(Identity, Staff, time), Audit,
                new SyncRunCoordinator(Guard, Runs, options, time), options, time, Staging);
        }

        public Task<FileImportResult> ImportAsync(DeliveryExportKind kind, string csv, Guid? tenant = null) =>
            Service().ImportAsync(tenant ?? Tenant, kind, csv, triggeredByMemberId: 7);

        public Task<FileImportResult> ConfirmAsync(Guid stagingKey, Guid? tenant = null) =>
            Service().ConfirmAsync(tenant ?? Tenant, stagingKey, triggeredByMemberId: 7);

        public Task CancelAsync(Guid stagingKey) => Service().CancelAsync(Tenant, stagingKey, triggeredByMemberId: 7);
    }

    /// <summary>
    /// The sales sample is only worth showing if it imports unedited and the
    /// Evidence Check finds something under every rule. If a rule is added
    /// or the importer tightens, this says so before a prospect sees it.
    /// </summary>
    [Fact]
    public async Task The_sales_sample_imports_unedited_and_trips_every_evidence_check_rule()
    {
        var h = new Harness();
        var today = DateOnly.FromDateTime(Now.UtcDateTime);

        var items = await h.ImportAsync(DeliveryExportKind.WorkItems, DeliveryExportSample.WorkItemsCsv(today));
        var time = await h.ImportAsync(DeliveryExportKind.TimeEntries, DeliveryExportSample.TimeEntriesCsv(today));

        Assert.True(items.Imported, items.Summary);
        Assert.True(time.Imported, time.Summary);
        Assert.Empty(items.IgnoredColumns ?? []);
        Assert.Empty(time.IgnoredColumns ?? []);

        var report = EvidenceCheckCalculator.Evaluate(
            new EvidenceCheckInput(h.Programme.Projects, h.Programme.Workstreams, h.Programme.WorkItems, h.Programme.TimeEntries,
                h.Identity.Unresolved.Count(u => !u.IsResolved), []),
            Now.UtcDateTime);

        string[] everyRuleExceptNoTime =
        [
            "unmapped-status", "committed-without-owner", "committed-without-estimate", "committed-without-due-date",
            "time-not-linked", "time-without-person", "billability-unknown", "done-without-time", "unmatched-people",
            "overdue", "blocked", "over-estimate", "time-on-uncommitted-work"
        ];
        Assert.Equal(everyRuleExceptNoTime.Order(), report.Findings.Select(f => f.Key).Order());
        Assert.Equal(EvidenceReadiness.NotDecisionReady, report.Readiness);
        Assert.Equal(3, report.Projects.Count);
    }

    [Fact]
    public async Task A_valid_file_builds_the_project_hierarchy_and_maps_every_field()
    {
        var h = new Harness();

        var result = await h.ImportAsync(DeliveryExportKind.WorkItems, WorkItemsCsv);

        Assert.True(result.Imported, result.Summary);
        Assert.Equal(3, result.Rows);
        Assert.Equal(2, h.Programme.Projects.Count);
        Assert.Equal(["Payments", "Work items", "Work items"], h.Programme.Workstreams.Select(w => w.Name).Order().ToArray());

        var web1 = h.Programme.WorkItems.Single(w => w.ExternalId == "WEB-1");
        Assert.Equal("Checkout, payments", web1.Title);
        Assert.Equal(WorkItemLifecycleStage.InProgress, web1.Stage);
        Assert.Equal("In progress", web1.RawStatus);
        Assert.Equal(Harness.JamieKey, web1.AssignedStaffKey);
        Assert.Equal(new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc), web1.DueDateUtc);
        Assert.Equal(16m, web1.EstimatedHours);
        Assert.Equal("FileImport", web1.ExternalSource);
        Assert.Contains(h.Programme.Allocations, a => a.WorkItemKey == web1.WorkItemKey && a.StaffKey == Harness.JamieKey);

        var web2 = h.Programme.WorkItems.Single(w => w.ExternalId == "WEB-2");
        Assert.True(web2.IsMilestone);
        Assert.Equal("WEB-1", web2.ParentExternalId);
        Assert.Equal(new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc), web2.DueDateUtc);

        Assert.Equal(SyncRunStatus.Succeeded, h.Runs.Single("FileImport").Status);
        Assert.Equal(3, h.Raw.Saved.Count(s => s.EntityType == "work-item"));
        Assert.Contains("ImportCompleted", h.Audit.Actions);
    }

    [Fact]
    public async Task A_programme_column_groups_projects_and_a_blank_one_keeps_the_shared_root()
    {
        var h = new Harness();
        const string csv =
            "Programme,Project,WorkItemId,Title,Status\r\n" +
            "Patient portal,Portal build,PP-1,Login,Done\r\n" +
            "Patient portal,Integration layer,PP-2,HL7 feed,In progress\r\n" +
            "Claims,Claims API,CL-1,Pricing,Backlog\r\n" +
            ",Internal tooling,INT-1,Laptop refresh,To do\r\n";

        var result = await h.ImportAsync(DeliveryExportKind.WorkItems, csv);

        Assert.True(result.Imported, result.Summary);
        Assert.Equal(["Claims", "Imported delivery data", "Patient portal"], h.Programme.Programmes.Select(p => p.Name).Order().ToArray());
        var portal = h.Programme.Programmes.Single(p => p.Name == "Patient portal").ProgrammeKey;
        Assert.Equal(["Integration layer", "Portal build"],
            h.Programme.Projects.Where(p => p.ProgrammeKey == portal).Select(p => p.Name).Order().ToArray());

        // A re-import that names a different programme moves the project
        // rather than creating a second copy of it.
        await h.ImportAsync(DeliveryExportKind.WorkItems, csv.Replace("Claims,Claims API", "Patient portal,Claims API"));
        Assert.Equal(4, h.Programme.Projects.Count);
        Assert.Equal(portal, h.Programme.Projects.Single(p => p.Name == "Claims API").ProgrammeKey);
    }

    [Fact]
    public async Task An_unrecognised_status_is_kept_as_unmapped_and_counted_not_guessed()
    {
        var h = new Harness();

        var result = await h.ImportAsync(DeliveryExportKind.WorkItems, WorkItemsCsv);

        var data7 = h.Programme.WorkItems.Single(w => w.ExternalId == "DATA-7");
        Assert.Equal(WorkItemLifecycleStage.Unmapped, data7.Stage);
        Assert.Equal("Waiting on client", data7.RawStatus);
        Assert.Equal(1, result.UnmappedStatuses);
        Assert.Contains("Unmapped", result.Summary);
    }

    [Fact]
    public async Task An_unmatched_person_is_queued_for_an_admin_not_dropped()
    {
        var h = new Harness();

        var result = await h.ImportAsync(DeliveryExportKind.WorkItems, WorkItemsCsv);

        Assert.Equal(1, result.UnresolvedPeople);
        var queued = Assert.Single(h.Identity.Unresolved);
        Assert.Equal("FileImport", queued.ExternalSource);
        Assert.Equal("sam@agency.example", queued.Email);
        Assert.Null(h.Programme.WorkItems.Single(w => w.ExternalId == "DATA-7").AssignedStaffKey);
    }

    [Fact]
    public async Task A_person_whose_email_matches_staff_is_suggested_and_the_summary_says_so()
    {
        var h = new Harness();
        h.Identity.Links.Clear();

        var result = await h.ImportAsync(DeliveryExportKind.WorkItems, WorkItemsCsv);

        Assert.Null(h.Programme.WorkItems.Single(w => w.ExternalId == "WEB-1").AssignedStaffKey);
        Assert.Equal(Harness.JamieKey, h.Identity.Unresolved.Single(u => u.Email == "jamie@example.com").SuggestedStaffKey);
        Assert.Contains("1 of them with a suggested email match to approve", result.Summary);
    }

    [Fact]
    public async Task Uploading_the_same_file_twice_updates_rather_than_duplicates()
    {
        var h = new Harness();

        await h.ImportAsync(DeliveryExportKind.WorkItems, WorkItemsCsv);
        var keys = h.Programme.WorkItems.ToDictionary(w => w.ExternalId!, w => w.WorkItemKey);
        var second = await h.ImportAsync(DeliveryExportKind.WorkItems, WorkItemsCsv.Replace("In progress", "Done"));

        Assert.True(second.Imported, second.Summary);
        Assert.Equal(3, h.Programme.WorkItems.Count);
        Assert.Equal(2, h.Programme.Projects.Count);
        Assert.Equal(3, h.Programme.Workstreams.Count);
        Assert.Equal(keys["WEB-1"], h.Programme.WorkItems.Single(w => w.ExternalId == "WEB-1").WorkItemKey);
        Assert.Equal(WorkItemLifecycleStage.Done, h.Programme.WorkItems.Single(w => w.ExternalId == "WEB-1").Stage);
    }

    [Fact]
    public async Task One_bad_row_means_nothing_is_written_and_every_problem_is_reported()
    {
        var h = new Harness();
        var csv =
            "Project,WorkItemId,Title,Status,DueDate,EstimatedHours,AssigneeEmail\n" +
            "Alpha,A-1,Good row,Done,2026-01-01,4,\n" +
            "Alpha,A-2,,Done,13/13/2026,lots,not-an-email\n" +
            "Alpha,A-1,Duplicate id,Done,,,\n";

        var result = await h.ImportAsync(DeliveryExportKind.WorkItems, csv);

        Assert.False(result.Imported);
        Assert.Empty(h.Programme.WorkItems);
        Assert.Empty(h.Programme.Projects);
        Assert.Empty(h.Runs.Runs);
        Assert.Contains(result.Errors, e => e is { Row: 3, Column: "Title" });
        Assert.Contains(result.Errors, e => e is { Row: 3, Column: "DueDate" });
        Assert.Contains(result.Errors, e => e is { Row: 3, Column: "EstimatedHours" });
        Assert.Contains(result.Errors, e => e is { Row: 3, Column: "AssigneeEmail" });
        Assert.Contains(result.Errors, e => e is { Row: 4, Column: "WorkItemId" });
        Assert.Contains("ImportRejected", h.Audit.Actions);
    }

    [Fact]
    public async Task A_missing_required_column_is_refused_and_names_accepted_spellings()
    {
        var h = new Harness();

        var result = await h.ImportAsync(DeliveryExportKind.WorkItems, "Project,Title,Status\nAlpha,Thing,Done\n");

        Assert.False(result.Imported);
        var error = Assert.Single(result.Errors);
        Assert.Equal("WorkItemId", error.Column);
        Assert.Contains("Issue key", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Two_headers_meaning_the_same_column_are_refused_rather_than_one_silently_winning()
    {
        var h = new Harness();

        var result = await h.ImportAsync(DeliveryExportKind.WorkItems, "Project,ID,Task ID,Title,Status\nAlpha,1,2,Thing,Done\n");

        Assert.False(result.Imported);
        Assert.Contains(result.Errors, e => e.Column == "WorkItemId" && e.Message.Contains("both mean"));
    }

    [Fact]
    public async Task Unrecognised_columns_are_ignored_and_reported()
    {
        var h = new Harness();

        var result = await h.ImportAsync(DeliveryExportKind.WorkItems, "Project,WorkItemId,Title,Status,Sprint\nAlpha,A-1,Thing,Done,12\n");

        Assert.True(result.Imported, result.Summary);
        Assert.Equal(["Sprint"], result.IgnoredColumns);
    }

    [Fact]
    public async Task Time_entries_link_to_imported_work_items_and_report_unknown_billability()
    {
        var h = new Harness();
        await h.ImportAsync(DeliveryExportKind.WorkItems, WorkItemsCsv);

        var result = await h.ImportAsync(DeliveryExportKind.TimeEntries,
            "Entry ID,Task ID,User,Email,Spent Date,Hours,Billable\n" +
            "T1,WEB-1,Jamie Gardner,jamie@example.com,2026-09-21,3.5,yes\n" +
            "T2,,Jamie Gardner,jamie@example.com,22/09/2026,1:30,\n");

        Assert.True(result.Imported, result.Summary);
        var t1 = h.Programme.TimeEntries.Single(t => t.ExternalId == "T1");
        Assert.Equal(h.Programme.WorkItems.Single(w => w.ExternalId == "WEB-1").WorkItemKey, t1.WorkItemKey);
        Assert.Equal(Harness.JamieKey, t1.StaffKey);
        Assert.Equal(3.5m, t1.DurationHours);
        Assert.True(t1.IsBillable);
        Assert.True(t1.BillabilityKnown);

        var t2 = h.Programme.TimeEntries.Single(t => t.ExternalId == "T2");
        Assert.Null(t2.WorkItemKey);
        Assert.Equal(1.5m, t2.DurationHours);
        Assert.Equal(new DateOnly(2026, 9, 22), t2.WorkDate);
        Assert.False(t2.BillabilityKnown);
        Assert.Contains("unknown billability", result.Summary);
    }

    [Fact]
    public async Task Time_against_a_work_item_that_was_never_imported_is_refused()
    {
        var h = new Harness();

        var result = await h.ImportAsync(DeliveryExportKind.TimeEntries, "WorkItemId,Date,Hours\nNOPE-1,2026-09-21,2\n");

        Assert.False(result.Imported);
        Assert.Contains(result.Errors, e => e.Column == "WorkItemId" && e.Message.Contains("NOPE-1"));
        Assert.Empty(h.Programme.TimeEntries);
    }

    [Fact]
    public async Task Another_tenants_work_items_are_invisible_to_a_time_import()
    {
        var h = new Harness();
        await h.ImportAsync(DeliveryExportKind.WorkItems, WorkItemsCsv, OtherTenant);

        var result = await h.ImportAsync(DeliveryExportKind.TimeEntries, "WorkItemId,Date,Hours\nWEB-1,2026-09-21,2\n");

        Assert.False(result.Imported);
        Assert.Empty(h.Programme.TimeEntries);
    }

    [Fact]
    public async Task Entries_without_an_id_are_idempotent_on_reimport_yet_identical_rows_still_count_twice()
    {
        var h = new Harness();
        const string csv = "Person,Date,Hours\nJamie Gardner,2026-09-21,2\nJamie Gardner,2026-09-21,2\n";

        await h.ImportAsync(DeliveryExportKind.TimeEntries, csv);
        var second = await h.ImportAsync(DeliveryExportKind.TimeEntries, csv);

        Assert.True(second.Imported, second.Summary);
        Assert.Equal(2, h.Programme.TimeEntries.Count);
        Assert.Equal(4m, h.Programme.TimeEntries.Sum(t => t.DurationHours));
    }

    // ---- Each upload replaces the last (GTM review 28 Sept, finding 2)

    [Fact]
    public async Task A_corrected_entry_without_an_id_replaces_its_old_version_instead_of_adding_to_it()
    {
        var h = new Harness();
        await h.ImportAsync(DeliveryExportKind.TimeEntries, "Person,Date,Hours\nJamie Gardner,2026-09-21,3\n");

        // The customer corrects 3 hours to 4 at source and re-exports.
        var preview = await h.ImportAsync(DeliveryExportKind.TimeEntries, "Person,Date,Hours\nJamie Gardner,2026-09-21,4\n");

        Assert.True(preview.AwaitingConfirmation);
        Assert.False(preview.Imported);
        Assert.Equal((3m, 4m, 1, 1), (preview.Plan!.HoursBefore, preview.Plan.HoursAfter, preview.Plan.Added, preview.Plan.Removed));
        Assert.Equal(3m, h.Programme.TimeEntries.Sum(t => t.DurationHours)); // nothing changed yet

        var confirmed = await h.ConfirmAsync(preview.StagingKey!.Value);

        Assert.True(confirmed.Imported, confirmed.Summary);
        Assert.Equal(4m, Assert.Single(h.Programme.TimeEntries).DurationHours); // 4, not 7
        Assert.Empty(h.Staging.Staged);
        Assert.Contains("from 3.0 to 4.0 hours", confirmed.Summary);
    }

    [Fact]
    public async Task A_row_deleted_at_source_is_removed_only_after_confirmation_and_cancel_changes_nothing()
    {
        var h = new Harness();
        await h.ImportAsync(DeliveryExportKind.TimeEntries, "EntryId,Date,Hours\nT1,2026-09-21,2\nT2,2026-09-22,5\n");

        var preview = await h.ImportAsync(DeliveryExportKind.TimeEntries, "EntryId,Date,Hours\nT1,2026-09-21,2\n");
        var removal = Assert.Single(preview.Plan!.Removals);
        Assert.Equal(("T2", "2026-09-22, 5 h"), (removal.Id, removal.Description));

        await h.CancelAsync(preview.StagingKey!.Value);

        Assert.Equal(["T1", "T2"], h.Programme.TimeEntries.Select(t => t.ExternalId).Order());
        Assert.Empty(h.Staging.Staged);
        Assert.Contains("ImportCancelled", h.Audit.Actions);
        Assert.False((await h.ConfirmAsync(preview.StagingKey.Value)).Imported); // a cancelled preview can't be revived
    }

    [Fact]
    public async Task The_same_file_again_or_a_file_that_only_adds_imports_straight_away()
    {
        var h = new Harness();
        const string first = "EntryId,Date,Hours\nT1,2026-09-21,2\n";
        await h.ImportAsync(DeliveryExportKind.TimeEntries, first);

        var replay = await h.ImportAsync(DeliveryExportKind.TimeEntries, first);
        var grown = await h.ImportAsync(DeliveryExportKind.TimeEntries, first + "T2,2026-09-22,1\n");

        Assert.True(replay.Imported && !replay.AwaitingConfirmation, replay.Summary);
        Assert.True(grown.Imported && !grown.AwaitingConfirmation, grown.Summary);
        Assert.Equal(3m, h.Programme.TimeEntries.Sum(t => t.DurationHours));
        Assert.Empty(h.Staging.Staged);
    }

    [Fact]
    public async Task A_removed_work_item_takes_its_allocations_but_its_recorded_time_is_kept_unlinked()
    {
        var h = new Harness();
        await h.ImportAsync(DeliveryExportKind.WorkItems, WorkItemsCsv);
        await h.ImportAsync(DeliveryExportKind.TimeEntries, "EntryId,WorkItemId,Date,Hours\nT1,WEB-1,2026-09-21,6\n");
        var web1 = h.Programme.WorkItems.Single(w => w.ExternalId == "WEB-1").WorkItemKey;
        Assert.Contains(h.Programme.Allocations, a => a.WorkItemKey == web1);

        // WEB-1 was deleted at source.
        var withoutWeb1 = string.Join("\r\n", WorkItemsCsv.Split("\r\n").Where(line => !line.Contains(",WEB-1,")));
        var preview = await h.ImportAsync(DeliveryExportKind.WorkItems, withoutWeb1);

        Assert.Equal(("WEB-1", 6m), (Assert.Single(preview.Plan!.Removals).Id, preview.Plan.HoursUnlinkedByRemoval));
        var confirmed = await h.ConfirmAsync(preview.StagingKey!.Value);

        Assert.True(confirmed.Imported, confirmed.Summary);
        Assert.DoesNotContain(h.Programme.WorkItems, w => w.ExternalId == "WEB-1");
        Assert.DoesNotContain(h.Programme.Allocations, a => a.WorkItemKey == web1);
        var kept = Assert.Single(h.Programme.TimeEntries);
        Assert.Null(kept.WorkItemKey);
        Assert.Equal(6m, kept.DurationHours);
        Assert.Contains("6.0 hours recorded against them are now unlinked", confirmed.Summary);
    }

    [Fact]
    public async Task Confirm_refuses_to_apply_removals_the_person_did_not_see()
    {
        var h = new Harness();
        await h.ImportAsync(DeliveryExportKind.TimeEntries, "EntryId,Date,Hours\nT1,2026-09-21,2\nT2,2026-09-22,5\n");
        var preview = await h.ImportAsync(DeliveryExportKind.TimeEntries, "EntryId,Date,Hours\nT1,2026-09-21,2\n");

        // Another import lands between the preview and the confirmation.
        await h.ImportAsync(DeliveryExportKind.TimeEntries, "EntryId,Date,Hours\nT1,2026-09-21,2\nT2,2026-09-22,5\nT3,2026-09-23,1\n");
        var confirmed = await h.ConfirmAsync(preview.StagingKey!.Value);

        Assert.False(confirmed.Imported);
        Assert.True(confirmed.AwaitingConfirmation);
        Assert.StartsWith("The data changed since the preview", confirmed.Summary);
        Assert.Equal(2, confirmed.Plan!.Removed); // T2 and T3 now, not just T2
        Assert.Equal(3, h.Programme.TimeEntries.Count); // nothing applied
    }

    [Fact]
    public async Task An_expired_preview_changes_nothing()
    {
        var h = new Harness();
        await h.ImportAsync(DeliveryExportKind.TimeEntries, "EntryId,Date,Hours\nT1,2026-09-21,2\nT2,2026-09-22,5\n");
        var preview = await h.ImportAsync(DeliveryExportKind.TimeEntries, "EntryId,Date,Hours\nT1,2026-09-21,2\n");

        h.Clock = Now + DeliveryExportImportService.StagingLifetime + TimeSpan.FromMinutes(1);
        var confirmed = await h.ConfirmAsync(preview.StagingKey!.Value);

        Assert.False(confirmed.Imported);
        Assert.Contains("expired", confirmed.Summary);
        Assert.Equal(2, h.Programme.TimeEntries.Count);
        Assert.Empty(h.Staging.Staged);
    }

    [Fact]
    public async Task Another_tenant_cannot_confirm_a_staged_import()
    {
        var h = new Harness();
        await h.ImportAsync(DeliveryExportKind.TimeEntries, "EntryId,Date,Hours\nT1,2026-09-21,2\nT2,2026-09-22,5\n");
        var preview = await h.ImportAsync(DeliveryExportKind.TimeEntries, "EntryId,Date,Hours\nT1,2026-09-21,2\n");

        var foreign = await h.ConfirmAsync(preview.StagingKey!.Value, OtherTenant);

        Assert.False(foreign.Imported);
        Assert.Equal(2, h.Programme.TimeEntries.Count);
        Assert.Single(h.Staging.Staged);
    }

    [Fact]
    public async Task Entry_ids_that_differ_only_by_case_are_refused_rather_than_merged()
    {
        var h = new Harness();

        var result = await h.ImportAsync(DeliveryExportKind.TimeEntries, "EntryId,Date,Hours\nT-1,2026-09-21,2\nt-1,2026-09-22,5\n");

        Assert.False(result.Imported);
        Assert.Contains(result.Errors, e => e.Column == "EntryId");
        Assert.Empty(h.Programme.TimeEntries);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("1:75")]
    [InlineData("20000")]
    public async Task Implausible_hours_are_refused(string hours)
    {
        var h = new Harness();

        var result = await h.ImportAsync(DeliveryExportKind.TimeEntries, $"Date,Hours\n2026-09-21,{hours}\n");

        Assert.False(result.Imported);
        Assert.Contains(result.Errors, e => e.Column == "Hours");
    }

    [Fact]
    public async Task A_file_with_only_a_header_is_refused()
    {
        var h = new Harness();

        var result = await h.ImportAsync(DeliveryExportKind.WorkItems, "Project,WorkItemId,Title,Status\n");

        Assert.False(result.Imported);
        Assert.Empty(h.Runs.Runs);
    }

    [Fact]
    public async Task A_concurrent_import_for_the_same_tenant_is_refused_without_writing()
    {
        var h = new Harness();
        using var held = h.Guard.TryEnter(Tenant, DeliveryExportImportService.SourceName);

        var result = await h.ImportAsync(DeliveryExportKind.WorkItems, WorkItemsCsv);

        Assert.False(result.Imported);
        Assert.Contains("already running", result.Summary);
        Assert.Empty(h.Programme.WorkItems);
    }

    [Fact]
    public void Templates_round_trip_through_the_parser_with_every_column_recognised()
    {
        foreach (var kind in Enum.GetValues<DeliveryExportKind>())
        {
            var rows = DelimitedTextParser.Parse(DeliveryExportSchema.TemplateCsv(kind), 10);
            Assert.Equal(2, rows.Count);
            Assert.Equal(DeliveryExportSchema.ColumnsFor(kind).Select(c => c.Name), rows[0]);
        }
    }

    [Fact]
    public async Task The_work_items_template_itself_imports_cleanly()
    {
        var h = new Harness();

        var result = await h.ImportAsync(DeliveryExportKind.WorkItems, DeliveryExportSchema.TemplateCsv(DeliveryExportKind.WorkItems));

        Assert.True(result.Imported, result.Summary + string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void Aliases_never_collide_within_one_file_kind()
    {
        foreach (var kind in Enum.GetValues<DeliveryExportKind>())
        {
            var spellings = DeliveryExportSchema.ColumnsFor(kind)
                .SelectMany(c => c.Aliases.Append(c.Name))
                .Select(DeliveryExportSchema.NormaliseHeader)
                .ToList();
            Assert.Empty(spellings.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key));
        }
    }
}
