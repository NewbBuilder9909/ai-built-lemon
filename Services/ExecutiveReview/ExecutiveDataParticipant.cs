using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ExecutiveReview;

public sealed class ExecutiveDataParticipant(IStaffRepository staff, IExecutiveDataRepository data) : IStaffDataParticipant
{
    public string Section => "Executive review";

    public async Task<IReadOnlyList<GdprExportLinkedRecordRow>> ExportAsync(Guid staffKey)
    {
        var subject = await staff.GetByStaffKeyAsync(staffKey);
        return subject?.TenantId is Guid tenant
            ? await data.ExportSubjectAsync(tenant, staffKey, subject.MemberId) : [];
    }

    public async Task EraseAsync(Guid staffKey, DateTime nowUtc)
    {
        var subject = await staff.GetByStaffKeyAsync(staffKey);
        if (subject?.TenantId is Guid tenant)
            await data.EraseSubjectAsync(tenant, staffKey, subject.MemberId, nowUtc);
    }
}
