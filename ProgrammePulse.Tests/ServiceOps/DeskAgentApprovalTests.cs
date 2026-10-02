using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Services.ServiceOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.ServiceOps;

/// <summary>
/// Approving a desk agent as a person. Only an agent the desk actually
/// reported, and still unmapped, can be approved, and the name comes from the
/// desk rather than the form (Aikido: business logic bypass).
/// </summary>
public sealed class DeskAgentApprovalTests
{
    private readonly ServiceOpsTestContext _context = new();

    private DeskAdminService Admin() => new(_context.Repository, _context.StaffRepository, _context.Time);

    [Fact]
    public async Task An_agent_the_desk_reported_is_linked_under_the_desk_name()
    {
        Seen("agent-7", "Desk Name");

        var result = await Admin().ApproveAgentAsync(ServiceOpsTestContext.TenantA, _context.DeskA.ConnectionKey, "agent-7", "Forged Name",
            _context.Alex.StaffKey, _context.Sarah.StaffKey, 1);

        Assert.Equal(CommandStatus.Succeeded, result.Status);
        var link = Assert.Single(_context.Repository.AgentLinks);
        Assert.Equal(("agent-7", "Desk Name"), (link.ExternalAgentId, link.ExternalAgentName));
    }

    [Fact]
    public async Task An_id_no_ticket_ever_carried_is_not_found_and_nothing_is_linked()
    {
        Seen("agent-7", "Desk Name");

        var result = await Admin().ApproveAgentAsync(ServiceOpsTestContext.TenantA, _context.DeskA.ConnectionKey, "invented-id", "Anyone",
            _context.Alex.StaffKey, _context.Sarah.StaffKey, 1);

        Assert.Equal(CommandStatus.NotFound, result.Status);
        Assert.Empty(_context.Repository.AgentLinks);
    }

    [Fact]
    public async Task Another_tenants_agent_is_not_found()
    {
        Seen("agent-7", "Desk Name", ServiceOpsTestContext.TenantB, _context.DeskB.ConnectionKey);

        var result = await Admin().ApproveAgentAsync(ServiceOpsTestContext.TenantA, _context.DeskA.ConnectionKey, "agent-7", null,
            _context.Alex.StaffKey, _context.Sarah.StaffKey, 1);

        Assert.Equal(CommandStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task The_links_page_offers_this_tenants_desks_by_name_only()
    {
        var page = await Admin().BuildLinksPageAsync(ServiceOpsTestContext.TenantA, canReviewRootCause: true);

        var desk = Assert.Single(page.Desks!);
        Assert.Equal((_context.DeskA.ConnectionKey, _context.DeskA.DisplayName), (desk.ConnectionKey, desk.DisplayName));
        // A form choice has nowhere to carry a credential.
        Assert.DoesNotContain(typeof(ProgrammePulse.Models.ViewModels.ServiceOps.DeskOption).GetProperties(), p => p.Name.Contains("Credential"));
    }

    private void Seen(string agentId, string name, Guid? tenantId = null, Guid? connectionKey = null) =>
        _context.Repository.Participants.Add(new SupportCaseParticipant
        {
            ParticipantKey = Guid.NewGuid(),
            TenantId = tenantId ?? ServiceOpsTestContext.TenantA,
            ConnectionKey = connectionKey ?? _context.DeskA.ConnectionKey,
            ExternalTicketId = "T-1",
            ExternalAgentId = agentId,
            AgentDisplayName = name,
            Role = default,
            OccurredAtUtc = ServiceOpsTestContext.Start.UtcDateTime,
            IngestedAtUtc = ServiceOpsTestContext.Start.UtcDateTime,
        });
}
