using System.Text.Json;
using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.ExecutiveReview;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ExecutiveReview;

public interface IMarketSettingsRepository
{
    Task<MarketSettingsVersion> GetAsync(Guid tenantId);
    Task<MarketSettingsVersion> SaveAsync(ReviewActor actor, MarketSettings settings, int expectedVersion);
}

public sealed class MarketSettingsRepository(
    IScopeProvider scopes,
    TimeProvider clock,
    Microsoft.Extensions.Options.IOptions<ProgrammePulse.Services.Localization.LocalizationSettings>? localization = null) : IMarketSettingsRepository
{
    public async Task<MarketSettingsVersion> GetAsync(Guid tenantId)
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<MarketSettingsVersionDto>(
            Sql.Builder.Where("tenantId = @0", tenantId).OrderBy("version DESC"));
        return dto is null ? new(0, new MarketSettings(), null, null) : new(
            dto.Version, JsonSerializer.Deserialize<MarketSettings>(dto.SettingsJson)!,
            DateTime.SpecifyKind(dto.EffectiveAtUtc, DateTimeKind.Utc), dto.ChangedByMemberId == 0 ? null : dto.ChangedByMemberId);
    }

    public async Task<MarketSettingsVersion> SaveAsync(ReviewActor actor, MarketSettings settings, int expectedVersion)
    {
        MarketConfiguration.Validate(settings, localization?.Value);
        using var scope = scopes.CreateScope();
        // Lock an existing parent, including when no settings row exists yet.
        await LockTenantAsync(scope, actor.TenantId);
        var current = await GetAsync(actor.TenantId);
        if (expectedVersion != current.Version) throw new ReviewConflictException();
        var now = clock.GetUtcNow().UtcDateTime;
        var version = checked(current.Version + 1);
        await scope.Database.InsertAsync(new MarketSettingsVersionDto
        {
            TenantId = actor.TenantId, Version = version, SettingsJson = JsonSerializer.Serialize(settings),
            EffectiveAtUtc = now, ChangedByMemberId = actor.MemberId
        });
        scope.Complete();
        return new(version, settings, now, actor.MemberId);
    }

    internal static async Task LockTenantAsync(IScope scope, Guid tenantId)
    {
        var tenant = await scope.Database.FirstOrDefaultAsync<TenantDto>(
            "SELECT * FROM Tenancy_Tenant WITH (UPDLOCK, HOLDLOCK) WHERE tenantKey = @0", tenantId);
        if (tenant is null) throw new UnauthorizedAccessException();
    }
}
