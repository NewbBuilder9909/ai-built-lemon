using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.ProgrammeOps;

public class FakeStaffRepository : IStaffRepository
{
    public readonly List<StaffProfile> Staff = [];

    public Task<StaffProfile?> GetByStaffKeyAsync(Guid staffKey) =>
        Task.FromResult(Staff.FirstOrDefault(s => s.StaffKey == staffKey));

    public Task<StaffProfile?> GetByMemberIdAsync(int memberId) =>
        Task.FromResult(Staff.FirstOrDefault(s => s.MemberId == memberId));

    public virtual Task<IReadOnlyList<StaffProfile>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<StaffProfile>>(Staff.ToList());

    public Task<IReadOnlyList<StaffProfile>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StaffProfile>>(Staff.Where(s => s.TenantId == tenantId).ToList());

    public Task<StaffProfile> CreateAsync(StaffProfile staff)
    {
        Staff.Add(staff);
        return Task.FromResult(staff);
    }

    public Task UpdateDefaultWorkHoursAsync(Guid staffKey, decimal defaultWorkHoursPerWeek, DateTime nowUtc)
    {
        var existing = Staff.First(s => s.StaffKey == staffKey);
        Staff.Remove(existing);
        Staff.Add(existing with { DefaultWorkHoursPerWeek = defaultWorkHoursPerWeek, UpdatedAtUtc = nowUtc });
        return Task.CompletedTask;
    }

    public Task SetActiveAsync(Guid staffKey, Guid tenantId, bool isActive, DateTime nowUtc)
    {
        var existing = Staff.FirstOrDefault(s => s.StaffKey == staffKey && s.TenantId == tenantId);
        if (existing is not null)
        {
            Staff.Remove(existing);
            Staff.Add(existing with { IsActive = isActive, UpdatedAtUtc = nowUtc });
        }

        return Task.CompletedTask;
    }

    public Task AnonymizeAsync(Guid staffKey, DateTime nowUtc)
    {
        var existing = Staff.First(s => s.StaffKey == staffKey);
        Staff.Remove(existing);
        Staff.Add(existing with
        {
            FullName = "Erased Staff Member",
            Email = $"erased-{staffKey:N}@erased.invalid",
            JobTitle = null,
            Department = null,
            IsActive = false,
            UpdatedAtUtc = nowUtc
        });
        return Task.CompletedTask;
    }
}
