using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Programme;
using NPoco;
using ProgrammePulse.Services.Shared;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class IdentityResolutionRepository(IScopeProvider scopeProvider) : IIdentityResolutionRepository
{
    public async Task<IReadOnlyList<ExternalIdentityLink>> GetLinksAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ExternalIdentityLinkDto>(
            Sql.Builder.Where("tenantId = @0", tenantId).OrderBy("externalSource", "createdAtUtc DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ExternalIdentityLink>> GetLinksForSourceAsync(string externalSource, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ExternalIdentityLinkDto>(
            Sql.Builder.Where("externalSource = @0 AND tenantId = @1", externalSource, tenantId));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ExternalIdentityLink>> GetLinksForStaffAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ExternalIdentityLinkDto>(
            Sql.Builder.Where("staffKey = @0", staffKey));
        return dtos.Select(Map).ToList();
    }

    public async Task<ExternalIdentityLink> CreateLinkAsync(ExternalIdentityLink link, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(link.ExternalUserId) && string.IsNullOrWhiteSpace(link.Email))
        {
            throw new ArgumentException("An identity link needs an external user id or an email.", nameof(link));
        }

        var userId = NullIfBlank(link.ExternalUserId);
        var normalisedEmail = NullIfBlank(link.Email)?.ToLowerInvariant();

        using var scope = scopeProvider.CreateScope();

        // Refuse a second link for the same external identity up front (a
        // clear message beats a unique-index violation); the filtered
        // unique indexes from AddIdentityLinkUniqueness are the backstop
        // for two admins racing on the same queue row. Scoped by tenant so
        // two tenants' identical ClickUp user ids/emails never conflict.
        var conflict = await scope.Database.FirstOrDefaultAsync<ExternalIdentityLinkDto>(
            Sql.Builder.Where(
                "tenantId = @0 AND externalSource = @1 AND ((@2 <> '' AND externalUserId = @2) OR (@3 <> '' AND email = @3))",
                tenantId, link.ExternalSource, userId ?? string.Empty, normalisedEmail ?? string.Empty));
        if (conflict is not null)
        {
            scope.Complete();
            throw new DuplicateIdentityLinkException(link.ExternalSource, userId, normalisedEmail, conflict.StaffKey);
        }

        var dto = new ExternalIdentityLinkDto
        {
            LinkKey = link.LinkKey,
            TenantId = tenantId,
            ConnectionKey = link.ConnectionKey,
            ExternalSource = link.ExternalSource,
            ExternalUserId = userId,
            Email = normalisedEmail,
            StaffKey = link.StaffKey,
            CreatedByMemberId = link.CreatedByMemberId,
            CreatedAtUtc = link.CreatedAtUtc
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task DeleteLinkAsync(Guid linkKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"DELETE FROM [{ExternalIdentityLinkDto.TableName}] WHERE [linkKey] = @0 AND [tenantId] = @1", linkKey, tenantId);
        scope.Complete();
    }

    public async Task<IReadOnlyList<UnresolvedIdentity>> GetUnresolvedAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<UnresolvedIdentityDto>(
            Sql.Builder.Where("tenantId = @0 AND resolvedAtUtc IS NULL", tenantId).OrderBy("occurrenceCount DESC", "lastSeenUtc DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<ResultPage<UnresolvedIdentity>> GetUnresolvedPageAsync(Guid tenantId, PageRequest page)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<UnresolvedIdentityDto>(
            Sql.Builder.Where("tenantId = @0 AND resolvedAtUtc IS NULL", tenantId)
                .OrderBy("occurrenceCount DESC", "lastSeenUtc DESC", "id DESC")
                .ForPage(page));
        return ResultPage<UnresolvedIdentity>.From(dtos.Select(Map).ToList(), page);
    }

    public async Task<int> CountUnresolvedAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return await scope.Database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {UnresolvedIdentityDto.TableName} WHERE tenantId = @0 AND resolvedAtUtc IS NULL", tenantId);
    }

    public async Task<UnresolvedIdentity?> GetUnresolvedByKeyAsync(Guid unresolvedIdentityKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<UnresolvedIdentityDto>(
            Sql.Builder.Where("unresolvedIdentityKey = @0 AND tenantId = @1", unresolvedIdentityKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<UnresolvedIdentity> RecordSightingAsync(string externalSource, string? externalUserId, string? email, string? displayName, string context, DateTime nowUtc, Guid tenantId, Guid? suggestedStaffKey = null)
    {
        var userId = NullIfBlank(externalUserId);
        var normalisedEmail = NullIfBlank(email)?.ToLowerInvariant();

        using var scope = scopeProvider.CreateScope();
        // COALESCE to '' rather than "IS NULL OR = @x" so a null argument
        // never has to travel as an untyped NULL parameter.
        var existing = await scope.Database.FirstOrDefaultAsync<UnresolvedIdentityDto>(
            Sql.Builder.Where(
                "tenantId = @0 AND externalSource = @1 AND COALESCE(externalUserId, '') = @2 AND COALESCE(email, '') = @3",
                tenantId, externalSource, userId ?? string.Empty, normalisedEmail ?? string.Empty));

        UnresolvedIdentityDto dto;
        if (existing is null)
        {
            dto = new UnresolvedIdentityDto
            {
                UnresolvedIdentityKey = Guid.NewGuid(),
                TenantId = tenantId,
                ExternalSource = externalSource,
                ExternalUserId = userId,
                Email = normalisedEmail,
                DisplayName = NullIfBlank(displayName),
                Context = context,
                FirstSeenUtc = nowUtc,
                LastSeenUtc = nowUtc,
                OccurrenceCount = 1,
                SuggestedStaffKey = suggestedStaffKey
            };
            await scope.Database.InsertAsync(dto);
        }
        else
        {
            dto = existing;
            dto.OccurrenceCount++;
            dto.LastSeenUtc = nowUtc;
            dto.Context = context;
            dto.DisplayName = NullIfBlank(displayName) ?? dto.DisplayName;
            dto.SuggestedStaffKey = suggestedStaffKey;
            dto.ResolvedStaffKey = null;
            dto.ResolvedAtUtc = null;
            await scope.Database.UpdateAsync(dto);
        }

        scope.Complete();
        return Map(dto);
    }

    public async Task MarkResolvedAsync(Guid unresolvedIdentityKey, Guid staffKey, DateTime nowUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"UPDATE [{UnresolvedIdentityDto.TableName}] SET [resolvedStaffKey] = @0, [resolvedAtUtc] = @1 WHERE [unresolvedIdentityKey] = @2 AND [tenantId] = @3",
            staffKey, nowUtc, unresolvedIdentityKey, tenantId);
        scope.Complete();
    }

    public async Task<int> DeleteUnresolvedNotSeenSinceAsync(DateTime cutoffUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var deleted = await scope.Database.ExecuteAsync(
            $"DELETE FROM [{UnresolvedIdentityDto.TableName}] WHERE [resolvedAtUtc] IS NULL AND [lastSeenUtc] < @0", cutoffUtc);
        scope.Complete();
        return deleted;
    }

    public async Task DeleteLinksForStaffAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"DELETE FROM [{ExternalIdentityLinkDto.TableName}] WHERE [staffKey] = @0", staffKey);
        scope.Complete();
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ExternalIdentityLink Map(ExternalIdentityLinkDto dto) => new()
    {
        LinkKey = dto.LinkKey,
        TenantId = dto.TenantId,
        ConnectionKey = dto.ConnectionKey,
        ExternalSource = dto.ExternalSource,
        ExternalUserId = dto.ExternalUserId,
        Email = dto.Email,
        StaffKey = dto.StaffKey,
        CreatedByMemberId = dto.CreatedByMemberId,
        CreatedAtUtc = dto.CreatedAtUtc
    };

    private static UnresolvedIdentity Map(UnresolvedIdentityDto dto) => new()
    {
        UnresolvedIdentityKey = dto.UnresolvedIdentityKey,
        TenantId = dto.TenantId,
        ExternalSource = dto.ExternalSource,
        ExternalUserId = dto.ExternalUserId,
        Email = dto.Email,
        DisplayName = dto.DisplayName,
        Context = dto.Context,
        FirstSeenUtc = dto.FirstSeenUtc,
        LastSeenUtc = dto.LastSeenUtc,
        OccurrenceCount = dto.OccurrenceCount,
        SuggestedStaffKey = dto.SuggestedStaffKey,
        ResolvedStaffKey = dto.ResolvedStaffKey,
        ResolvedAtUtc = dto.ResolvedAtUtc
    };
}
