using NPoco;
using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>An uploaded file held between a replacement import's preview and its confirmation.</summary>
public sealed record StagedImport(Guid StagingKey, Guid TenantId, string Kind, string Content, string PlanFingerprint, int? CreatedByMemberId, DateTime CreatedAtUtc);

public interface IImportStagingRepository
{
    Task AddAsync(StagedImport staged);

    /// <summary>The staged file, or null if it isn't this tenant's, was confirmed or cancelled, or has been purged.</summary>
    Task<StagedImport?> GetAsync(Guid tenantId, Guid stagingKey);

    Task DeleteAsync(Guid tenantId, Guid stagingKey);

    /// <summary>Retention purge across tenants: staged files nobody confirmed.</summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc);
}

public sealed class ImportStagingRepository(IScopeProvider scopeProvider) : IImportStagingRepository
{
    public async Task AddAsync(StagedImport staged)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.InsertAsync(new ImportStagingDto
        {
            StagingKey = staged.StagingKey, TenantId = staged.TenantId, Kind = staged.Kind, Content = staged.Content,
            PlanFingerprint = staged.PlanFingerprint, CreatedByMemberId = staged.CreatedByMemberId, CreatedAtUtc = staged.CreatedAtUtc
        });
        scope.Complete();
    }

    public async Task<StagedImport?> GetAsync(Guid tenantId, Guid stagingKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<ImportStagingDto>(
            Sql.Builder.Where("tenantId = @0 AND stagingKey = @1", tenantId, stagingKey));
        return dto is null ? null
            : new StagedImport(dto.StagingKey, dto.TenantId, dto.Kind, dto.Content, dto.PlanFingerprint, dto.CreatedByMemberId, dto.CreatedAtUtc);
    }

    public async Task DeleteAsync(Guid tenantId, Guid stagingKey)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"DELETE FROM {ImportStagingDto.TableName} WHERE tenantId = @0 AND stagingKey = @1", tenantId, stagingKey);
        scope.Complete();
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var removed = await scope.Database.ExecuteAsync(
            $"DELETE FROM {ImportStagingDto.TableName} WHERE createdAtUtc < @0", cutoffUtc);
        scope.Complete();
        return removed;
    }
}
