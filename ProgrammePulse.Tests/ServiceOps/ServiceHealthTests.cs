using ProgrammePulse.Models.Integrations.Freshdesk.Raw;
using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ServiceOps;
using static ProgrammePulse.Tests.ServiceOps.FakeFreshdeskClient;

namespace ProgrammePulse.Tests.ServiceOps;

/// <summary>
/// The service view: demand, recurrence, resolution, and the caveats
/// that stop it being over-read.
///
/// The design document asks for uncertainty to be as prominent as
/// results, and for comparisons to use like periods, severity and
/// exposure. Most of these tests are about the honesty rather than the
/// arithmetic.
/// </summary>
public class ServiceHealthTests
{
    private static readonly DateTime Sept1 = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Demand_is_grouped_by_approved_component()
    {
        var ctx = new ServiceOpsTestContext("billing", "sync");
        ctx.Client.WithTickets(new FreshdeskPage(
        [
            Ticket("1", Sept1, component: "billing"),
            Ticket("2", Sept1, component: "billing"),
            Ticket("3", Sept1, component: "sync")
        ], Sept1, true));
        await ctx.RunAsync();

        var report = await ctx.ReportAsync();

        Assert.Equal(2, report.Components.Count);
        Assert.Equal(2, report.Components.Single(c => c.ComponentKey == "billing").CasesOpened);
        Assert.Equal(1, report.Components.Single(c => c.ComponentKey == "sync").CasesOpened);
        Assert.Equal(3, report.TotalCasesOpened);
    }

    [Fact]
    public async Task Cases_with_no_approved_component_get_their_own_row_and_are_counted_separately()
    {
        var ctx = new ServiceOpsTestContext("billing");
        ctx.Client.WithTickets(new FreshdeskPage(
        [
            Ticket("1", Sept1, component: "billing"),
            Ticket("2", Sept1, component: "mystery-tag"),
            Ticket("3", Sept1, component: null)
        ], Sept1, true));
        await ctx.RunAsync();

        var report = await ctx.ReportAsync();

        Assert.Equal(2, report.CasesWithUnmappedComponent);
        var unmapped = report.Components.Single(c => c.IsUnmappedComponent);
        Assert.Equal(2, unmapped.CasesOpened);
        // Sorted last: it is a data-quality bucket, not a component, and
        // ranking it among real ones invites reading it as one.
        Assert.Same(unmapped, report.Components[^1]);
    }

    [Fact]
    public async Task Withdrawn_cases_are_excluded_from_every_figure_but_disclosed()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));
        ctx.Client.WithWithdrawn(new FreshdeskPage([Ticket("9", Sept1, deleted: true)], Sept1, true));
        await ctx.RunAsync();

        var report = await ctx.ReportAsync();

        Assert.Equal(1, report.TotalCasesOpened);
        Assert.Equal(1, report.WithdrawnExcluded);
    }

    [Fact]
    public async Task Reopen_rate_is_withheld_below_the_floor_and_reported_above_it()
    {
        var ctx = new ServiceOpsTestContext();

        var tickets = new List<FreshdeskTicket>();
        for (var i = 1; i <= 10; i++)
        {
            tickets.Add(Ticket(
                i.ToString(), Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(2),
                reopenedAtUtc: i <= 2 ? Sept1.AddDays(1) : null));
        }

        ctx.Client.WithTickets(new FreshdeskPage(tickets, Sept1, true));
        await ctx.RunAsync();

        var row = (await ctx.ReportAsync()).Components.Single();
        Assert.Equal(10, row.CasesResolved);
        Assert.Equal(2, row.Reopened);
        Assert.Equal(0.2, row.ReopenRate);
    }

    [Fact]
    public async Task A_rate_from_two_cases_is_not_reported_at_all()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
        [
            Ticket("1", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(1), reopenedAtUtc: Sept1.AddDays(1)),
            Ticket("2", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(1))
        ], Sept1, true));
        await ctx.RunAsync();

        // "50% reopen rate" from two cases looks precise and means
        // nothing, and somebody would put it in a board pack.
        Assert.Null((await ctx.ReportAsync()).Components.Single().ReopenRate);
    }

    [Fact]
    public async Task Time_to_restore_is_a_median_measured_to_resolution_not_closure()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
        [
            Ticket("1", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(1)),
            Ticket("2", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(3)),
            // A three-week outlier that would badly skew a mean.
            Ticket("3", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddDays(21))
        ], Sept1, true));
        await ctx.RunAsync();

        var row = (await ctx.ReportAsync()).Components.Single();
        Assert.Equal(TimeSpan.FromHours(3), row.MedianTimeToRestore);
    }

    [Fact]
    public async Task High_and_urgent_cases_are_counted_so_severity_can_be_compared()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
        [
            Ticket("1", Sept1, priority: 1),
            Ticket("2", Sept1, priority: 3),
            Ticket("3", Sept1, priority: 4)
        ], Sept1, true));
        await ctx.RunAsync();

        Assert.Equal(2, (await ctx.ReportAsync()).Components.Single().HighOrUrgent);
    }

    [Fact]
    public async Task Source_link_coverage_shows_how_much_of_the_demand_is_explained()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
        [
            Ticket("1", Sept1, issueKeys: "PROJ-1"),
            Ticket("2", Sept1),
            Ticket("3", Sept1),
            Ticket("4", Sept1)
        ], Sept1, true));
        await ctx.RunAsync();

        var report = await ctx.ReportAsync();

        // A service view built on connected Git can look authoritative
        // while explaining a small fraction of the cases.
        Assert.Equal(0.25, report.SourceLinkCoverage);
        Assert.Equal(3, report.Components.Single().UnlinkedToSource);
    }

    [Fact]
    public async Task Only_a_reviewed_root_cause_counts_as_a_confirmed_code_cause()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
            [Ticket("1", Sept1, issueKeys: "PROJ-1"), Ticket("2", Sept1, issueKeys: "PROJ-2")], Sept1, true));
        await ctx.RunAsync();

        var before = (await ctx.ReportAsync()).Components.Single();
        Assert.Equal(2, before.LinkedToSource);
        // Two key matches, zero causes. That is the whole distinction.
        Assert.Equal(0, before.ConfirmedCodeCauses);

        var link = ctx.Repository.Links.First(l => l.ExternalTicketId == "1");
        await ctx.LinkService.ConfirmRootCauseAsync(
            link.LinkKey, ctx.Sarah.StaffKey, "confirmed at the incident review", ServiceOpsTestContext.TenantA, 1);

        var after = (await ctx.ReportAsync()).Components.Single();
        Assert.Equal(1, after.ConfirmedCodeCauses);
    }

    [Fact]
    public async Task A_previous_period_of_the_same_length_is_offered_for_comparison()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
        [
            Ticket("1", new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc)),
            Ticket("2", new DateTime(2026, 9, 5, 9, 0, 0, DateTimeKind.Utc)),
            Ticket("3", new DateTime(2026, 9, 6, 9, 0, 0, DateTimeKind.Utc))
        ], Sept1, true));
        await ctx.RunAsync();

        var report = await ctx.Health.BuildAsync(
            ServiceOpsTestContext.TenantA, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1));

        Assert.Equal(2, report.TotalCasesOpened);
        Assert.NotNull(report.PreviousPeriod);
        Assert.Equal(1, report.PreviousPeriod!.Sum(r => r.CasesOpened));
    }

    [Fact]
    public async Task An_empty_previous_period_is_not_offered_rather_than_reading_as_a_doubling()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));
        await ctx.RunAsync();

        var report = await ctx.Health.BuildAsync(
            ServiceOpsTestContext.TenantA, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1));

        Assert.Null(report.PreviousPeriod);
    }

    [Fact]
    public async Task An_empty_window_with_incomplete_coverage_reads_as_inconclusive()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([], null, IsComplete: false, IncompleteReason: "rate limited"));
        await ctx.RunAsync();

        var report = await ctx.ReportAsync();

        Assert.Equal(0, report.TotalCasesOpened);
        Assert.True(report.AbsenceIsInconclusive);
        Assert.False(report.IsComplete);
    }

    [Fact]
    public async Task Unverified_replay_is_flagged_until_a_run_has_actually_succeeded()
    {
        var ctx = new ServiceOpsTestContext();

        Assert.True((await ctx.ReportAsync()).IsUnverifiedReplay);

        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));
        await ctx.RunAsync();

        Assert.False((await ctx.ReportAsync()).IsUnverifiedReplay);
    }

    [Fact]
    public async Task Unidentified_agents_are_surfaced_on_the_report()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
        [
            Ticket("1", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(1), responderId: "77"),
            Ticket("2", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(1), responderId: "88")
        ], Sept1, true));
        await ctx.RunAsync();

        Assert.Equal(2, (await ctx.ReportAsync()).UnmappedAgents);
    }

    [Fact]
    public async Task The_report_never_includes_another_tenants_cases()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));
        await ctx.RunAsync(ctx.DeskA);
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("2", Sept1), Ticket("3", Sept1)], Sept1, true));
        await ctx.RunAsync(ctx.DeskB);

        Assert.Equal(1, (await ctx.ReportAsync(ServiceOpsTestContext.TenantA)).TotalCasesOpened);
        Assert.Equal(2, (await ctx.ReportAsync(ServiceOpsTestContext.TenantB)).TotalCasesOpened);
    }

    [Fact]
    public async Task A_period_that_ends_before_it_starts_is_refused()
    {
        var ctx = new ServiceOpsTestContext();

        await Assert.ThrowsAsync<ServiceOpsValidationException>(() => ctx.Health.BuildAsync(
            ServiceOpsTestContext.TenantA, new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1)));
    }

    // ---- Host policy ----

    [Theory]
    [InlineData("acme", "https://acme.freshdesk.com/api/v2")]
    [InlineData("ACME", "https://acme.freshdesk.com/api/v2")]
    [InlineData("acme.freshdesk.com", "https://acme.freshdesk.com/api/v2")]
    [InlineData("https://acme.freshdesk.com", "https://acme.freshdesk.com/api/v2")]
    public void A_freshdesk_account_canonicalises_to_the_vendors_own_host(string input, string expected) =>
        Assert.Equal(expected, DeskHostPolicy.CanonicalizeFreshdesk(input));

    [Theory]
    [InlineData("http://acme.freshdesk.com")]              // not HTTPS
    [InlineData("https://acme.evil.test")]                 // another host entirely
    [InlineData("https://acme.freshdesk.com.evil.test")]   // suffix trick
    [InlineData("https://user:pass@acme.freshdesk.com")]   // credentials in the URL
    [InlineData("https://acme.freshdesk.com:8443")]        // non-default port
    [InlineData("acme..freshdesk.com")]
    [InlineData("-acme")]
    [InlineData("acme/../evil")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_refused_rather_than_defaulted(string? input) =>
        Assert.Null(DeskHostPolicy.CanonicalizeFreshdesk(input));

    [Theory]
    [InlineData("billing", true)]
    [InlineData("sync engine", true)]
    [InlineData("api/v2", true)]
    [InlineData("drop table", true)]
    [InlineData("<script>", false)]
    [InlineData("a;b", false)]
    [InlineData("", false)]
    public void A_component_key_is_a_narrow_label(string candidate, bool expected) =>
        Assert.Equal(expected, DeskHostPolicy.IsValidComponentKey(candidate));

    // ---- Access ----

    [Fact]
    public void Service_health_is_a_wider_grant_than_the_skills_capabilities()
    {
        // The deliberate difference: a skills coverage figure is derived
        // from employees' personal records; a component's support demand
        // is about a product. An analyst reporting on service quality
        // needs the latter and should not have to be made a Team Lead.
        foreach (var role in new[] { StaffRole.Analyst, StaffRole.Board, StaffRole.TeamLead, StaffRole.Admin })
        {
            Assert.True(RoleCapabilities.Has([role], Capability.ViewServiceHealth), $"{role} cannot see service health.");
        }

        Assert.False(RoleCapabilities.Has([StaffRole.Analyst], Capability.ViewTeamSkillCoverage));
        Assert.False(RoleCapabilities.Has([StaffRole.Board], Capability.ViewTeamSkillCoverage));
    }

    [Fact]
    public void An_ordinary_employee_sees_neither_service_health_nor_root_cause_review()
    {
        Assert.False(RoleCapabilities.Has([StaffRole.Staff], Capability.ViewServiceHealth));
        Assert.False(RoleCapabilities.Has([StaffRole.Staff], Capability.ReviewRootCause));
    }

    [Fact]
    public void Confirming_a_root_cause_is_narrower_than_reading_the_service_view()
    {
        // Every role that can confirm can also read, but not the reverse
        // — the strongest claim the product makes needs the narrower grant.
        Assert.True(RoleCapabilities.Has([StaffRole.TeamLead], Capability.ReviewRootCause));
        Assert.True(RoleCapabilities.Has([StaffRole.Admin], Capability.ReviewRootCause));
        Assert.False(RoleCapabilities.Has([StaffRole.Analyst], Capability.ReviewRootCause));
        Assert.False(RoleCapabilities.Has([StaffRole.Board], Capability.ReviewRootCause));

        Assert.All(
            RoleCapabilities.RolesGranting(Capability.ReviewRootCause),
            role => Assert.True(RoleCapabilities.Has([role], Capability.ViewServiceHealth)));
    }

    [Fact]
    public void The_platform_operator_holds_neither()
    {
        Assert.False(RoleCapabilities.Has([StaffRole.PlatformAdmin], Capability.ViewServiceHealth));
        Assert.False(RoleCapabilities.Has([StaffRole.PlatformAdmin], Capability.ReviewRootCause));
    }
}
