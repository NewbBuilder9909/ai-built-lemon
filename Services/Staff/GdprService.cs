using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Services.Staff;

public sealed class GdprService(
    IStaffRepository staffRepository,
    IAvailabilityRepository availabilityRepository,
    ILeaveRequestRepository leaveRequestRepository,
    IStaffRateRepository staffRateRepository,
    IWorkHoursHistoryRepository workHoursHistoryRepository,
    IStaffAuditLogRepository staffAuditLogRepository,
    IEnumerable<IStaffDataParticipant> participants,
    TimeProvider timeProvider) : IGdprService
{
    public async Task<GdprExport?> BuildExportAsync(Guid staffKey, Guid tenantId)
    {
        var staff = await staffRepository.GetByStaffKeyAsync(staffKey);
        if (staff is null)
        {
            return null;
        }

        // Not DateOnly.MinValue (0001-01-01) — SQL Server's datetime column
        // type only goes back to 1753-01-01, and a value before that throws
        // SqlTypeException at the ADO.NET layer rather than just returning
        // no rows. 1753-01-01 is itself a valid, in-range lower bound, so
        // "everything ever recorded" is still exactly what this returns.
        var earliestPossibleDate = new DateOnly(1753, 1, 1);
        var availability = await availabilityRepository.GetForStaffAsync(staffKey, earliestPossibleDate, DateOnly.MaxValue);
        var leaveRequests = await leaveRequestRepository.GetForStaffAsync(staffKey, tenantId);
        var rateHistory = await staffRateRepository.GetHistoryAsync(staffKey);
        var workHoursHistory = await workHoursHistoryRepository.GetHistoryAsync(staffKey);
        // Article 15 covers "what has been done with my data", not just the
        // data itself — every admin action recorded against this StaffKey.
        var auditTrail = await staffAuditLogRepository.GetForEntityAsync(staffKey.ToString(), tenantId);

        // Records other feature areas hold against the subject (identity
        // links to source-tool users, today) — see IStaffDataParticipant.
        var linkedRecords = new List<GdprExportLinkedRecordRow>();
        foreach (var participant in participants)
        {
            linkedRecords.AddRange(await participant.ExportAsync(staffKey));
        }

        var profile = new GdprExportProfile(
            staff.StaffKey, staff.FullName, staff.Email, staff.JobTitle, staff.Department, staff.Team,
            staff.IsActive, staff.DefaultWorkHoursPerWeek, staff.CreatedAtUtc);

        return new GdprExport(
            profile,
            availability.Select(a => new GdprExportAvailabilityRow(a.Date, a.StartTime, a.EndTime, a.Status, a.Source)).ToList(),
            leaveRequests.Select(l => new GdprExportLeaveRequestRow(l.RequestId, l.RequestedFrom, l.RequestedTo, l.Type, l.Status, l.Notes, l.CreatedAtUtc, l.DecidedAtUtc)).ToList(),
            rateHistory.Select(r => new GdprExportRateRow(r.CostPerHour, r.RateCurrency, r.EffectiveFromUtc, r.EffectiveToUtc, r.ChangedAtUtc)).ToList(),
            workHoursHistory.Select(h => new GdprExportWorkHoursRow(h.HoursPerWeek, h.EffectiveFromUtc, h.EffectiveToUtc, h.ChangedAtUtc)).ToList(),
            auditTrail.Select(a => new GdprExportAuditRow(a.EntityType, a.Action, a.ActorMemberId, a.DetailJson, a.TimestampUtc)).ToList(),
            linkedRecords,
            timeProvider.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// Participants erase first: an identity link carries the subject's
    /// source-tool email and user id, which would otherwise outlive the
    /// pseudonymised staff row.
    /// </summary>
    public async Task EraseAsync(Guid staffKey)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var participant in participants)
        {
            await participant.EraseAsync(staffKey, now);
        }

        await staffRepository.AnonymizeAsync(staffKey, now);
    }
}
