using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Services.Staff;

public interface IStaffRepository
{
    Task<StaffProfile?> GetByStaffKeyAsync(Guid staffKey);

    Task<StaffProfile?> GetByMemberIdAsync(int memberId);

    /// <summary>
    /// Every staff row across every tenant. Reporting/ProgrammeOps callers
    /// still use this (those areas are single-tenant this cycle — see
    /// docs/tenancy.md); anything that renders a roster to an Admin should
    /// use <see cref="GetByTenantAsync"/> instead.
    /// </summary>
    Task<IReadOnlyList<StaffProfile>> GetAllAsync();

    /// <summary>Tenant-scoped roster; the filter is in the query, not applied afterward.</summary>
    Task<IReadOnlyList<StaffProfile>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<StaffProfile> CreateAsync(StaffProfile staff);

    /// <summary>
    /// Updates only the "current" convenience value on StaffProfile —
    /// callers also need IWorkHoursHistoryRepository.SetCurrentHoursAsync to
    /// keep the audited history in sync; StaffAdminController.SetWorkHours
    /// is the one place that calls both together.
    /// </summary>
    Task UpdateDefaultWorkHoursAsync(Guid staffKey, decimal defaultWorkHoursPerWeek, DateTime nowUtc);

    /// <summary>
    /// Suspends or restores a person's access. Every capability check reads
    /// <see cref="StaffProfile.IsActive"/>, so this takes effect on their next
    /// request, signed in or not.
    /// </summary>
    Task SetActiveAsync(Guid staffKey, Guid tenantId, bool isActive, DateTime nowUtc);

    /// <summary>
    /// GDPR erasure, scoped to the PII this repository owns: FullName/Email/
    /// JobTitle/Department overwritten with a placeholder, IsActive set
    /// false. Availability/LeaveRequest/StaffRate rows are untouched — they
    /// stay linked by StaffKey for financial/audit integrity, which is the
    /// same pseudonymisation-not-deletion trade-off GDPR itself allows when
    /// full deletion would break another legal obligation (here: historical
    /// cost records). The linked Umbraco Member account (login credentials)
    /// is a separate system this method does not touch — disable or delete
    /// it from the backoffice member list as a separate step.
    /// </summary>
    Task AnonymizeAsync(Guid staffKey, DateTime nowUtc);
}
