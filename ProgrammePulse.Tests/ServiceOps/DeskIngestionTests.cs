using ProgrammePulse.Models.Integrations.Freshdesk.Raw;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Services.Integrations.Freshdesk;
using ProgrammePulse.Services.ServiceOps;
using static ProgrammePulse.Tests.ServiceOps.FakeFreshdeskClient;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.ServiceOps;

/// <summary>
/// Desk ingestion: incremental replay, reopens, deletions, partial
/// results, rate limits, unknown component tags and multi-tenant
/// separation — the cases the Slice 3 brief names.
///
/// The watermark tests carry most of the weight. Freshdesk paginates by
/// <c>updated_since</c> rather than an opaque cursor, so the overlap is
/// deliberate and the idempotency that makes it safe has to be proved.
/// </summary>
public class DeskIngestionTests
{
    private static readonly DateTime Sept1 = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_ticket_becomes_a_case_with_its_component_mapped_against_the_approved_list()
    {
        var ctx = new ServiceOpsTestContext("billing");
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1, component: "Billing")], Sept1, true));

        await ctx.RunAsync();

        var fact = Assert.Single(ctx.Repository.Cases);
        Assert.Equal("1", fact.ExternalTicketId);
        Assert.Equal("billing", fact.ComponentKey);
        Assert.Equal("Billing", fact.RawComponentTag);
        Assert.False(fact.HasUnmappedComponent);
    }

    [Fact]
    public async Task A_tag_nobody_approved_leaves_the_component_unmapped_but_still_counts_the_case()
    {
        var ctx = new ServiceOpsTestContext("billing");
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1, component: "whatever-the-agent-typed")], Sept1, true));

        await ctx.RunAsync();

        var fact = Assert.Single(ctx.Repository.Cases);
        Assert.Null(fact.ComponentKey);
        // Preserved, so an admin can see what the desk actually said and
        // decide whether to approve it.
        Assert.Equal("whatever-the-agent-typed", fact.RawComponentTag);
        Assert.True(fact.HasUnmappedComponent);
        Assert.True(fact.CountsTowardsTrends);
    }

    [Fact]
    public async Task An_unrecognised_status_is_treated_as_active_rather_than_dropped()
    {
        var ctx = new ServiceOpsTestContext();
        // Customers add their own statuses. Losing the case would
        // understate demand, which is worse than an imprecise state.
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1, status: 42)], Sept1, true));

        await ctx.RunAsync();

        var fact = Assert.Single(ctx.Repository.Cases);
        Assert.Equal(SupportCaseState.Active, fact.State);
        Assert.Equal("42", fact.ProviderStatus);
    }

    [Fact]
    public async Task Replaying_the_same_ticket_updates_one_case_and_keeps_its_first_seen_stamp()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));
        await ctx.RunAsync();
        var first = ctx.Repository.Cases.Single().FirstIngestedAtUtc;

        ctx.Time.Advance(TimeSpan.FromHours(2));
        ctx.Client.WithTickets(new FreshdeskPage(
            [Ticket("1", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(4))], Sept1.AddHours(4), true));
        await ctx.RunAsync();

        var fact = Assert.Single(ctx.Repository.Cases);
        Assert.Equal(first, fact.FirstIngestedAtUtc);
        Assert.Equal(SupportCaseState.Resolved, fact.State);
    }

    [Fact]
    public async Task The_next_run_resumes_from_the_watermark_minus_a_deliberate_overlap()
    {
        var ctx = new ServiceOpsTestContext();
        var watermark = Sept1.AddHours(3);
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1, updatedAtUtc: watermark)], watermark, true));
        await ctx.RunAsync();

        await ctx.RunAsync();

        // First run starts from nothing; the second rewinds by the
        // overlap, because Freshdesk's filter is second-granular and
        // several tickets can share the boundary second.
        Assert.Equal(2, ctx.Client.ObservedSince.Count);
        Assert.Null(ctx.Client.ObservedSince[0]);
        Assert.Equal(watermark.AddSeconds(-FreshdeskIngestionService.OverlapSeconds), ctx.Client.ObservedSince[1]);
    }

    [Fact]
    public async Task The_overlap_re_reads_tickets_without_duplicating_demand()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1), Ticket("2", Sept1)], Sept1, true));
        await ctx.RunAsync();

        // The overlap window legitimately returns the same tickets again.
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1), Ticket("2", Sept1), Ticket("3", Sept1)], Sept1, true));
        await ctx.RunAsync();

        Assert.Equal(3, ctx.Repository.Cases.Count);
        var report = await ctx.ReportAsync();
        Assert.Equal(3, report.TotalCasesOpened);
    }

    [Fact]
    public async Task A_partial_page_keeps_what_it_fetched_and_does_not_advance_the_coverage_claim()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
            [Ticket("1", Sept1)], Sept1, IsComplete: false, IncompleteReason: "the Freshdesk rate limit was reached"));

        var result = await ctx.RunAsync();

        Assert.False(result.IsComplete);
        Assert.Single(ctx.Repository.Cases);

        var coverage = ctx.Repository.Coverage.Single(c => c.Stream == DeskStream.Tickets);
        Assert.Equal(DeskCoverageStatus.Partial, coverage.Status);
        // The claim does not move: "no cases" must stay distinguishable
        // from "we have not looked".
        Assert.Null(coverage.CompleteThroughUtc);
        Assert.Contains("rate limit", coverage.StatusDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_partial_run_still_advances_the_watermark_so_the_next_run_makes_progress()
    {
        var ctx = new ServiceOpsTestContext();
        var watermark = Sept1.AddHours(6);
        ctx.Client.WithTickets(new FreshdeskPage(
            [Ticket("1", Sept1, updatedAtUtc: watermark)], watermark, IsComplete: false,
            IncompleteReason: "more than 4000 tickets changed since the last run"));

        await ctx.RunAsync();
        await ctx.RunAsync();

        // Otherwise a desk with more history than one run's page budget
        // would re-read the same first pages for ever and never finish.
        Assert.Equal(watermark.AddSeconds(-FreshdeskIngestionService.OverlapSeconds), ctx.Client.ObservedSince[1]);
    }

    [Fact]
    public async Task A_complete_run_advances_the_coverage_claim()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));

        var result = await ctx.RunAsync();

        Assert.True(result.IsComplete);
        var coverage = ctx.Repository.Coverage.Single(c => c.Stream == DeskStream.Tickets);
        Assert.Equal(DeskCoverageStatus.Complete, coverage.Status);
        Assert.Equal(ctx.Time.Now.UtcDateTime, coverage.CompleteThroughUtc);
    }

    [Fact]
    public async Task A_successful_desk_sync_records_a_durable_run_summary()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));

        await ctx.RunAsync();

        var run = ctx.SyncRuns.Single(FreshdeskIngestionService.SourceName);
        Assert.Equal(SyncRunStatus.Succeeded, run.Status);
        Assert.Contains("cases", run.Summary);
    }

    [Fact]
    public async Task A_live_database_lease_on_another_instance_refuses_a_new_desk_sync()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.SyncRuns.Leases[(ServiceOpsTestContext.TenantA, FreshdeskIngestionService.SourceName)] =
            ("other-instance", ServiceOpsTestContext.Start.UtcDateTime.AddMinutes(5), Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ServiceOpsValidationException>(() => ctx.RunAsync());

        Assert.Contains("another instance", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(ctx.RunGuard.IsRunning(ServiceOpsTestContext.TenantA, FreshdeskIngestionService.SourceName));
    }

    [Fact]
    public async Task A_reopened_case_is_recorded_as_reopened()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
            [Ticket("1", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(2), reopenedAtUtc: Sept1.AddDays(1))],
            Sept1, true));

        var result = await ctx.RunAsync();

        Assert.Equal(1, result.Reopened);
        Assert.True(ctx.Repository.Cases.Single().IsReopened);
    }

    [Fact]
    public async Task A_deleted_ticket_is_withdrawn_rather_than_vanishing()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithWithdrawn(new FreshdeskPage([Ticket("9", Sept1, deleted: true)], Sept1, true));

        await ctx.RunAsync();

        var fact = Assert.Single(ctx.Repository.Cases);
        Assert.True(fact.IsWithdrawn);
        Assert.Equal(SupportCaseState.Withdrawn, fact.State);
        // Kept on purpose: a ticket that silently disappeared would
        // change last month's figures retrospectively.
        Assert.False(fact.CountsTowardsTrends);
    }

    [Fact]
    public async Task A_spam_ticket_is_withdrawn_too()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1, spam: true)], Sept1, true));

        await ctx.RunAsync();

        Assert.True(ctx.Repository.Cases.Single().IsWithdrawn);
    }

    [Fact]
    public async Task A_case_deleted_after_it_was_imported_becomes_withdrawn_on_the_next_run()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));
        await ctx.RunAsync();
        Assert.False(ctx.Repository.Cases.Single().IsWithdrawn);

        ctx.Client.WithWithdrawn(new FreshdeskPage([Ticket("1", Sept1, deleted: true)], Sept1, true));
        await ctx.RunAsync();

        Assert.True(ctx.Repository.Cases.Single().IsWithdrawn);
    }

    [Fact]
    public async Task Losing_access_marks_coverage_rather_than_deleting_cases()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));
        await ctx.RunAsync();
        Assert.NotEmpty(ctx.Repository.Cases);

        ctx.Client.AccessLost = true;
        await ctx.RunAsync();

        Assert.NotEmpty(ctx.Repository.Cases);
        Assert.All(ctx.Repository.Coverage, c => Assert.Equal(DeskCoverageStatus.PermissionLost, c.Status));
        Assert.Equal(DeskConnectionStatus.AccessLost,
            ctx.Repository.Connections.Single(c => c.ConnectionKey == ctx.DeskA.ConnectionKey).Status);
    }

    [Fact]
    public async Task A_run_without_a_readable_credential_stops_rather_than_falling_back()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Protector.Readable = false;

        var ex = await Assert.ThrowsAsync<ServiceOpsValidationException>(() => ctx.RunAsync());

        Assert.Contains("no shared fallback", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ctx.Repository.Cases);
    }

    [Fact]
    public async Task A_disconnected_desk_refuses_to_sync()
    {
        var ctx = new ServiceOpsTestContext();
        await ctx.Repository.SetConnectionStatusAsync(
            ctx.DeskA.ConnectionKey, DeskConnectionStatus.Disconnected,
            ServiceOpsTestContext.TenantA, ctx.Time.Now.UtcDateTime, clearCredential: true);

        await Assert.ThrowsAsync<ServiceOpsValidationException>(() => ctx.RunAsync());
    }

    [Fact]
    public async Task Another_tenants_connection_key_reads_as_not_found()
    {
        var ctx = new ServiceOpsTestContext();

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() =>
            ctx.Ingestion.RunAsync(ctx.DeskB.ConnectionKey, ServiceOpsTestContext.TenantA, null));
    }

    [Fact]
    public async Task Two_tenants_with_the_same_ticket_id_keep_their_cases_apart()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));
        await ctx.RunAsync(ctx.DeskA);

        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));
        await ctx.RunAsync(ctx.DeskB);

        Assert.Equal(2, ctx.Repository.Cases.Count);
        Assert.Single(ctx.Repository.Cases, c => c.TenantId == ServiceOpsTestContext.TenantA);
        Assert.Single(ctx.Repository.Cases, c => c.TenantId == ServiceOpsTestContext.TenantB);

        var report = await ctx.ReportAsync(ServiceOpsTestContext.TenantA);
        Assert.Equal(1, report.TotalCasesOpened);
    }

    [Fact]
    public async Task A_resolver_is_recorded_as_a_participant_but_not_attributed_to_anyone()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
            [Ticket("1", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(2), responderId: "77", responderName: "A. Agent")],
            Sept1, true));

        await ctx.RunAsync();

        var participant = Assert.Single(ctx.Repository.Participants);
        Assert.Equal(SupportCaseRole.Resolver, participant.Role);
        Assert.Equal("77", participant.ExternalAgentId);
        // A desk agent id is not a person until an admin says it is.
        Assert.Null(participant.StaffKey);
        Assert.False(participant.IsAttributableToAPerson);
    }

    [Fact]
    public async Task An_assignee_on_an_unresolved_case_has_not_resolved_anything()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
            [Ticket("1", Sept1, status: StatusOpen, responderId: "77")], Sept1, true));

        await ctx.RunAsync();

        Assert.Empty(ctx.Repository.Participants);
    }

    [Fact]
    public async Task Approving_an_agent_attributes_participation_already_ingested()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
            [Ticket("1", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(2), responderId: "77"),
             Ticket("2", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(3), responderId: "77")],
            Sept1, true));
        await ctx.RunAsync();

        await ctx.Repository.CreateAgentLinkAsync(new DeskAgentLink
        {
            LinkKey = Guid.NewGuid(),
            TenantId = ServiceOpsTestContext.TenantA,
            ConnectionKey = ctx.DeskA.ConnectionKey,
            Provider = DeskHostPolicy.FreshdeskProvider,
            ExternalAgentId = "77",
            StaffKey = ctx.Alex.StaffKey,
            ApprovedAtUtc = ctx.Time.Now.UtcDateTime
        });

        var attributed = await ctx.Repository.AttributeParticipationAsync(
            ctx.DeskA.ConnectionKey, "77", ctx.Alex.StaffKey, ServiceOpsTestContext.TenantA, ctx.Time.Now.UtcDateTime);

        Assert.Equal(2, attributed);
        Assert.All(ctx.Repository.Participants, p => Assert.Equal(ctx.Alex.StaffKey, p.StaffKey));
    }

    [Fact]
    public async Task A_resync_does_not_clear_an_attribution_approved_between_runs()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
            [Ticket("1", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(2), responderId: "77")], Sept1, true));
        await ctx.RunAsync();

        await ctx.Repository.AttributeParticipationAsync(
            ctx.DeskA.ConnectionKey, "77", ctx.Alex.StaffKey, ServiceOpsTestContext.TenantA, ctx.Time.Now.UtcDateTime);

        // The mapper always emits StaffKey null — the attribution comes
        // from the link, so the upsert must not overwrite it back to null.
        ctx.Client.WithTickets(new FreshdeskPage(
            [Ticket("1", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(2), responderId: "77")], Sept1, true));
        await ctx.RunAsync();

        Assert.Equal(ctx.Alex.StaffKey, ctx.Repository.Participants.Single().StaffKey);
    }

    [Fact]
    public async Task Bronze_captures_are_written_for_every_ticket()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));

        await ctx.RunAsync();

        Assert.Contains(ctx.Repository.Raw, r => r.EntityType == "ticket" && r.ExternalId == "1");
    }

    [Fact]
    public async Task A_sync_is_audited_with_its_counts()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));

        await ctx.RunAsync();

        var entry = ctx.Repository.Audit.Single(a => a.Action == ServiceOpsAuditAction.SyncCompleted);
        Assert.Equal(ServiceOpsTestContext.TenantA, entry.TenantId);
        Assert.Contains("\"cases\":1", entry.DetailJson!, StringComparison.Ordinal);
    }
}
