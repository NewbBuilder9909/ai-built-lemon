using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Programme;
using Umbraco.Cms.Infrastructure.Scoping;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class EstimateBaselineRepository(IScopeProvider scopeProvider) : IEstimateBaselineRepository
{
    public async Task<IReadOnlyList<EstimateBaseline>> GetForTenantAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var rows = await scope.Database.FetchAsync<EstimateBaselineDto>(Sql.Builder.Where("tenantId = @0", tenantId));
        return rows.Select(Map).ToList();
    }

    public async Task<EstimateBaseline> CaptureAsync(EstimateBaseline baseline)
    {
        using var scope = scopeProvider.CreateScope();
        var workItem = await scope.Database.FirstOrDefaultAsync<WorkItemDto>(
            Sql.Builder.Where("workItemKey = @0 AND tenantId = @1", baseline.WorkItemKey, baseline.TenantId));
        if (workItem is null)
        {
            throw new CrossTenantReferenceException("WorkItem", baseline.WorkItemKey);
        }
        var existing = await scope.Database.FirstOrDefaultAsync<EstimateBaselineDto>(
            Sql.Builder.Where("tenantId = @0 AND workItemKey = @1", baseline.TenantId, baseline.WorkItemKey));
        if (existing is not null)
        {
            scope.Complete();
            return Map(existing);
        }

        var dto = new EstimateBaselineDto
        {
            EstimateBaselineKey = baseline.EstimateBaselineKey,
            TenantId = baseline.TenantId,
            WorkItemKey = baseline.WorkItemKey,
            EstimatorStaffKey = baseline.EstimatorStaffKey,
            OriginalEffortHours = baseline.OriginalEffortHours,
            CapturedAtUtc = baseline.CapturedAtUtc
        };
        try
        {
            await scope.Database.InsertAsync(dto);
        }
        catch (Exception)
        {
            // The unique tenant/work-item index settles concurrent capture requests.
            scope.Complete();
            var winner = (await GetForTenantAsync(baseline.TenantId))
                .FirstOrDefault(r => r.WorkItemKey == baseline.WorkItemKey);
            if (winner is not null) return winner;
            throw;
        }
        scope.Complete();
        return Map(dto);
    }

    public async Task<EstimateBaseline?> ReviewAsync(Guid tenantId, Guid estimateBaselineKey, bool isComparable, string? note, Guid reviewedByStaffKey, DateTime reviewedAtUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<EstimateBaselineDto>(
            Sql.Builder.Where("tenantId = @0 AND estimateBaselineKey = @1", tenantId, estimateBaselineKey));
        if (dto is null) return null;
        if (dto.ReviewedAtUtc is not null)
        {
            scope.Complete();
            return Map(dto);
        }
        dto.IsComparable = isComparable;
        dto.ReviewNote = note;
        dto.ReviewedByStaffKey = reviewedByStaffKey;
        dto.ReviewedAtUtc = reviewedAtUtc;
        await scope.Database.UpdateAsync(dto);
        scope.Complete();
        return Map(dto);
    }

    private static EstimateBaseline Map(EstimateBaselineDto dto) => new()
    {
        EstimateBaselineKey = dto.EstimateBaselineKey,
        TenantId = dto.TenantId,
        WorkItemKey = dto.WorkItemKey,
        EstimatorStaffKey = dto.EstimatorStaffKey,
        OriginalEffortHours = dto.OriginalEffortHours,
        CapturedAtUtc = dto.CapturedAtUtc,
        ReviewedAtUtc = dto.ReviewedAtUtc,
        ReviewedByStaffKey = dto.ReviewedByStaffKey,
        IsComparable = dto.IsComparable,
        ReviewNote = dto.ReviewNote
    };
}
