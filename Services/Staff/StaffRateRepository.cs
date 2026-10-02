using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Staff;
using NPoco;
using ProgrammePulse.Services.Shared;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Staff;

public sealed class StaffRateRepository(IScopeProvider scopeProvider, TimeProvider timeProvider) : IStaffRateRepository
{
    public async Task<StaffRate?> GetCurrentAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<StaffRateDto>(
            Sql.Builder.Where("staffKey = @0 AND effectiveToUtc IS NULL", staffKey));
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyList<StaffRate>> GetHistoryAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<StaffRateDto>(
            Sql.Builder.Where("staffKey = @0", staffKey).OrderBy("effectiveFromUtc DESC", "id DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<StaffRate>>> GetHistoryAsync(IReadOnlyCollection<Guid> staffKeys, CancellationToken cancellationToken = default)
    {
        var keys = staffKeys.Distinct().ToList();
        var dtos = new List<StaffRateDto>();
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        foreach (var chunk in keys.Chunk(SqlInList.ChunkSize))
        {
            dtos.AddRange(await scope.Database.FetchAsync<StaffRateDto>(
                Sql.Builder.Where("staffKey IN (@0)", chunk).OrderBy("effectiveFromUtc DESC", "id DESC"), cancellationToken));
        }

        return SqlInList.GroupByKey(keys, dtos.Select(Map), rate => rate.StaffKey);
    }

    public async Task<StaffRate> SetCurrentRateAsync(Guid staffKey, decimal costPerHour, string rateCurrency, Guid changedByStaffKey)
    {
        using var scope = scopeProvider.CreateScope();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var currentDto = await scope.Database.FirstOrDefaultAsync<StaffRateDto>(
            Sql.Builder.Where("staffKey = @0 AND effectiveToUtc IS NULL", staffKey));
        if (currentDto is not null)
        {
            currentDto.EffectiveToUtc = now;
            await scope.Database.UpdateAsync(currentDto);
        }

        var newDto = new StaffRateDto
        {
            StaffKey = staffKey,
            CostPerHour = costPerHour,
            RateCurrency = rateCurrency,
            EffectiveFromUtc = now,
            EffectiveToUtc = null,
            ChangedByStaffKey = changedByStaffKey,
            ChangedAtUtc = now
        };
        await scope.Database.InsertAsync(newDto);

        scope.Complete();

        return Map(newDto);
    }

    private static StaffRate Map(StaffRateDto dto) => new()
    {
        StaffKey = dto.StaffKey,
        CostPerHour = dto.CostPerHour,
        RateCurrency = dto.RateCurrency,
        EffectiveFromUtc = dto.EffectiveFromUtc,
        EffectiveToUtc = dto.EffectiveToUtc,
        ChangedByStaffKey = dto.ChangedByStaffKey,
        ChangedAtUtc = dto.ChangedAtUtc
    };
}
