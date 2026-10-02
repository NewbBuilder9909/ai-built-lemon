using System.Text.Json;
using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ServiceOps;

/// <summary>
/// Service Ops' part in a subject access request and an erasure,
/// registered as <see cref="IStaffDataParticipant"/> by
/// ServiceOperationsComposer.
///
/// **Erasure detaches rather than deletes, and this is the one place in
/// the three slices where that is the right answer.** Skill assertions
/// and engineering evidence are deleted, because detached from the
/// person they mean nothing. A support case is different: it is a record
/// of something that happened to a *customer*, and of how the
/// organisation responded. Deleting the participation row would not
/// remove a claim about a person — it would quietly rewrite the
/// organisation's own incident history, changing how long an outage took
/// to resolve and whether it was resolved at all. So the row stays, the
/// StaffKey and the agent's display name go, and the case keeps its
/// timings.
///
/// What is removed outright is the approved <see cref="DeskAgentLink"/> —
/// the statement that a desk account is this person. Without it nothing
/// re-attributes on the next sync.
///
/// Export is tenant-scoped even though the interface is not: the
/// subject's own tenant is resolved from their StaffProfile and used as
/// the filter, so the export can never be the place a boundary is
/// crossed. A subject with no resolvable tenant exports nothing.
/// </summary>
public sealed class ServiceOpsDataParticipant(
    IServiceOpsRepository repository,
    IStaffRepository staffRepository) : IStaffDataParticipant
{
    public string Section => "Support case participation";

    public async Task<IReadOnlyList<GdprExportLinkedRecordRow>> ExportAsync(Guid staffKey)
    {
        var tenantId = await ResolveSubjectTenantAsync(staffKey);
        if (tenantId is null)
        {
            return [];
        }

        var rows = new List<GdprExportLinkedRecordRow>();

        // The mappings that attributed desk work to them. A statement
        // that an account is this person is their data, and they are
        // entitled to see it and dispute it.
        foreach (var link in await repository.GetAgentLinksForStaffAsync(staffKey))
        {
            if (link.TenantId != tenantId)
            {
                continue;
            }

            rows.Add(new GdprExportLinkedRecordRow(
                Section,
                $"{link.Provider} agent account {link.ExternalAgentName ?? link.ExternalAgentId} was approved as this person"
                + (link.ApprovedByStaffKey is null ? string.Empty : $" by staff {link.ApprovedByStaffKey}"),
                link.ApprovedAtUtc));
        }

        foreach (var participation in await repository.GetParticipantsForStaffAsync(staffKey, tenantId.Value))
        {
            rows.Add(new GdprExportLinkedRecordRow(
                Section,
                $"Recorded as {participation.Role} on support case {participation.ExternalTicketId}",
                participation.OccurredAtUtc));
        }

        return rows;
    }

    public async Task EraseAsync(Guid staffKey, DateTime nowUtc)
    {
        var tenantId = await ResolveSubjectTenantAsync(staffKey);

        // Links first. If the order were reversed, a concurrent sync
        // could re-attribute freshly ingested participation through a
        // link that still existed.
        var agents = (await repository.GetAgentLinksForStaffAsync(staffKey))
            .Select(l => (l.TenantId, l.ConnectionKey, l.ExternalAgentId))
            .ToList();
        var linksDeleted = await repository.DeleteAgentLinksForStaffAsync(staffKey);
        var detached = await repository.DetachParticipationForStaffAsync(staffKey, nowUtc);

        // Rows under the same agent accounts that were never attributed still
        // carry the person's name (Aikido: incomplete data deletion).
        var namesScrubbed = await repository.ScrubAgentNamesAsync(agents, nowUtc);

        if (tenantId is not null)
        {
            await repository.LogAsync(
                ServiceOpsAuditAction.EntityTypeStaff,
                staffKey.ToString(),
                ServiceOpsAuditAction.ParticipationDetachedForSubject,
                actorMemberId: null,
                JsonSerializer.Serialize(new { agentLinksDeleted = linksDeleted, participationDetached = detached, namesScrubbed }),
                nowUtc,
                tenantId.Value);
        }
    }

    private async Task<Guid?> ResolveSubjectTenantAsync(Guid staffKey) =>
        (await staffRepository.GetByStaffKeyAsync(staffKey))?.TenantId;
}
