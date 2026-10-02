using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Staff;
using NPoco;
using ProgrammePulse.Services.Shared;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Staff;

public sealed class AvailabilityRepository(IScopeProvider scopeProvider) : IAvailabilityRepository
{
    public async Task<IReadOnlyList<Availability>> GetForStaffAsync(Guid staffKey, DateOnly from, DateOnly to)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<AvailabilityDto>(
            Sql.Builder.Where("staffKey = @0 AND date >= @1 AND date <= @2",
                    staffKey, from.ToDateTime(TimeOnly.MinValue), to.ToDateTime(TimeOnly.MinValue))
                .OrderBy("date, startTime, id"));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Availability>>> GetForStaffAsync(IReadOnlyCollection<Guid> staffKeys, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var keys = staffKeys.Distinct().ToList();
        var dtos = new List<AvailabilityDto>();
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        foreach (var chunk in keys.Chunk(SqlInList.ChunkSize))
        {
            dtos.AddRange(await scope.Database.FetchAsync<AvailabilityDto>(
                Sql.Builder.Where("staffKey IN (@0) AND date >= @1 AND date <= @2",
                        chunk, from.ToDateTime(TimeOnly.MinValue), to.ToDateTime(TimeOnly.MinValue))
                    .OrderBy("date, startTime, id"), cancellationToken));
        }

        return SqlInList.GroupByKey(keys, dtos.Select(Map), row => row.StaffKey);
    }

    public async Task<Availability> CreateAsync(Availability availability)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = new AvailabilityDto
        {
            StaffKey = availability.StaffKey,
            Date = availability.Date.ToDateTime(TimeOnly.MinValue),
            StartTime = availability.StartTime.ToTimeSpan(),
            EndTime = availability.EndTime.ToTimeSpan(),
            Status = availability.Status.ToString(),
            Source = availability.Source.ToString()
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return availability;
    }

    private static Availability Map(AvailabilityDto dto) => new()
    {
        StaffKey = dto.StaffKey,
        Date = DateOnly.FromDateTime(dto.Date),
        StartTime = TimeOnly.FromTimeSpan(dto.StartTime),
        EndTime = TimeOnly.FromTimeSpan(dto.EndTime),
        Status = Enum.Parse<AvailabilityStatus>(dto.Status),
        Source = Enum.Parse<AvailabilitySource>(dto.Source)
    };
}
