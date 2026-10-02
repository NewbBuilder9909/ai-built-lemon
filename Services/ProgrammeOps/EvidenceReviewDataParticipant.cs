using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Evidence Check reviews' contribution to a subject access request and
/// erasure: where a person was named as a finding's owner, recorded a
/// decision, or recorded the review. Erasure removes the staff key and
/// keeps the finding, so the review history still says what was decided,
/// only no longer by or for whom. Free-text notes are not rewritten; the
/// same limit docs/executive-data-lifecycle.md states for the decision
/// journal applies here.
/// </summary>
public sealed class EvidenceReviewDataParticipant(IEvidenceReviewRepository reviews) : IStaffDataParticipant
{
    public string Section => "Evidence Check reviews";

    public async Task<IReadOnlyList<GdprExportLinkedRecordRow>> ExportAsync(Guid staffKey) =>
        (await reviews.GetStaffReferencesAsync(staffKey))
            .Select(r => new GdprExportLinkedRecordRow(Section, r.Summary, r.RecordedAtUtc))
            .ToList();

    public Task EraseAsync(Guid staffKey, DateTime nowUtc) => reviews.EraseStaffReferencesAsync(staffKey);
}
