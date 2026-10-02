using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Programme Ops' contribution to a subject access request / erasure: the
/// explicit identity links (ProgrammeOps_ExternalIdentityLink) that tie a
/// StaffProfile to a ClickUp user id / Hub Planner resource id / upstream
/// email. Registered as IStaffDataParticipant by ProgrammeOperationsComposer.
/// Unresolved-identity rows are not keyed by StaffKey (they are, by
/// definition, not yet anyone's) and age out on the Bronze retention window.
/// </summary>
public sealed class IdentityLinkDataParticipant(IIdentityResolutionRepository identityRepository) : IStaffDataParticipant
{
    public string Section => "Identity links";

    public async Task<IReadOnlyList<GdprExportLinkedRecordRow>> ExportAsync(Guid staffKey)
    {
        var links = await identityRepository.GetLinksForStaffAsync(staffKey);
        return links
            .Select(l => new GdprExportLinkedRecordRow(
                Section,
                $"{l.ExternalSource}: user id {l.ExternalUserId ?? "—"}, email {l.Email ?? "—"}",
                l.CreatedAtUtc))
            .ToList();
    }

    public Task EraseAsync(Guid staffKey, DateTime nowUtc) => identityRepository.DeleteLinksForStaffAsync(staffKey);
}
