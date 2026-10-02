using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Shared;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class CodeRepositoryLinkRepository(IScopeProvider scopeProvider) : ICodeRepositoryLinkRepository
{
    private const string LiveForTenant = "tenantId = @0 AND removedAtUtc IS NULL";

    public async Task<ResultPage<CodeRepositoryLink>> GetLivePageAsync(Guid tenantId, PageRequest page)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<CodeRepositoryLinkDto>(
            Sql.Builder.Where(LiveForTenant, tenantId).OrderBy("provider", "sourceAccountId", "repositoryKey", "id").ForPage(page));
        return ResultPage<CodeRepositoryLink>.From(dtos.Select(Map).ToList(), page);
    }

    public async Task<IReadOnlyList<CodeRepositoryLink>> GetLiveAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<CodeRepositoryLinkDto>(
            Sql.Builder.Where(LiveForTenant, tenantId).OrderBy("provider", "sourceAccountId", "repositoryKey", "id"));
        return dtos.Select(Map).ToList();
    }

    public async Task<CodeRepositoryLink?> GetLiveByKeyAsync(Guid linkKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<CodeRepositoryLinkDto>(
            Sql.Builder.Where("linkKey = @0 AND tenantId = @1 AND removedAtUtc IS NULL", linkKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<bool> TryCreateAsync(CodeRepositoryLink link)
    {
        using var scope = scopeProvider.CreateScope();

        var projectTenant = await scope.Database.ExecuteScalarAsync<Guid?>(
            $"SELECT tenantId FROM {ProjectDto.TableName} WHERE projectKey = @0 AND tenantId = @1", link.ProjectKey, link.TenantId);
        if (projectTenant != link.TenantId)
        {
            throw new CrossTenantReferenceException("Project", link.ProjectKey);
        }

        // Check and insert in one statement under a key-range lock, so two
        // admins declaring the same link at once cannot both insert; the
        // filtered unique index is the backstop.
        var inserted = await scope.Database.ExecuteAsync(
            $"""
            INSERT INTO {CodeRepositoryLinkDto.TableName}
                (linkKey, tenantId, provider, sourceAccountId, repositoryKey, projectKey, note, linkedByStaffKey, linkedAtUtc)
            SELECT @0, @1, @2, @3, @4, @5, @6, @7, @8
            WHERE NOT EXISTS (
                SELECT 1 FROM {CodeRepositoryLinkDto.TableName} WITH (UPDLOCK, HOLDLOCK)
                WHERE tenantId = @1 AND provider = @2 AND sourceAccountId = @3 AND repositoryKey = @4
                  AND projectKey = @5 AND removedAtUtc IS NULL)
            """,
            new object[]
            {
                link.LinkKey, link.TenantId, link.Repository.Provider, link.Repository.SourceAccountId,
                link.Repository.RepositoryKey, link.ProjectKey, link.Note!, link.LinkedByStaffKey!, link.LinkedAtUtc,
            });

        scope.Complete();
        return inserted == 1;
    }

    public async Task<bool> EndAsync(Guid linkKey, Guid tenantId, Guid? removedByStaffKey, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var ended = await scope.Database.ExecuteAsync(
            $"UPDATE {CodeRepositoryLinkDto.TableName} SET removedAtUtc = @0, removedByStaffKey = @1 WHERE linkKey = @2 AND tenantId = @3 AND removedAtUtc IS NULL",
            nowUtc, removedByStaffKey!, linkKey, tenantId);
        scope.Complete();
        return ended == 1;
    }

    private static CodeRepositoryLink Map(CodeRepositoryLinkDto dto) => new()
    {
        LinkKey = dto.LinkKey,
        TenantId = dto.TenantId,
        Repository = new CodeRepositoryRef(dto.Provider, dto.SourceAccountId, dto.RepositoryKey),
        ProjectKey = dto.ProjectKey,
        Note = dto.Note,
        LinkedByStaffKey = dto.LinkedByStaffKey,
        LinkedAtUtc = dto.LinkedAtUtc,
        RemovedAtUtc = dto.RemovedAtUtc,
        RemovedByStaffKey = dto.RemovedByStaffKey,
    };
}
