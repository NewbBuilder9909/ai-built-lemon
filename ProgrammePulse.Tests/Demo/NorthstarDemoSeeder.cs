using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.ContractOps;
using ProgrammePulse.Services.Integrations.FileImport;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Tests.Personas;
using static ProgrammePulse.Tests.Demo.NorthstarDemoDataset;

namespace ProgrammePulse.Tests.Demo;

public sealed record NorthstarDemoSeedResult(int Staff, int WorkItems, int TimeEntries, int Bookings, int Contracts, int SkillAssertions, string TotpSecret);

/// <summary>
/// Loads <see cref="NorthstarDemoDataset"/> into a running application
/// through the same services a customer's actions go through: staff are
/// onboarded by the admin service (real Umbraco members), delivery data
/// arrives as two CSV exports through the file import (real status mapping,
/// identity queue and sync run), contracts and invoices through the
/// contract admin service, skills through the assertion service's
/// declare-then-validate flow. Repositories are used directly only where the
/// product's own path needs a signed-in approver (leave) or has no command
/// yet (bookings, RAID with fixed keys, baselines).
///
/// Lives in the test project, like <see cref="NorthstarPersonaSeeder"/>, so
/// demo accounts and their shared password cannot ship.
///
/// Meant for an empty database. Re-running is safe — every step checks what
/// is already there, and the imports are idempotent by id — but dates are
/// fixed at the first run, so reload into a fresh database to roll the
/// twelve-week window forward.
/// </summary>
public sealed class NorthstarDemoSeeder(IServiceProvider services)
{
    public static readonly Guid Tenant = NorthstarPersonas.NorthstarTenantKey;
    public static readonly Guid MeridianTenant = NorthstarPersonas.MeridianTenantKey;
    private const string BookingSource = "NorthstarDemo";

    /// <summary>
    /// The TOTP secret for the Admin and Platform Admin personas. Set
    /// PROGRAMMEPULSE_PERSONA_TOTP_SECRET to a Base32 value you have added to
    /// an authenticator app (docs/demo-data.md); otherwise a fresh one is
    /// generated per run and returned in the result.
    /// </summary>
    public static readonly string TotpSecret =
        Environment.GetEnvironmentVariable("PROGRAMMEPULSE_PERSONA_TOTP_SECRET")
        ?? ProgrammePulse.Services.Security.TotpAuthenticator.GenerateSecret();

    public static IReadOnlyList<PersonaDefinition> Personas =>
        NorthstarPersonas.Signable.Select(p => p with { TotpSecret = TotpSecret }).ToList();

    public async Task<NorthstarDemoSeedResult> SeedAsync()
    {
        var today = DateOnly.FromDateTime(services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);

        await EnsureTenantsAsync();
        await new NorthstarPersonaSeeder(services).SeedAsync(Personas);
        await PlacePersonasInTheirTeamsAsync();
        var staff = await OnboardStaffAsync();
        await SeedLeaveAsync(staff, today);

        // History replayed in order: the plan as it stood, estimates captured
        // before work began (the product refuses them afterwards), then the
        // export as it stands today and the time recorded since.
        var firstRun = !await HasEstimatesAsync();
        if (firstRun)
        {
            await ImportAsync(Tenant, DeliveryExportKind.WorkItems, WorkItemsCsv(today, asPlanned: true));
            await CaptureEstimatesAsync(staff, today);
        }
        await ImportAsync(Tenant, DeliveryExportKind.WorkItems, WorkItemsCsv(today));
        await ImportAsync(Tenant, DeliveryExportKind.TimeEntries, TimeEntriesCsv(today));
        await ReviewEstimatesAsync(staff, today);

        var projects = await SeedPortfolioAsync(staff, today);
        var bookings = await SeedBookingsAsync(staff, projects, today);
        await SeedRaidAsync(staff, projects, today);
        var contracts = await SeedContractsAsync(staff, today);
        var assertions = await SeedSkillsAsync(staff, today);
        await SeedContinuityAsync(staff);
        await CaptureTrendAsync(staff, today);
        await RecordEvidenceReviewAsync(staff, today);
        await RaiseAlertsAsync();
        await SeedMeridianAsync(today);

        return new NorthstarDemoSeedResult(staff.Count, WorkItems.Count, TimeEntries(today).Count, bookings, contracts, assertions, TotpSecret);
    }

    // ---- Tenants and people ---------------------------------------------------

    private async Task EnsureTenantsAsync()
    {
        using var scope = services.CreateScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var now = DateTime.UtcNow;

        var northstar = await tenants.GetByKeyAsync(Tenant)
            ?? throw new InvalidOperationException("The default tenant row is missing; the Tenancy migration has not run.");
        await tenants.UpdateAsync(northstar with
        {
            Name = "Northstar Digital", Status = TenantStatus.Active, Plan = TenantPlan.Enterprise, IsActive = true, UpdatedAtUtc = now
        });

        if (await tenants.GetByKeyAsync(MeridianTenant) is null)
        {
            await tenants.CreateAsync(new Tenant
            {
                TenantKey = MeridianTenant, Name = "Meridian Consulting", ShortCode = "meridian", IsActive = true,
                Status = TenantStatus.Trial, Plan = TenantPlan.Professional, TrialEndsAtUtc = now.AddDays(10), CreatedAtUtc = now
            });
        }
    }

    /// <summary>
    /// The persona seeder puts every persona in one "Northstar" team; the
    /// demo needs them in the teams the dataset describes, or a Team Lead's
    /// team-scoped reporting shows four people instead of her team. There is
    /// no command for moving someone between teams, so this is the same
    /// direct write the persona seeder uses to restore its fixture.
    /// </summary>
    private async Task PlacePersonasInTheirTeamsAsync()
    {
        using var scope = services.CreateScope();
        using var database = scope.ServiceProvider.GetRequiredService<Umbraco.Cms.Infrastructure.Scoping.IScopeProvider>().CreateScope();
        foreach (var person in People.Where(p => p.Persona is not null))
        {
            await database.Database.ExecuteAsync(new NPoco.Sql(
                "UPDATE StaffOps_Staff SET team=@0 WHERE staffKey=@1 AND tenantId=@2", person.Team, person.Persona!.StaffKey, Tenant));
        }
        database.Complete();
    }

    /// <summary>Person key → staff key, for everyone with a profile.</summary>
    private async Task<Dictionary<string, Guid>> OnboardStaffAsync()
    {
        using var scope = services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IStaffAdminService>();
        var repository = scope.ServiceProvider.GetRequiredService<IStaffRepository>();
        var rates = scope.ServiceProvider.GetRequiredService<IStaffRateRepository>();

        var existing = (await repository.GetByTenantAsync(Tenant)).ToDictionary(s => s.Email, StringComparer.OrdinalIgnoreCase);
        foreach (var person in People.Where(p => p.IsStaff && p.Persona is null && !existing.ContainsKey(p.Email)))
        {
            var result = await admin.OnboardAsync(Tenant, new StaffOnboardingRequest(
                person.FullName, person.Email, NorthstarPersonaSeeder.Password, person.Role, person.JobTitle,
                "Delivery", person.Team, person.HoursPerWeek), actorMemberId: null);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Could not onboard {person.FullName}: {string.Join("; ", result.Errors)}");
        }

        var profiles = (await repository.GetByTenantAsync(Tenant)).ToDictionary(s => s.Email, StringComparer.OrdinalIgnoreCase);
        var keys = People.Where(p => p.IsStaff).ToDictionary(p => p.Key, p => profiles[p.Email].StaffKey);

        // Rates as they would stand in a real organisation: set when each
        // person joined, so the twelve weeks of history can be costed. Written
        // through the real repository with its clock set to that date; a
        // rate set "now" would leave every past hour uncosted.
        var scopeProvider = scope.ServiceProvider.GetRequiredService<Umbraco.Cms.Infrastructure.Scoping.IScopeProvider>();
        IStaffRateRepository RatesAt(int daysAgo) => new StaffRateRepository(scopeProvider, new FixedClock(DateTime.UtcNow.AddDays(-daysAgo)));
        foreach (var person in People.Where(p => p.IsStaff && p.CostPerHour > 0))
        {
            var staffKey = keys[person.Key];
            if ((await rates.GetHistoryAsync(staffKey)).Count == 0)
            {
                var joined = person.StartedDaysAgo < HistoryDays ? person.StartedDaysAgo + 5 : JoinedDaysAgo;
                var change = RateChanges.SingleOrDefault(c => c.Person == person.Key);
                await RatesAt(joined).SetCurrentRateAsync(staffKey, change?.PreviousCostPerHour ?? person.CostPerHour, "GBP", keys["emma"]);
                if (change is not null)
                    await RatesAt(change.ChangedDaysAgo).SetCurrentRateAsync(staffKey, person.CostPerHour, "GBP", keys["emma"]);
            }
            else if ((await rates.GetCurrentAsync(staffKey))?.CostPerHour != person.CostPerHour)
            {
                await rates.SetCurrentRateAsync(staffKey, person.CostPerHour, "GBP", keys["emma"]);
            }
        }

        return keys;
    }

    /// <summary>
    /// Leave goes straight to the repositories because approval needs a
    /// signed-in approver; the availability rows mirror what
    /// LeaveApprovalService.ApproveAsync writes.
    /// </summary>
    private async Task SeedLeaveAsync(IReadOnlyDictionary<string, Guid> staff, DateOnly today)
    {
        using var scope = services.CreateScope();
        var requests = scope.ServiceProvider.GetRequiredService<ILeaveRequestRepository>();
        var availability = scope.ServiceProvider.GetRequiredService<IAvailabilityRepository>();

        foreach (var leave in Leave)
        {
            var staffKey = staff[leave.Person];
            var from = today.AddDays(-leave.FromDaysAgo);
            var to = today.AddDays(-leave.ToDaysAgo);
            if ((await requests.GetForStaffAsync(staffKey, Tenant)).Any(r => r.RequestedFrom == from && r.RequestedTo == to))
                continue;

            var decided = leave.Status is LeaveRequestStatus.Approved or LeaveRequestStatus.Rejected;
            var created = today.AddDays(-leave.FromDaysAgo - 21).ToDateTime(new TimeOnly(9, 30), DateTimeKind.Utc);
            await requests.CreateAsync(new LeaveRequest
            {
                RequestId = Guid.NewGuid(), TenantId = Tenant, StaffKey = staffKey, RequestedFrom = from, RequestedTo = to,
                Type = leave.Type, Status = leave.Status, Notes = leave.Notes,
                ApprovedByStaffKey = decided ? staff["daniel"] : null,
                CreatedAtUtc = created, DecidedAtUtc = decided ? created.AddDays(1) : null
            }, Tenant);

            if (leave.Status != LeaveRequestStatus.Approved)
                continue;
            for (var date = from; date <= to; date = date.AddDays(1))
            {
                await availability.CreateAsync(new Availability
                {
                    StaffKey = staffKey, Date = date, StartTime = TimeOnly.MinValue, EndTime = TimeOnly.MaxValue,
                    Status = leave.Type == LeaveType.Sick ? AvailabilityStatus.OutOfOffice : AvailabilityStatus.Holiday,
                    Source = AvailabilitySource.LeavePolicy
                });
            }
        }
    }

    private async Task ImportAsync(Guid tenant, DeliveryExportKind kind, string csv)
    {
        await ImportOnceAsync(tenant, kind, csv);

        // An email match is only a suggestion on the identity queue until an
        // Admin approves it (docs/programme-ops.md). The demo tenant has been
        // looked after, so approve them as its Admin would, through the same
        // service and audit trail, and import again so the approved links take
        // effect: re-importing a file updates rather than duplicates. People
        // with no matching profile, such as the contractor, stay on the queue.
        if (await ApproveIdentitySuggestionsAsync(tenant) > 0)
        {
            await ImportOnceAsync(tenant, kind, csv);
        }
    }

    private async Task ImportOnceAsync(Guid tenant, DeliveryExportKind kind, string csv)
    {
        using var scope = services.CreateScope();
        var import = scope.ServiceProvider.GetRequiredService<IDeliveryExportImportService>();
        var result = await import.ImportAsync(tenant, kind, csv, triggeredByMemberId: null);
        // Dates are relative to today, so a re-seed on another day replaces the
        // last run's rows. Confirm that the way an operator reloading the demo would;
        // before imports replaced, the old rows piled up beside the new ones.
        if (result.AwaitingConfirmation)
            result = await import.ConfirmAsync(tenant, result.StagingKey!.Value, triggeredByMemberId: null);
        if (!result.Imported)
        {
            throw new InvalidOperationException($"The demo {kind} export was refused: {result.Summary} "
                + string.Join("; ", result.Errors.Take(5).Select(e => $"row {e.Row} {e.Column}: {e.Message}")));
        }
    }

    private async Task<int> ApproveIdentitySuggestionsAsync(Guid tenant)
    {
        using var scope = services.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IIdentityResolutionRepository>();
        var approvals = scope.ServiceProvider.GetRequiredService<IIdentityQueueService>();
        var approved = 0;
        foreach (var row in (await queue.GetUnresolvedAsync(tenant)).Where(u => u.SuggestedStaffKey is not null))
        {
            var message = await approvals.ApproveSuggestionAsync(tenant, row.UnresolvedIdentityKey, actorMemberId: null);
            approved += message.StartsWith("Linked", StringComparison.Ordinal) ? 1 : 0;
        }

        return approved;
    }

    // ---- Estimates, trend and alerts ---------------------------------------------

    private async Task<bool> HasEstimatesAsync()
    {
        using var scope = services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IEstimateBaselineRepository>().GetForTenantAsync(Tenant)).Count > 0;
    }

    /// <summary>
    /// The calibration service with its clock at a given moment, so each
    /// estimate is captured on the day its work was planned and reviewed on
    /// the day it finished.
    /// </summary>
    private static EstimateCalibrationService CalibrationAt(IServiceProvider scoped, DateTime at) => new(
        scoped.GetRequiredService<IEstimateBaselineRepository>(),
        scoped.GetRequiredService<IProgrammeReadRepository>(),
        scoped.GetRequiredService<IStaffRepository>(),
        scoped.GetRequiredService<IAuditLogRepository>(),
        new FixedClock(at));

    private async Task CaptureEstimatesAsync(IReadOnlyDictionary<string, Guid> staff, DateOnly today)
    {
        using var scope = services.CreateScope();
        var programme = scope.ServiceProvider.GetRequiredService<IProgrammeReadRepository>();
        foreach (var estimate in Estimates)
        {
            var item = Item(estimate.Item);
            var workItem = await programme.GetWorkItemByExternalIdAsync(DeliveryExportImportService.SourceName, item.Id, Tenant)
                ?? throw new InvalidOperationException($"{item.Id} was not imported.");
            var planned = today.AddDays(-Math.Max(item.StartDaysAgo, 1) - 2).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);
            await CalibrationAt(scope.ServiceProvider, planned).CaptureAsync(Tenant, workItem.WorkItemKey, staff[estimate.Estimator], null);
        }
    }

    private async Task ReviewEstimatesAsync(IReadOnlyDictionary<string, Guid> staff, DateOnly today)
    {
        using var scope = services.CreateScope();
        var baselines = await scope.ServiceProvider.GetRequiredService<IEstimateBaselineRepository>().GetForTenantAsync(Tenant);
        var programme = scope.ServiceProvider.GetRequiredService<IProgrammeReadRepository>();
        foreach (var estimate in Estimates.Where(e => e.Review != DemoEstimateReview.Pending))
        {
            var item = Item(estimate.Item);
            var workItem = await programme.GetWorkItemByExternalIdAsync(DeliveryExportImportService.SourceName, item.Id, Tenant);
            var baseline = baselines.FirstOrDefault(b => b.WorkItemKey == workItem?.WorkItemKey);
            if (baseline is null || baseline.ReviewedAtUtc is not null)
                continue;
            var reviewer = item.Programme == Harbour ? staff["sarah"] : item.Programme == Kestrel ? staff["daniel"] : staff["hannah"];
            var finished = today.AddDays(Math.Min(item.DueInDays ?? -1, -1) + 1).ToDateTime(new TimeOnly(16, 0), DateTimeKind.Utc);
            await CalibrationAt(scope.ServiceProvider, finished).ReviewAsync(Tenant, baseline.EstimateBaselineKey, reviewer,
                comparable: estimate.Review == DemoEstimateReview.Comparable, estimate.Note, null);
        }
    }

    /// <summary>
    /// The four-week period that ended yesterday, captured this morning. Only
    /// one: a snapshot holds the data as it stands when it is taken, and the
    /// demo's history is all imported today, so snapshots backdated with a
    /// shifted clock recorded today's figures under earlier periods (three
    /// identical rows). The trend grows as later periods are captured.
    /// </summary>
    private async Task CaptureTrendAsync(IReadOnlyDictionary<string, Guid> staff, DateOnly today)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        if ((await sp.GetRequiredService<IReportingSnapshotService>().GetHistoryAsync(Tenant)).Count > 0)
            return;
        var end = today.AddDays(-1);
        var capture = new ReportingSnapshotService(sp.GetRequiredService<IReportingQueryService>(), sp.GetRequiredService<IProgrammeRepository>(),
            new FixedClock(today.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc)));
        var result = await capture.CaptureAsync(end.AddDays(-27), end, staff["priya"], Tenant);
        if (result.Status != CommandStatus.Succeeded)
            throw new InvalidOperationException($"Demo trend snapshot refused: {result.Message}");
    }

    /// <summary>
    /// This week's review: the Evidence Check recorded by the project
    /// manager, with owners and decisions on the findings that matter. Goes
    /// through the review service, so its rules apply (a fix needs an owner
    /// and a date, accepting a gap needs the caveat).
    /// </summary>
    private async Task RecordEvidenceReviewAsync(IReadOnlyDictionary<string, Guid> staff, DateOnly today)
    {
        using var scope = services.CreateScope();
        var reviews = scope.ServiceProvider.GetRequiredService<IEvidenceReviewService>();
        if ((await reviews.GetHistoryAsync(Tenant)).Count > 0)
            return;

        var recordedBy = new EvidenceReviewActor(staff["sarah"], null);
        // This week's review: the default scope, the whole agency over the last seven days.
        var weekly = (await scope.ServiceProvider.GetRequiredService<IEvidenceCheckService>().ResolveScopeAsync(Tenant, new EvidenceScopeRequest())).Scope!;
        var review = await reviews.RecordAsync(Tenant, weekly, recordedBy)
            ?? throw new InvalidOperationException("The Evidence Check had no data to record.");

        var decisions = new (string FindingKey, FindingDisposition Decision, string? Owner, int? DueInDays, string? Note)[]
        {
            ("unmapped-status", FindingDisposition.FixAtSource, "hannah", 7, "Map Civic's CAB and legal statuses to stages in the export."),
            ("unmatched-people", FindingDisposition.FixAtSource, "emma", 3, "Create the contractor's profile and link their time."),
            ("time-not-linked", FindingDisposition.FixAtSource, "lucy", 7, "Book support time to the support allowance item."),
            ("billability-unknown", FindingDisposition.AcceptAndState, null, null,
                "Civic governance hours are under discussion with the client; reported as unknown until agreed."),
            ("over-estimate", FindingDisposition.Disputed, null, null,
                "Harbour's overrun is scope the client added; the change request is with their commercial lead.")
        };
        foreach (var d in decisions.Where(d => review.Findings.Any(f => f.FindingKey == d.FindingKey)))
        {
            var result = await reviews.DecideAsync(Tenant, review.ReviewKey, d.FindingKey, d.Decision,
                d.Owner is null ? null : staff[d.Owner], d.DueInDays is null ? null : today.AddDays(d.DueInDays.Value), d.Note, recordedBy);
            if (result.Status != EvidenceDecisionStatus.Saved)
                throw new InvalidOperationException($"Decision on {d.FindingKey} was refused: {result.Error}");
        }
    }

    /// <summary>The same detection a sync runs after it publishes.</summary>
    private async Task RaiseAlertsAsync()
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAlertDetectionService>().DetectForTenantAsync(Tenant, DateTime.UtcNow);
    }

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }

    // ---- Portfolio -------------------------------------------------------------

    /// <summary>Customers, budgets and baselines; returns project name → key.</summary>
    private async Task<Dictionary<string, Guid>> SeedPortfolioAsync(IReadOnlyDictionary<string, Guid> staff, DateOnly today)
    {
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProgrammeRepository>();
        var now = DateTime.UtcNow;

        var programmes = (await repository.GetProgrammesAsync(Tenant)).ToDictionary(p => p.Name);
        foreach (var customer in Customers)
        {
            await repository.UpsertCustomerAsync(new Customer
            {
                CustomerKey = customer.Key, TenantId = Tenant, Name = customer.Name, CreatedAtUtc = now, UpdatedAtUtc = now
            }, Tenant);
            var programme = programmes[customer.Programme];
            await repository.AssignProgrammeToCustomerAsync(programme.ProgrammeKey, customer.Key, Tenant);
            await repository.SetProgrammeBudgetAsync(programme.ProgrammeKey, customer.Budget, "GBP", Tenant);
        }

        var projects = (await repository.GetProjectsAsync(Tenant)).ToDictionary(p => p.Name, p => p.ProjectKey);

        // Baselines are the estimates as they stood when each workstream was
        // planned, so later scope and overrun show against them.
        var baselined = (await repository.GetWorkstreamBaselinesAsync(Tenant)).Select(b => b.WorkstreamKey).ToHashSet();
        var lockedAt = today.AddDays(-70).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);
        foreach (var workstream in await repository.GetWorkstreamsAsync(Tenant))
        {
            var project = projects.First(p => p.Value == workstream.ProjectKey).Key;
            var items = WorkItems.Where(w => w.Project == project && w.Workstream == workstream.Name && w.Estimate is not null).ToList();
            if (baselined.Contains(workstream.WorkstreamKey) || items.Count == 0
                || items[0].Programme is not (Harbour or Kestrel) || workstream.Name == "Delivery")
                continue;
            var owner = items[0].Programme == Harbour ? staff["sarah"] : staff["daniel"];
            await repository.LockBaselineAsync(workstream.WorkstreamKey, items.Sum(w => w.Estimate!.Value), owner, lockedAt, Tenant);
        }

        return projects;
    }

    private async Task<int> SeedBookingsAsync(IReadOnlyDictionary<string, Guid> staff, IReadOnlyDictionary<string, Guid> projects, DateOnly today)
    {
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProgrammeRepository>();
        var now = DateTime.UtcNow;
        var count = 0;

        foreach (var booking in Bookings)
        {
            var resourceId = booking.Person ?? "agency-devops";
            for (var week = booking.FromWeek; week <= booking.ToWeek; week++)
            {
                var monday = WeekStart(today, week);
                await repository.UpsertPlannedAllocationAsync(new PlannedAllocation
                {
                    PlannedAllocationKey = Guid.NewGuid(), TenantId = Tenant, ProjectKey = projects[booking.Project],
                    StaffKey = booking.Person is { } key && staff.TryGetValue(key, out var staffKey) ? staffKey : null,
                    Title = $"{booking.ResourceName}: {booking.Project}",
                    StartUtc = monday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    EndUtc = monday.AddDays(4).ToDateTime(new TimeOnly(23, 59), DateTimeKind.Utc),
                    AllocatedHours = booking.HoursPerWeek, RawType = "Booking",
                    ExternalSource = BookingSource,
                    ExternalId = string.Create(CultureInfo.InvariantCulture, $"{resourceId}:{booking.Project}:{monday:yyyy-MM-dd}"),
                    ExternalResourceId = resourceId, CreatedAtUtc = now, UpdatedAtUtc = now
                }, Tenant);
                count++;
            }
        }

        return count;
    }

    private async Task SeedRaidAsync(IReadOnlyDictionary<string, Guid> staff, IReadOnlyDictionary<string, Guid> projects, DateOnly today)
    {
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProgrammeRepository>();
        var raised = today.AddDays(-35).ToDateTime(new TimeOnly(11, 0), DateTimeKind.Utc);

        foreach (var entry in Raid)
        {
            if (entry.IsRisk)
            {
                await repository.UpsertRiskAsync(new Risk
                {
                    RiskKey = entry.Key, TenantId = Tenant, ProjectKey = projects[entry.Project], Title = entry.Title,
                    Description = entry.Description, Severity = entry.Severity, Status = (RiskStatus)entry.Status,
                    CreatedAtUtc = raised, UpdatedAtUtc = raised.AddDays(20)
                }, Tenant);
            }
            else
            {
                await repository.UpsertIssueAsync(new Issue
                {
                    IssueKey = entry.Key, TenantId = Tenant, ProjectKey = projects[entry.Project], Title = entry.Title,
                    Description = entry.Description, Severity = entry.Severity, Status = (IssueStatus)entry.Status,
                    CreatedAtUtc = raised, UpdatedAtUtc = raised.AddDays(20)
                }, Tenant);
            }
        }

        var existingChanges = (await repository.GetChangeRequestsAsync(Tenant)).Select(c => c.Title).ToHashSet();
        foreach (var change in ChangeRequests.Where(c => !existingChanges.Contains(c.Title)))
        {
            var created = await repository.CreateChangeRequestAsync(new ChangeRequest
            {
                ChangeRequestKey = change.Key, TenantId = Tenant, ProjectKey = projects[change.Project], Title = change.Title,
                Description = change.Description, Status = ChangeRequestStatus.Proposed, RequestedByStaffKey = staff[change.RequestedBy],
                CreatedAtUtc = raised, UpdatedAtUtc = raised
            }, Tenant);
            if (change.Status != ChangeRequestStatus.Proposed)
                await repository.DecideChangeRequestAsync(created.ChangeRequestKey, change.Status, staff[change.DecidedBy!], raised.AddDays(6), Tenant);
        }

        var programmes = (await repository.GetProgrammesAsync(Tenant)).ToDictionary(p => p.Name, p => p.ProgrammeKey);
        var withStakeholders = (await repository.GetStakeholdersAsync(Tenant)).Select(s => s.ProgrammeKey).ToHashSet();
        (string Programme, string? Staff, string? External, StakeholderRole Role)[] stakeholders =
        [
            (Harbour, null, "Dr Rachel Moore, Chief Information Officer, Harbour Health", StakeholderRole.Sponsor),
            (Harbour, "sarah", null, StakeholderRole.Accountable),
            (Harbour, "marcus", null, StakeholderRole.Responsible),
            (Kestrel, null, "Mike Hanlon, Chief Operating Officer, Kestrel Insurance", StakeholderRole.Sponsor),
            (Kestrel, "daniel", null, StakeholderRole.Accountable),
            (Civic, "hannah", null, StakeholderRole.Accountable),
            (Civic, null, "Service desk manager, Civic Transport", StakeholderRole.Informed)
        ];
        foreach (var stakeholder in stakeholders.Where(s => !withStakeholders.Contains(programmes[s.Programme])))
        {
            await repository.AddStakeholderAsync(new ProgrammeStakeholder
            {
                ProgrammeStakeholderKey = Guid.NewGuid(), TenantId = Tenant, ProgrammeKey = programmes[stakeholder.Programme],
                StaffKey = stakeholder.Staff is null ? null : staff[stakeholder.Staff], ExternalName = stakeholder.External,
                Role = stakeholder.Role, CreatedAtUtc = raised
            }, Tenant);
        }
    }

    // ---- Commercial ------------------------------------------------------------

    private async Task<int> SeedContractsAsync(IReadOnlyDictionary<string, Guid> staff, DateOnly today)
    {
        using var scope = services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IContractAdminService>();
        var repository = scope.ServiceProvider.GetRequiredService<IContractRepository>();
        var obligations = scope.ServiceProvider.GetRequiredService<IContractObligationRepository>();
        var customers = Customers.ToDictionary(c => c.Name, c => c.Key);

        foreach (var contract in Contracts)
        {
            var existing = (await repository.GetContractsAsync(Tenant)).FirstOrDefault(c => c.Reference == contract.Reference);
            var key = existing?.ContractKey;
            if (key is null)
            {
                var (outcome, created) = await admin.CreateAsync(Tenant, new ContractInput(
                    customers[contract.Customer], contract.Reference, contract.Model, contract.TotalValue, contract.AnnualValue,
                    contract.BillRate, "GBP", today.AddDays(-contract.StartDaysAgo), today.AddDays(contract.EndInDays)), actorMemberId: null);
                key = created ?? throw new InvalidOperationException($"Contract {contract.Reference} was refused: {outcome.Error}");
                await admin.UpdateStatusAsync(Tenant, key.Value, contract.Status, contract.Notes, actorMemberId: null);
            }

            if ((await repository.GetNonLabourCostsAsync(key.Value, Tenant)).Count == 0)
            {
                foreach (var cost in NonLabourCosts.Where(c => c.Contract == contract.Reference))
                    await admin.AddNonLabourCostAsync(Tenant, key.Value, cost.Description, cost.Amount, "GBP", today.AddDays(-cost.DaysAgo), staff["emma"], null);
            }
        }

        var contracts = (await repository.GetContractsAsync(Tenant)).ToDictionary(c => c.Reference, c => c.ContractKey);
        var harbour = contracts["HHT-FP-2026-01"];
        if ((await obligations.GetLiveForContractAsync(harbour, Tenant)).Count == 0)
        {
            await obligations.AddAsync(new ContractObligation
            {
                ObligationKey = Guid.NewGuid(), TenantId = Tenant, ContractKey = harbour, Kind = ObligationKind.SecurityFindingGate,
                ClauseReference = "Schedule 4, 12.3", Description = "No release to production with an open High or Critical security finding.",
                SeverityThreshold = FindingSeverity.High, Action = GateAction.Block, RecordedByStaffKey = staff["emma"], RecordedAtUtc = DateTime.UtcNow
            });
            await obligations.AddAsync(new ContractObligation
            {
                ObligationKey = Guid.NewGuid(), TenantId = Tenant, ContractKey = harbour, Kind = ObligationKind.DataResidency,
                ClauseReference = "Schedule 6, 3.1", Description = "Patient data stored and processed in the UK only.",
                RecordedByStaffKey = staff["emma"], RecordedAtUtc = DateTime.UtcNow
            });
        }

        // Last month's invoices: Kestrel's issued, Civic's still a draft
        // with governance hours nobody has said are billable.
        var lastMonthStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var lastMonthEnd = lastMonthStart.AddMonths(1).AddDays(-1);
        foreach (var (reference, issue) in new[] { ("KES-TM-2026-04", true), ("CTA-RET-2026-02", false) })
        {
            var key = contracts[reference];
            if ((await repository.GetInvoicesAsync(key, Tenant)).Count > 0)
                continue;
            var error = await admin.GenerateInvoiceAsync(Tenant, key, lastMonthStart, lastMonthEnd, staff["emma"], null);
            if (error is not null)
                throw new InvalidOperationException($"Invoice for {reference} was refused: {error}");
            if (issue)
            {
                var invoice = (await repository.GetInvoicesAsync(key, Tenant)).Single();
                await admin.ChangeInvoiceStatusAsync(Tenant, key, invoice.InvoiceKey, InvoiceStatus.Issued, needsReviewHoursConfirmed: true, null);
            }
        }

        return contracts.Count;
    }

    // ---- Skills and continuity -------------------------------------------------

    private async Task<int> SeedSkillsAsync(IReadOnlyDictionary<string, Guid> staff, DateOnly today)
    {
        using var scope = services.CreateScope();
        var assertions = scope.ServiceProvider.GetRequiredService<ISkillAssertionService>();
        var repository = scope.ServiceProvider.GetRequiredService<ISkillsEvidenceRepository>();

        var existingSkills = (await repository.GetSkillsAsync(Tenant, includeRetired: true)).Select(s => s.SkillKey).ToHashSet();
        foreach (var skill in Skills.Where(s => !existingSkills.Contains(s.Key)))
            await assertions.CreateSkillAsync(skill.Key, skill.Name, skill.Kind, null, Tenant, null);

        var count = 0;
        foreach (var claim in Assertions)
        {
            var subject = staff[claim.Person];
            if ((await repository.GetCurrentForStaffAsync(subject, Tenant)).Any(a => a.SkillKey == claim.Skill))
                continue;

            var declared = await assertions.DeclareAsync(subject, claim.Skill, claim.Level,
                $"Recent work on {Skills.Single(s => s.Key == claim.Skill).Name.ToLowerInvariant()} for Northstar clients.", Tenant, null);
            count++;
            if (claim.State == DemoAssertionState.Submitted)
                continue;

            var reviewer = staff[claim.Reviewer!];
            if (claim.State == DemoAssertionState.Rejected)
            {
                await assertions.RejectAsync(declared.AssertionKey, reviewer, "No client work in this yet; revisit after the landing zone upgrade.", Tenant, null);
                continue;
            }

            var reviewDue = claim.State == DemoAssertionState.Lapsed ? today.AddDays(-12) : (DateOnly?)null;
            var validated = await assertions.ValidateAsync(declared.AssertionKey, reviewer, claim.Level,
                "Seen in delivery and code review this quarter.", reviewDue, Tenant, null);

            if (claim.State == DemoAssertionState.Challenged)
            {
                await assertions.ChallengeAsync(validated.AssertionKey, subject, claim.Level + 1,
                    "I led the offline capture work end to end; I think this is Practitioner.", Tenant, null);
            }
        }

        return count;
    }

    private async Task SeedContinuityAsync(IReadOnlyDictionary<string, Guid> staff)
    {
        using var scope = services.CreateScope();
        var continuity = scope.ServiceProvider.GetRequiredService<IContinuityService>();
        var repository = scope.ServiceProvider.GetRequiredService<IContinuityRepository>();
        var reviewer = staff["daniel"];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var existing = (await repository.GetComponentsAsync(Tenant)).Select(c => c.ComponentKey).ToHashSet();
        foreach (var component in Components.Where(c => !existing.Contains(c.Key)))
        {
            await continuity.DeclareComponentAsync(component.Key, component.Name, component.Description, staff[component.Owner], reviewer, Tenant, null);
            if (component.Backup is not null)
                await continuity.ApproveBackupAsync(component.Key, staff[component.Backup], "Validated cover; has run a release.", reviewer, Tenant, null);
            if (component.Action is { } action)
            {
                await continuity.RaiseActionAsync(component.Key, action, staff[component.ActionOwner!], component.ActionRationale!,
                    null, today.AddDays(component.ActionDueInDays!.Value), reviewer, Tenant, null);
            }
        }
    }

    // ---- A second tenant -------------------------------------------------------

    /// <summary>
    /// Meridian is a trial customer who has uploaded the sales sample and
    /// nothing else: the platform console has two organisations to show, and
    /// every Northstar page is a check that Meridian's data stays out of it.
    /// </summary>
    private async Task SeedMeridianAsync(DateOnly today)
    {
        using var scope = services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IStaffAdminService>();
        var repository = scope.ServiceProvider.GetRequiredService<IStaffRepository>();

        if ((await repository.GetByTenantAsync(MeridianTenant)).Count == 0)
        {
            foreach (var (name, email, role, title) in new[]
            {
                ("Rhys Morgan", "rhys.morgan@meridian.test", StaffRole.TeamLead, "Delivery Lead"),
                ("Ana Silva", "ana.silva@meridian.test", StaffRole.Staff, "Consultant")
            })
            {
                var result = await admin.OnboardAsync(MeridianTenant,
                    new StaffOnboardingRequest(name, email, NorthstarPersonaSeeder.Password, role, title, "Consulting", "Meridian", 37.5m), null);
                if (!result.Succeeded)
                    throw new InvalidOperationException($"Could not onboard {name}: {string.Join("; ", result.Errors)}");
            }
        }

        await ImportAsync(MeridianTenant, DeliveryExportKind.WorkItems, DeliveryExportSample.WorkItemsCsv(today));
        await ImportAsync(MeridianTenant, DeliveryExportKind.TimeEntries, DeliveryExportSample.TimeEntriesCsv(today));
    }
}
