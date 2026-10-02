using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Staff;
using NPoco;
using ProgrammePulse.Services.Shared;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Staff;

public sealed class WorkHoursHistoryRepository(IScopeProvider scopeProvider, TimeProvider timeProvider) : IWorkHoursHistoryRepository
{
    public async Task<WorkHoursHistory?> GetCurrentAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<WorkHoursHistoryDto>(
            Sql.Builder.Where("staffKey = @0 AND effectiveToUtc IS NULL", staffKey));
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyList<WorkHoursHistory>> GetHistoryAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<WorkHoursHistoryDto>(
            Sql.Builder.Where("staffKey = @0", staffKey).OrderBy("effectiveFromUtc DESC", "id DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<WorkHoursHistory>>> GetHistoryAsync(IReadOnlyCollection<Guid> staffKeys, CancellationToken cancellationToken = default)
    {
        var keys = staffKeys.Distinct().ToList();
        var dtos = new List<WorkHoursHistoryDto>();
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        foreach (var chunk in keys.Chunk(SqlInList.ChunkSize))
        {
            dtos.AddRange(await scope.Database.FetchAsync<WorkHoursHistoryDto>(
                Sql.Builder.Where("staffKey IN (@0)", chunk).OrderBy("effectiveFromUtc DESC", "id DESC"), cancellationToken));
        }

        return SqlInList.GroupByKey(keys, dtos.Select(Map), row => row.StaffKey);
    }

    public async Task<WorkHoursHistory> SetCurrentHoursAsync(Guid staffKey, decimal hoursPerWeek, Guid changedByStaffKey)
    {
        using var scope = scopeProvider.CreateScope();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var currentDto = await scope.Database.FirstOrDefaultAsync<WorkHoursHistoryDto>(
            Sql.Builder.Where("staffKey = @0 AND effectiveToUtc IS NULL", staffKey));
        if (currentDto is not null)
        {
            currentDto.EffectiveToUtc = now;
            await scope.Database.UpdateAsync(currentDto);
        }

        var newDto = new WorkHoursHistoryDto
        {
            StaffKey = staffKey,
            HoursPerWeek = hoursPerWeek,
            EffectiveFromUtc = now,
            EffectiveToUtc = null,
            ChangedByStaffKey = changedByStaffKey,
            ChangedAtUtc = now
        };
        await scope.Database.InsertAsync(newDto);

        scope.Complete();

        return Map(newDto);
    }

    private static WorkHoursHistory Map(WorkHoursHistoryDto dto) => new()
    {
        StaffKey = dto.StaffKey,
        HoursPerWeek = dto.HoursPerWeek,
        EffectiveFromUtc = dto.EffectiveFromUtc,
        EffectiveToUtc = dto.EffectiveToUtc,
        ChangedByStaffKey = dto.ChangedByStaffKey,
        ChangedAtUtc = dto.ChangedAtUtc
    };
}
