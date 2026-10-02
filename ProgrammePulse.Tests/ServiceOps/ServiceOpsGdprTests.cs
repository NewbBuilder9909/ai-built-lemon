using System.Text.Json;
using ProgrammePulse.Models.Integrations.Freshdesk.Raw;
using ProgrammePulse.Models.ServiceOps;
using static ProgrammePulse.Tests.ServiceOps.FakeFreshdeskClient;

namespace ProgrammePulse.Tests.ServiceOps;

/// <summary>
/// GDPR propagation into support participation.
///
/// This is the one place across the three slices where erasure
/// **detaches rather than deletes**, and the tests exist to pin the
/// reasoning as much as the behaviour. A skill assertion and an
/// attributed commit are claims about a person and mean nothing without
/// them. A support case is a record of something that happened to a
/// customer and how the organisation responded — deleting the
/// participation row would not remove a claim about a person, it would
/// rewrite the organisation's own incident history.
/// </summary>
public class ServiceOpsGdprTests
{
    private static readonly DateTime Sept1 = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private static async Task<ServiceOpsTestContext> WithAttributedParticipationAsync()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
        [
            Ticket("1", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(2), responderId: "77", responderName: "A. Agent"),
            Ticket("2", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(3), responderId: "77", responderName: "A. Agent"),
            Ticket("3", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(4), responderId: "88")
        ], Sept1, true));
        await ctx.RunAsync();

        await ctx.Repository.CreateAgentLinkAsync(new DeskAgentLink
        {
            LinkKey = Guid.NewGuid(),
            TenantId = ServiceOpsTestContext.TenantA,
            ConnectionKey = ctx.DeskA.ConnectionKey,
            Provider = DeskHostPolicy.FreshdeskProvider,
            ExternalAgentId = "77",
            ExternalAgentName = "A. Agent",
            StaffKey = ctx.Alex.StaffKey,
            ApprovedByStaffKey = ctx.Sarah.StaffKey,
            ApprovedAtUtc = ctx.Time.Now.UtcDateTime
        });

        await ctx.Repository.AttributeParticipationAsync(
            ctx.DeskA.ConnectionKey, "77", ctx.Alex.StaffKey, ServiceOpsTestContext.TenantA, ctx.Time.Now.UtcDateTime);

        return ctx;
    }

    [Fact]
    public async Task The_export_includes_the_subjects_participation_and_the_mapping_behind_it()
    {
        var ctx = await WithAttributedParticipationAsync();

        var rows = await ctx.Participant.ExportAsync(ctx.Alex.StaffKey);

        // Two resolutions plus the approval that made them theirs.
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal("Support case participation", r.Section));
        Assert.Contains(rows, r => r.Summary.Contains("was approved as this person", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Summary.Contains("Resolver on support case 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_export_never_includes_someone_elses_participation()
    {
        var ctx = await WithAttributedParticipationAsync();

        var rows = await ctx.Participant.ExportAsync(ctx.Alex.StaffKey);

        // Case 3 was resolved by an account nobody mapped.
        Assert.DoesNotContain(rows, r => r.Summary.Contains("case 3", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_subject_with_no_resolvable_tenant_exports_nothing()
    {
        var ctx = new ServiceOpsTestContext();

        Assert.Empty(await ctx.Participant.ExportAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Erasure_also_blanks_the_name_on_rows_under_their_agent_account_that_were_never_attributed()
    {
        var ctx = await WithAttributedParticipationAsync();
        var theirs = ctx.Repository.Participants.First(p => p.StaffKey == ctx.Alex.StaffKey);
        ctx.Repository.Participants.Add(theirs with
        {
            ParticipantKey = Guid.NewGuid(), ExternalTicketId = "late-ticket", StaffKey = null, AgentDisplayName = "Alex Agent"
        });

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        Assert.All(ctx.Repository.Participants.Where(p => p.ExternalAgentId == theirs.ExternalAgentId), p => Assert.Null(p.AgentDisplayName));
    }

    [Fact]
    public async Task Erasure_detaches_the_person_but_keeps_the_service_history_intact()
    {
        var ctx = await WithAttributedParticipationAsync();
        Assert.Equal(2, ctx.Repository.Participants.Count(p => p.StaffKey == ctx.Alex.StaffKey));

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        // The rows survive: how long each case took to resolve, and that
        // it was resolved at all, is a fact about the service.
        Assert.Equal(3, ctx.Repository.Participants.Count);
        // But nothing points at the person any more, and the agent's own
        // name goes with them.
        Assert.DoesNotContain(ctx.Repository.Participants, p => p.StaffKey == ctx.Alex.StaffKey);
        Assert.All(
            ctx.Repository.Participants.Where(p => p.ExternalAgentId == "77"),
            p => Assert.Null(p.AgentDisplayName));

        Assert.Empty(await ctx.Participant.ExportAsync(ctx.Alex.StaffKey));
    }

    [Fact]
    public async Task Erasure_removes_the_approved_mapping_so_nothing_re_attributes()
    {
        var ctx = await WithAttributedParticipationAsync();

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);
        Assert.Empty(ctx.Repository.AgentLinks);

        // Re-syncing the same tickets must not bring the attribution
        // back. Deleting links before detaching participation is what
        // makes this hold.
        ctx.Client.WithTickets(new FreshdeskPage(
            [Ticket("1", Sept1, status: StatusResolved, resolvedAtUtc: Sept1.AddHours(2), responderId: "77")], Sept1, true));
        await ctx.RunAsync();

        Assert.DoesNotContain(ctx.Repository.Participants, p => p.StaffKey == ctx.Alex.StaffKey);
    }

    [Fact]
    public async Task The_case_figures_are_unchanged_by_an_erasure()
    {
        var ctx = await WithAttributedParticipationAsync();
        var before = await ctx.ReportAsync();

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);
        var after = await ctx.ReportAsync();

        // This is the difference from the skills and evidence slices,
        // where the aggregate legitimately drops. Here the organisation's
        // service record must be the same before and after: the cases
        // still happened and were still resolved.
        Assert.Equal(before.TotalCasesOpened, after.TotalCasesOpened);
        Assert.Equal(
            before.Components.Single().MedianTimeToRestore,
            after.Components.Single().MedianTimeToRestore);
    }

    [Fact]
    public async Task Erasing_one_person_leaves_a_colleagues_attribution_alone()
    {
        var ctx = await WithAttributedParticipationAsync();
        await ctx.Repository.CreateAgentLinkAsync(new DeskAgentLink
        {
            LinkKey = Guid.NewGuid(),
            TenantId = ServiceOpsTestContext.TenantA,
            ConnectionKey = ctx.DeskA.ConnectionKey,
            Provider = DeskHostPolicy.FreshdeskProvider,
            ExternalAgentId = "88",
            StaffKey = ctx.Sarah.StaffKey,
            ApprovedAtUtc = ctx.Time.Now.UtcDateTime
        });
        await ctx.Repository.AttributeParticipationAsync(
            ctx.DeskA.ConnectionKey, "88", ctx.Sarah.StaffKey, ServiceOpsTestContext.TenantA, ctx.Time.Now.UtcDateTime);

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        Assert.Single(ctx.Repository.Participants, p => p.StaffKey == ctx.Sarah.StaffKey);
        Assert.Single(ctx.Repository.AgentLinks);
    }

    [Fact]
    public async Task Erasure_is_audited_with_counts_and_no_content()
    {
        var ctx = await WithAttributedParticipationAsync();

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        var entry = ctx.Repository.Audit.Single(a => a.Action == ServiceOpsAuditAction.ParticipationDetachedForSubject);
        var detail = JsonDocument.Parse(entry.DetailJson!).RootElement;

        Assert.Equal(1, detail.GetProperty("agentLinksDeleted").GetInt32());
        Assert.Equal(2, detail.GetProperty("participationDetached").GetInt32());
        Assert.DoesNotContain("A. Agent", entry.DetailJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reviewed_root_cause_survives_an_erasure_of_its_reviewer()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1, issueKeys: "PROJ-1")], Sept1, true));
        await ctx.RunAsync();

        var link = ctx.Repository.Links.Single();
        await ctx.LinkService.ConfirmRootCauseAsync(
            link.LinkKey, ctx.Sarah.StaffKey, "confirmed at the incident review", ServiceOpsTestContext.TenantA, 1);

        await ctx.Participant.EraseAsync(ctx.Sarah.StaffKey, ctx.Time.Now.UtcDateTime);

        // The assessment stands. It is a finding about a change, and the
        // reviewer key is now pseudonymous like every other audit
        // reference — the alternative is an unexplained causal claim.
        var reviewed = ctx.Repository.Links.Single();
        Assert.True(reviewed.IsCausalClaim);
        Assert.Equal("confirmed at the incident review", reviewed.ReviewNote);
    }

    [Fact]
    public async Task Disconnecting_a_desk_destroys_the_credential_and_keeps_the_history()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));
        await ctx.RunAsync();

        await ctx.Repository.SetConnectionStatusAsync(
            ctx.DeskA.ConnectionKey, DeskConnectionStatus.Disconnected,
            ServiceOpsTestContext.TenantA, ctx.Time.Now.UtcDateTime, clearCredential: true);

        var connection = ctx.Repository.Connections.Single(c => c.ConnectionKey == ctx.DeskA.ConnectionKey);
        Assert.Null(connection.ProtectedCredentialJson);
        Assert.False(connection.IsUsable);
        Assert.NotEmpty(ctx.Repository.Cases);

        // The other tenant's desk is untouched.
        Assert.True(ctx.Repository.Connections.Single(c => c.ConnectionKey == ctx.DeskB.ConnectionKey).IsUsable);
    }
}
