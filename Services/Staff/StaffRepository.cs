using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Staff;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Staff;

public sealed class StaffRepository(IScopeProvider scopeProvider) : IStaffRepository
{
    public async Task<StaffProfile?> GetByStaffKeyAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<StaffDto>(
            Sql.Builder.Where("staffKey = @0", staffKey));
        return dto is null ? null : Map(dto);
    }

    public async Task<StaffProfile?> GetByMemberIdAsync(int memberId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<StaffDto>(
            Sql.Builder.Where("memberId = @0", memberId));
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyList<StaffProfile>> GetAllAsync()
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<StaffDto>();
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<StaffProfile>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<StaffDto>(
            Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<StaffProfile> CreateAsync(StaffProfile staff)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = new StaffDto
        {
            StaffKey = staff.StaffKey,
            MemberId = staff.MemberId,
            FullName = staff.FullName,
            Email = staff.Email,
            JobTitle = staff.JobTitle,
            Department = staff.Department,
            Team = staff.Team,
            IsActive = staff.IsActive,
            DefaultWorkHoursPerWeek = staff.DefaultWorkHoursPerWeek,
            CalendarProvider = staff.CalendarProvider,
            CalendarUserId = staff.CalendarUserId,
            TenantId = staff.TenantId,
            CreatedAtUtc = staff.CreatedAtUtc,
            UpdatedAtUtc = staff.UpdatedAtUtc
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return staff;
    }

    public async Task UpdateDefaultWorkHoursAsync(Guid staffKey, decimal defaultWorkHoursPerWeek, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<StaffDto>(
            Sql.Builder.Where("staffKey = @0", staffKey))
            ?? throw new InvalidOperationException($"Staff {staffKey} not found.");

        dto.DefaultWorkHoursPerWeek = defaultWorkHoursPerWeek;
        dto.UpdatedAtUtc = nowUtc;

        await scope.Database.UpdateAsync(dto);
        scope.Complete();
    }

    public async Task SetActiveAsync(Guid staffKey, Guid tenantId, bool isActive, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"UPDATE {StaffDto.TableName} SET isActive = @0, updatedAtUtc = @1 WHERE staffKey = @2 AND tenantId = @3",
            isActive, nowUtc, staffKey, tenantId);
        scope.Complete();
    }

    public async Task AnonymizeAsync(Guid staffKey, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<StaffDto>(
            Sql.Builder.Where("staffKey = @0", staffKey))
            ?? throw new InvalidOperationException($"Staff {staffKey} not found.");

        dto.FullName = "Erased Staff Member";
        dto.Email = $"erased-{staffKey:N}@erased.invalid";
        dto.JobTitle = null;
        dto.Department = null;
        dto.IsActive = false;
        dto.UpdatedAtUtc = nowUtc;

        await scope.Database.ExecuteAsync($"DELETE FROM {MemberMfaChallengeDto.TableName} WHERE memberId=@0", dto.MemberId);
        await scope.Database.UpdateAsync(dto);
        scope.Complete();
    }

    private static StaffProfile Map(StaffDto dto) => new()
    {
        StaffKey = dto.StaffKey,
        MemberId = dto.MemberId,
        FullName = dto.FullName,
        Email = dto.Email,
        JobTitle = dto.JobTitle,
        Department = dto.Department,
        Team = dto.Team,
        IsActive = dto.IsActive,
        DefaultWorkHoursPerWeek = dto.DefaultWorkHoursPerWeek,
        CalendarProvider = dto.CalendarProvider,
        CalendarUserId = dto.CalendarUserId,
        TenantId = dto.TenantId,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };
}
