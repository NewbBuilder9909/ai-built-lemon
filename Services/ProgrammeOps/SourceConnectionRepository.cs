using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Programme;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class SourceConnectionRepository(IScopeProvider scopeProvider) : ISourceConnectionRepository
{
    public async Task<SourceConnection?> GetActiveForTenantAsync(Guid tenantId, string source)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A resolved tenant is required.", nameof(tenantId));
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<SourceConnectionDto>(
            Sql.Builder.Where("tenantId = @0 AND source = @1 AND isActive = @2", tenantId, source, true));
        return dto is null ? null : Map(dto);
    }

    public async Task<SourceConnection> GetOrCreateActiveAsync(Guid tenantId, string source, string? externalAccountId, DateTime nowUtc)
    {
        var existing = await GetActiveForTenantAsync(tenantId, source);
        if (existing is not null)
        {
            return existing;
        }

        using var scope = scopeProvider.CreateScope();
        var dto = new SourceConnectionDto
        {
            ConnectionKey = Guid.NewGuid(),
            TenantId = tenantId,
            Source = source,
            DisplayName = source,
            ExternalAccountId = externalAccountId,
            IsActive = true,
            CreatedAtUtc = nowUtc
        };

        try
        {
            await scope.Database.InsertAsync(dto);
        }
        catch (Exception)
        {
            // Lost a race with another concurrent first-sync for the same
            // (tenant, source) — the filtered unique index refused the
            // second insert. The winner's row is there; use it.
            scope.Complete();
            var winner = await GetActiveForTenantAsync(tenantId, source);
            return winner ?? throw new InvalidOperationException($"Could not create or find a source connection for tenant {tenantId} / {source}.");
        }

        scope.Complete();
        return Map(dto);
    }

    public async Task<SourceConnection> SetCredentialAsync(Guid tenantId, string source, string? protectedCredentialJson, DateTime nowUtc)
    {
        await GetOrCreateActiveAsync(tenantId, source, externalAccountId: null, nowUtc);

        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<SourceConnectionDto>(
            Sql.Builder.Where("tenantId = @0 AND source = @1 AND isActive = @2", tenantId, source, true));
        if (dto is null)
        {
            throw new InvalidOperationException($"Could not create or find a source connection for tenant {tenantId} / {source}.");
        }

        dto.ProtectedCredentialJson = protectedCredentialJson;
        await scope.Database.UpdateAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    private static SourceConnection Map(SourceConnectionDto dto) => new()
    {
        ConnectionKey = dto.ConnectionKey,
        TenantId = dto.TenantId,
        Source = dto.Source,
        DisplayName = dto.DisplayName,
        ExternalAccountId = dto.ExternalAccountId,
        IsActive = dto.IsActive,
        CreatedAtUtc = dto.CreatedAtUtc,
        ProtectedCredentialJson = dto.ProtectedCredentialJson
    };
}
