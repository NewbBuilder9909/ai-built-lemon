using System.Text.Json;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// The continuity plan's part in a subject access request and an
/// erasure.
///
/// **Detach, like Service Ops — not delete, like the skills matrix.**
/// The distinction across the four slices is consistent once stated: a
/// record is deleted when it is a *claim about the person* and means
/// nothing without them, and kept when it is a record of the
/// *organisation* that would be falsified by removing it.
///
/// A continuity plan is the second kind. "Billing has one owner and no
/// approved backup" is a fact about the business's exposure. Deleting
/// the ownership row when its owner is erased would not remove a claim
/// about that person — it would delete the component from the risk
/// register, which is precisely the exposure the view exists to find.
/// So the component stays and becomes **unowned**, which the coverage
/// view sorts to the top and shouts about.
///
/// What goes: approved backups (an approved backup who has left is not
/// cover, and saying otherwise is a false reassurance) and the person's
/// name on everything else. An open action keeps its rationale and loses
/// its owner, so it surfaces as needing reassignment rather than quietly
/// vanishing from the plan.
/// </summary>
public sealed class ContinuityDataParticipant(
    IContinuityRepository repository,
    ISkillsEvidenceAuditLogRepository auditLog,
    IStaffRepository staffRepository) : IStaffDataParticipant
{
    public string Section => "Continuity plan";

    public async Task<IReadOnlyList<GdprExportLinkedRecordRow>> ExportAsync(Guid staffKey)
    {
        var tenantId = await ResolveSubjectTenantAsync(staffKey);
        if (tenantId is null)
        {
            return [];
        }

        var rows = new List<GdprExportLinkedRecordRow>();

        foreach (var component in await repository.GetComponentsAsync(tenantId.Value))
        {
            if (component.OwnerStaffKey == staffKey)
            {
                rows.Add(new GdprExportLinkedRecordRow(
                    Section,
                    $"Named as owner of the component '{component.DisplayName}'",
                    component.LastReviewedAtUtc ?? component.CreatedAtUtc));
            }
        }

        foreach (var backup in (await repository.GetBackupsForStaffAsync(staffKey)).Where(b => b.TenantId == tenantId))
        {
            rows.Add(new GdprExportLinkedRecordRow(
                Section,
                $"Approved as backup cover for '{backup.ComponentKey}'"
                + (backup.Note is null ? string.Empty : $" — \"{backup.Note}\""),
                backup.ApprovedAtUtc));
        }

        foreach (var action in await repository.GetActionsForStaffAsync(staffKey, tenantId.Value))
        {
            rows.Add(new GdprExportLinkedRecordRow(
                Section,
                $"Owns a {action.Type} action on '{action.ComponentKey}': {action.Rationale}",
                action.RaisedAtUtc));
        }

        return rows;
    }

    public async Task EraseAsync(Guid staffKey, DateTime nowUtc)
    {
        var tenantId = await ResolveSubjectTenantAsync(staffKey);

        var (ownerships, backups, actions) = await repository.DetachStaffAsync(staffKey, nowUtc);

        if (tenantId is not null)
        {
            await auditLog.LogAsync(
                SkillsEvidenceAuditAction.EntityTypeStaff,
                staffKey.ToString(),
                SkillsEvidenceAuditAction.ContinuityDetachedForSubject,
                actorMemberId: null,
                JsonSerializer.Serialize(new
                {
                    componentsLeftUnowned = ownerships,
                    backupApprovalsRemoved = backups,
                    actionsNeedingReassignment = actions
                }),
                nowUtc,
                tenantId.Value);
        }
    }

    private async Task<Guid?> ResolveSubjectTenantAsync(Guid staffKey) =>
        (await staffRepository.GetByStaffKeyAsync(staffKey))?.TenantId;
}
