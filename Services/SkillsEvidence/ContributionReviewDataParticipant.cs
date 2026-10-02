using System.Text.Json;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.SkillsEvidence;

public sealed class ContributionReviewDataParticipant(IContributionReviewRepository repository, IStaffRepository staff) : IStaffDataParticipant
{
    public string Section => "Skill contribution examples";

    public async Task<IReadOnlyList<GdprExportLinkedRecordRow>> ExportAsync(Guid staffKey)
    {
        if ((await staff.GetByStaffKeyAsync(staffKey))?.TenantId is not { } tenantId) return [];
        var rows = await repository.ExportAsync(staffKey, tenantId);
        return rows.Select(r => new GdprExportLinkedRecordRow(Section,
            r.StaffKey == staffKey ? JsonSerializer.Serialize(r)
                : JsonSerializer.Serialize(new { r.LinkKey, r.Revision, r.Status, r.DecisionNote, r.RecordedAtUtc }),
            r.RecordedAtUtc)).ToArray();
    }

    public async Task EraseAsync(Guid staffKey, DateTime nowUtc)
    {
        if ((await staff.GetByStaffKeyAsync(staffKey))?.TenantId is { } tenantId)
            await repository.EraseAsync(staffKey, tenantId);
    }
}
