using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Reporting;

namespace ProgrammePulse.Tests.ProgrammeOps;

public sealed class PortfolioHealthTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private static (Programme Programme, Project Project, Workstream Stream, WorkItem Item) Seed(FakeProgrammeRepository repo, string name, Guid? tenant = null)
    {
        var tenantId = tenant ?? Tenant;
        var customer = new Customer { CustomerKey = Guid.NewGuid(), Name = name + " customer", TenantId = tenantId, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var programme = new Programme { ProgrammeKey = Guid.NewGuid(), CustomerKey = customer.CustomerKey, Name = name, TenantId = tenantId, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var project = new Project { ProjectKey = Guid.NewGuid(), ProgrammeKey = programme.ProgrammeKey, Name = name + " project", TenantId = tenantId, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var stream = new Workstream { WorkstreamKey = Guid.NewGuid(), ProjectKey = project.ProjectKey, Name = name + " stream", TenantId = tenantId, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var item = new WorkItem { WorkItemKey = Guid.NewGuid(), WorkstreamKey = stream.WorkstreamKey, Title = name + " milestone", IsMilestone = true, Stage = WorkItemLifecycleStage.Blocked, DueDateUtc = Now.AddDays(-1), TenantId = tenantId, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        repo.Customers.Add(customer); repo.Programmes.Add(programme); repo.Projects.Add(project); repo.Workstreams.Add(stream); repo.WorkItems.Add(item);
        return (programme, project, stream, item);
    }

    private static ProgrammeOverviewQueryService Query(FakeProgrammeRepository repo) => new(repo, new FakeStaffRepository(), new Clock());

    [Fact]
    public async Task Programme_scope_applies_to_health_KPIs_milestones_and_planned_allocations()
    {
        var repo = new FakeProgrammeRepository();
        var first = Seed(repo, "First"); var second = Seed(repo, "Second");
        Seed(repo, "Foreign", Guid.NewGuid());
        foreach (var project in new[] { first.Project, second.Project })
            repo.PlannedAllocations.Add(new PlannedAllocation { PlannedAllocationKey = Guid.NewGuid(), ProjectKey = project.ProjectKey, Title = "Booking", StartUtc = Now, EndUtc = Now.AddDays(1), AllocatedHours = 8, TenantId = Tenant, CreatedAtUtc = Now, UpdatedAtUtc = Now });

        var result = await Query(repo).BuildOverviewAsync(Tenant, first.Programme.ProgrammeKey, null);

        Assert.True(result.Scope.IsValid);
        Assert.Equal(first.Programme.ProgrammeKey, Assert.Single(result.ProgrammeHealth).ProgrammeKey);
        Assert.Equal(first.Stream.Name, Assert.Single(result.WorkstreamStatuses).WorkstreamName);
        Assert.Equal(first.Item.Title, Assert.Single(result.Milestones).Title);
        Assert.Equal(1, result.PlannedCoverage.TotalInWindow);
        Assert.DoesNotContain(result.Scope.Programmes, p => p.Name == "Foreign");
    }

    [Fact]
    public async Task Foreign_unknown_and_mismatched_scope_never_fall_back_to_the_portfolio()
    {
        var repo = new FakeProgrammeRepository();
        var first = Seed(repo, "First"); var second = Seed(repo, "Second"); var foreign = Seed(repo, "Foreign", Guid.NewGuid());
        foreach (var pair in new[] { (foreign.Programme.ProgrammeKey, (Guid?)null), (Guid.NewGuid(), (Guid?)null), (first.Programme.ProgrammeKey, second.Programme.CustomerKey) })
        {
            var result = await Query(repo).BuildOverviewAsync(Tenant, pair.Item1, pair.Item2);
            Assert.False(result.Scope.IsValid);
            Assert.Empty(result.ProgrammeHealth);
            Assert.Empty(result.WorkstreamStatuses);
        }
        var controller = new StaffProgrammeOverviewController(null!, null!, Query(repo), null!, null!, null!, null!, new Clock());
        Assert.IsType<NotFoundResult>(await controller.Index(Tenant, programmeKey: foreign.Programme.ProgrammeKey));
        Assert.IsType<NotFoundResult>(await controller.Export(Tenant, programmeKey: foreign.Programme.ProgrammeKey));
        controller.ModelState.AddModelError("programmeKey", "Malformed key");
        Assert.IsType<BadRequestObjectResult>(await controller.Index(Tenant));
    }

    [Fact]
    public async Task Health_counts_active_governance_records_and_keeps_cross_programme_dependencies()
    {
        var repo = new FakeProgrammeRepository(); var first = Seed(repo, "First"); var second = Seed(repo, "Second");
        foreach (var status in new[] { RiskStatus.Open, RiskStatus.Mitigating, RiskStatus.Closed })
            repo.Risks.Add(new Risk { RiskKey = Guid.NewGuid(), ProjectKey = first.Project.ProjectKey, Title = "Risk", Severity = SeverityLevel.High, Status = status, TenantId = Tenant, CreatedAtUtc = Now, UpdatedAtUtc = Now });
        foreach (var status in new[] { IssueStatus.Open, IssueStatus.Resolved })
            repo.Issues.Add(new Issue { IssueKey = Guid.NewGuid(), ProjectKey = first.Project.ProjectKey, Title = "Issue", Severity = SeverityLevel.Medium, Status = status, TenantId = Tenant, CreatedAtUtc = Now, UpdatedAtUtc = Now });
        repo.ChangeRequests.Add(new ChangeRequest { ChangeRequestKey = Guid.NewGuid(), ProjectKey = first.Project.ProjectKey, Title = "Change", Status = ChangeRequestStatus.Proposed, TenantId = Tenant, CreatedAtUtc = Now, UpdatedAtUtc = Now });
        repo.Dependencies.Add(new Dependency { DependencyKey = Guid.NewGuid(), WorkItemKey = first.Item.WorkItemKey, DependsOnWorkItemKey = second.Item.WorkItemKey, TenantId = Tenant, CreatedAtUtc = Now });

        var row = Assert.Single((await Query(repo).BuildOverviewAsync(Tenant, first.Programme.ProgrammeKey, null)).ProgrammeHealth);
        Assert.Equal(2, row.OpenRisks); Assert.Equal(2, row.HighRisks); Assert.Equal(1, row.OpenIssues);
        Assert.Equal(1, row.ProposedChanges); Assert.Equal(1, row.UnresolvedDependencies);
        Assert.Equal(1, row.MissingEstimates); Assert.Equal(1, row.WorkstreamsWithoutBaseline);
        Assert.Equal("Review required", row.ReviewSignal);
        Assert.Contains("No resolved accountable owner", row.ReviewReasons);
        Assert.Equal(Now.AddDays(-1), row.NextMilestoneDueUtc);

        var governance = await new GovernanceService(repo, new FakeStaffRepository(), new Clock()).BuildAsync(Tenant, first.Programme.ProgrammeKey);
        Assert.Single(governance.Dependencies);
        Assert.Equal(second.Item.Title, governance.Dependencies[0].DependsOnWorkItemTitle);
    }

    [Fact]
    public async Task Empty_or_incomplete_evidence_is_not_presented_as_green()
    {
        var repo = new FakeProgrammeRepository(); var first = Seed(repo, "Empty"); repo.WorkItems.Clear();
        var row = Assert.Single((await Query(repo).BuildOverviewAsync(Tenant)).ProgrammeHealth);
        Assert.Equal("Evidence incomplete", row.ReviewSignal);
        Assert.Contains("No delivery work items", row.ReviewReasons);
        Assert.Null(row.NextMilestoneDueUtc);
    }

    [Fact]
    public async Task Cancelled_work_is_not_done_and_undated_milestones_remain_unknown()
    {
        var repo = new FakeProgrammeRepository(); var first = Seed(repo, "First");
        repo.WorkItems[0] = first.Item with { Stage = WorkItemLifecycleStage.Cancelled };
        repo.WorkItems.Add(first.Item with { WorkItemKey = Guid.NewGuid(), Stage = WorkItemLifecycleStage.Unmapped, DueDateUtc = null });
        var row = Assert.Single((await Query(repo).BuildOverviewAsync(Tenant)).ProgrammeHealth);
        Assert.Equal(0, row.DoneItems); Assert.Equal(1, row.CancelledItems); Assert.Equal(2, row.TotalItems);
        Assert.Equal(1, row.UndatedMilestones); Assert.Equal(1, row.UnmappedStatuses); Assert.Null(row.NextMilestoneDueUtc);
    }

    [Fact]
    public async Task Registers_and_forms_use_customer_scope_and_reject_cross_programme_writes()
    {
        var repo = new FakeProgrammeRepository(); var first = Seed(repo, "First"); var second = Seed(repo, "Second");
        var raid = new RaidService(repo, new Clock());
        var governance = new GovernanceService(repo, new FakeStaffRepository(), new Clock());
        Assert.Equal(first.Project.ProjectKey, Assert.Single((await raid.BuildAsync(Tenant, customerKey: first.Programme.CustomerKey)).ProjectOptions).ProjectKey);
        Assert.Equal(first.Programme.ProgrammeKey, Assert.Single((await governance.BuildAsync(Tenant, customerKey: first.Programme.CustomerKey)).ProgrammeOptions).ProgrammeKey);
        var controller = new StaffRaidController(raid, null!);
        Assert.IsType<NotFoundResult>(await controller.CreateRisk(Tenant, second.Project.ProjectKey, "Wrong scope", null, SeverityLevel.High, first.Programme.ProgrammeKey));
        Assert.Empty(repo.Risks);
        var accepted = Assert.IsType<RedirectToActionResult>(await controller.CreateRisk(Tenant, first.Project.ProjectKey, "In scope", null, SeverityLevel.High, first.Programme.ProgrammeKey));
        Assert.Single(repo.Risks);
        Assert.Equal(first.Programme.ProgrammeKey, accepted.RouteValues!["programmeKey"]);
        var governanceController = new StaffGovernanceController(null!, governance, null!);
        Assert.IsType<NotFoundResult>(await governanceController.CreateChangeRequest(Tenant, second.Project.ProjectKey, "Wrong scope", null, first.Programme.ProgrammeKey));
        Assert.Empty(repo.ChangeRequests);
        Assert.False(await governance.LockBaselineAsync(Tenant, second.Stream.WorkstreamKey, 20, null, first.Programme.ProgrammeKey));
        Assert.Empty(repo.WorkstreamBaselines);
    }

    [Fact]
    public async Task Export_preserves_scope_health_evidence_and_formula_safety()
    {
        var repo = new FakeProgrammeRepository(); var first = Seed(repo, "=danger"); Seed(repo, "Excluded");
        var overview = await Query(repo).BuildOverviewAsync(Tenant, first.Programme.ProgrammeKey, null);
        var csv = ProgrammeOverviewCsv.Build(new ProgrammeOverviewPageViewModel(overview, [], 0, false, [], null, null), "Tenant", Now);
        Assert.Contains(first.Programme.ProgrammeKey.ToString(), csv);
        Assert.Contains("Review required", csv);
        Assert.Contains("'=danger", csv);
        Assert.DoesNotContain("Excluded", csv);
        Assert.Contains("Source freshness and unmatched-person counts are organisation-wide", csv);
        Assert.DoesNotContain(typeof(ProgrammeHealthRow).GetProperties(), p => p.Name.Contains("Cost") || p.Name.Contains("Revenue") || p.Name.Contains("Margin"));
    }
}
