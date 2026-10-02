using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Integrations.FileImport;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Tests.ProgrammeOps;
using static ProgrammePulse.Tests.Demo.NorthstarDemoDataset;

namespace ProgrammePulse.Tests.Demo;

/// <summary>
/// Pins what the Northstar demo is for. Each test is one value case a buyer
/// is shown; if the data drifts so that a case no longer holds, the demo
/// would quietly stop proving it, so the build fails instead. Runs through
/// the real importer, status mapper, identity resolver and Evidence Check
/// calculator, with only persistence faked.
/// </summary>
public class NorthstarDemoDatasetTests
{
    private static readonly Guid Tenant = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public void The_dataset_is_deterministic()
    {
        Assert.Equal(TimeEntriesCsv(Today), TimeEntriesCsv(Today));
        Assert.Equal(WorkItemsCsv(Today), WorkItemsCsv(Today));
    }

    [Fact]
    public void It_is_big_enough_to_look_like_a_real_agency()
    {
        Assert.Equal(25, People.Count(p => p.IsStaff));
        Assert.True(WorkItems.Count >= 100, $"{WorkItems.Count} work items");
        Assert.True(TimeEntries(Today).Count >= 1_500, $"{TimeEntries(Today).Count} time entries");
        Assert.Equal(5, WorkItems.Select(w => w.Programme).Distinct().Count());
    }

    [Fact]
    public void Every_item_gets_the_hours_it_was_written_with()
    {
        var logged = TimeEntries(Today).Where(e => e.WorkItemId is not null)
            .GroupBy(e => e.WorkItemId!).ToDictionary(g => g.Key, g => g.Sum(e => e.Hours));

        var short_ = WorkItems.Where(w => w.Spent > 0 && logged.GetValueOrDefault(w.Id) < w.Spent)
            .Select(w => $"{w.Id}: {logged.GetValueOrDefault(w.Id)} of {w.Spent}").ToList();

        Assert.True(short_.Count == 0, "Not enough capacity for: " + string.Join("; ", short_));
    }

    [Fact]
    public void Nobody_records_time_on_a_weekend_or_on_approved_leave()
    {
        var entries = TimeEntries(Today);

        Assert.DoesNotContain(entries, e => e.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        Assert.DoesNotContain(entries, e => IsOnLeave(e.Person.Key, e.Date, Today));
        Assert.All(entries, e => Assert.True(e.Date < Today));
    }

    [Fact]
    public void Utilisation_shows_one_overloaded_specialist_and_one_person_on_the_bench()
    {
        var hours = TimeEntries(Today).GroupBy(e => e.Person.Key).ToDictionary(g => g.Key, g => g.Sum(e => e.Hours));
        decimal Utilisation(string key)
        {
            var person = Person(key);
            var weeks = Math.Min(HistoryDays, person.StartedDaysAgo) / 7m;
            return hours[key] / (person.HoursPerWeek * weeks);
        }

        Assert.True(Utilisation("owen") > 1.05m, $"owen {Utilisation("owen"):P0}");
        Assert.True(Utilisation("mateusz") < 0.6m, $"mateusz {Utilisation("mateusz"):P0}");
        foreach (var key in new[] { "alex", "kwame", "grace", "ben" })
            Assert.InRange(Utilisation(key), 0.75m, 1.0m);
    }

    [Fact]
    public void Harbour_finishes_work_well_over_estimate_and_Kestrel_does_not()
    {
        // Completed items only: an in-progress item under its estimate is
        // not evidence of anything yet.
        decimal Overrun(string programme)
        {
            var estimated = WorkItems.Where(w => w.Programme == programme && w.Status == "Done" && w.Estimate is not null).ToList();
            return estimated.Sum(w => w.Spent) / estimated.Sum(w => w.Estimate!.Value) - 1m;
        }

        Assert.True(Overrun(Harbour) > 0.15m, $"Harbour {Overrun(Harbour):P0}");
        Assert.InRange(Overrun(Kestrel), -0.10m, 0.10m);
    }

    [Fact]
    public void The_shared_DevOps_engineer_is_booked_past_his_contract_and_worse_once_Brightwater_starts()
    {
        decimal Booked(int week) => Bookings.Where(b => b.Person == "owen" && week >= b.FromWeek && week <= b.ToWeek).Sum(b => b.HoursPerWeek);

        Assert.True(Booked(0) > Person("owen").HoursPerWeek);
        Assert.True(Booked(2) > Booked(0));
        Assert.Contains(Leave, l => l.Person == "owen" && l.Status == LeaveRequestStatus.Approved && l.FromDaysAgo < 0);
        Assert.Contains(Bookings, b => b.Person is null);
    }

    [Fact]
    public void Two_skills_have_exactly_one_validated_practitioner()
    {
        int Cover(string skill) => Assertions.Count(a => a.Skill == skill
            && a.State == DemoAssertionState.Validated && a.Level >= ProficiencyRubric.CoverThreshold);

        Assert.Equal(1, Cover("hl7-fhir"));
        Assert.Equal(1, Cover("azure-infrastructure"));
        Assert.True(Cover("react") >= 2);
        Assert.All(Assertions, a => Assert.Contains(Skills, s => s.Key == a.Skill));
        Assert.All(Components, c => Assert.True(Person(c.Owner).IsStaff));
    }

    [Fact]
    public async Task The_exports_import_unedited_and_the_Evidence_Check_finds_the_designed_problems()
    {
        var h = new Harness();

        var items = await h.ImportAsync(DeliveryExportKind.WorkItems, WorkItemsCsv(Today));
        var time = await h.ImportAsync(DeliveryExportKind.TimeEntries, TimeEntriesCsv(Today));

        Assert.True(items.Imported, items.Summary);
        Assert.True(time.Imported, time.Summary);
        Assert.Empty(items.IgnoredColumns ?? []);
        Assert.Equal(5, h.Programme.Programmes.Count);

        // The contractor is on the identity queue, not dropped.
        Assert.Contains(h.Identity.Unresolved, u => u.Email == Person("sam").Email);
        Assert.All(People.Where(p => p.IsStaff), p => Assert.DoesNotContain(h.Identity.Unresolved, u => u.Email == p.Email));

        var report = EvidenceCheckCalculator.Evaluate(
            new EvidenceCheckInput(h.Programme.Projects, h.Programme.Workstreams, h.Programme.WorkItems, h.Programme.TimeEntries,
                h.Identity.Unresolved.Count(u => !u.IsResolved), []),
            Now.UtcDateTime);

        string[] designed =
        [
            "unmapped-status", "committed-without-owner", "committed-without-estimate", "committed-without-due-date",
            "time-not-linked", "billability-unknown", "unmatched-people", "overdue", "blocked", "over-estimate"
        ];
        var found = report.Findings.Select(f => f.Key).ToHashSet();
        Assert.All(designed, key => Assert.Contains(key, found));
        // The portfolio can be reported with caveats; the retainer, run in
        // the client's own status vocabulary, cannot; the mobile app can.
        Assert.Equal(EvidenceReadiness.UseWithCaveats, report.Readiness);
        Assert.Equal(EvidenceReadiness.NotDecisionReady, report.Projects.Single(p => p.Project == "Managed service").Readiness);
        Assert.Equal(EvidenceReadiness.DecisionReady, report.Projects.Single(p => p.Project == "Adjuster mobile app").Readiness);
    }

    private sealed class Harness
    {
        public FakeProgrammeRepository Programme { get; } = new();
        public FakeStaffRepository Staff { get; } = new();
        public FakeIdentityResolutionRepository Identity { get; } = new();
        public FakeSyncRunRepository Runs { get; } = new();
        private readonly SyncRunGuard _guard = new();

        public Harness()
        {
            var memberId = 1;
            foreach (var person in People.Where(p => p.IsStaff))
            {
                var staffKey = Guid.NewGuid();
                Staff.Staff.Add(new StaffProfile
                {
                    StaffKey = staffKey, TenantId = Tenant, MemberId = memberId++, FullName = person.FullName,
                    Email = person.Email, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime
                });
                // Approved as the demo's Admin does (NorthstarDemoSeeder): an
                // email match alone is only a suggestion on the identity queue.
                Identity.Links.Add(new ExternalIdentityLink
                {
                    LinkKey = Guid.NewGuid(), TenantId = Tenant, ExternalSource = "FileImport",
                    ExternalUserId = person.Email.ToLowerInvariant(), Email = person.Email.ToLowerInvariant(),
                    StaffKey = staffKey, CreatedAtUtc = Now.UtcDateTime
                });
            }
        }

        public Task<FileImportResult> ImportAsync(DeliveryExportKind kind, string csv)
        {
            var time = new FixedTimeProvider(Now);
            var options = Options.Create(new ProgrammeOpsOptions());
            var service = new DeliveryExportImportService(
                Programme, new NullRaw(), new StaffIdentityResolver(Identity, Staff, time), new NullAudit(),
                new SyncRunCoordinator(_guard, Runs, options, time), options, time, new FakeImportStagingRepository());
            return service.ImportAsync(Tenant, kind, csv, triggeredByMemberId: null);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NullRaw : IRawConnectorPayloadRepository
    {
        public Task SaveAsync(Guid tenantId, string source, string sourceAccountId, string entityType, string externalId, string payloadJson, DateTime fetchedAtUtc) =>
            Task.CompletedTask;

        public Task<int> DeleteOlderThanAsync(DateTime cutoffUtc) => Task.FromResult(0);
    }

    private sealed class NullAudit : IAuditLogRepository
    {
        public Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AuditLog>> GetRecentAsync(int take, Guid tenantId) => throw new NotSupportedException();
    }
}
