using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>In-memory staged imports, tenant-scoped like the SQL repository.</summary>
public sealed class FakeImportStagingRepository : IImportStagingRepository
{
    public readonly List<StagedImport> Staged = [];

    public Task AddAsync(StagedImport staged)
    {
        Staged.Add(staged);
        return Task.CompletedTask;
    }

    public Task<StagedImport?> GetAsync(Guid tenantId, Guid stagingKey) =>
        Task.FromResult(Staged.FirstOrDefault(s => s.TenantId == tenantId && s.StagingKey == stagingKey));

    public Task DeleteAsync(Guid tenantId, Guid stagingKey)
    {
        Staged.RemoveAll(s => s.TenantId == tenantId && s.StagingKey == stagingKey);
        return Task.CompletedTask;
    }

    public Task<int> DeleteOlderThanAsync(DateTime cutoffUtc) => Task.FromResult(Staged.RemoveAll(s => s.CreatedAtUtc < cutoffUtc));
}
