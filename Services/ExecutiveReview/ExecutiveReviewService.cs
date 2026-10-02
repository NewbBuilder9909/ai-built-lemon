using Microsoft.Extensions.Options;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ExecutiveReview;

public sealed class ExecutiveReviewService(
    IReviewAccess access, IMarketSettingsRepository markets, IExecutivePackRepository packs,
    ISyncSourceRegistry sources, IStaffAuthorizationService authorization,
    IOptions<ExecutiveReviewOptions> options)
{
    public bool Enabled => options.Value.Enabled;

    public async Task<MarketSettingsVersion> GetMarketAsync()
    {
        var actor = await RequireAsync(Capability.ManageStaff);
        return await markets.GetAsync(actor.TenantId);
    }

    public async Task<MarketSettingsVersion> SaveMarketAsync(MarketSettings settings, int version)
    {
        var actor = await RequireAsync(Capability.ManageStaff);
        return await markets.SaveAsync(actor, settings, version);
    }

    public async Task<ExecutiveReviewViewModel> GetOverviewAsync()
    {
        var actor = await RequireAsync(Capability.ViewDeliveryReporting);
        return new(WorkSources(), await packs.GetRecentAsync(actor.TenantId), await authorization.HasAsync(Capability.CaptureTrend));
    }

    public async Task<ExecutivePack?> GetPackAsync(Guid packKey)
    {
        var actor = await RequireAsync(Capability.ViewDeliveryReporting);
        return await packs.GetAsync(actor.TenantId, packKey);
    }

    public async Task<ExecutivePack> CaptureAsync(string source)
    {
        var actor = await RequireAsync(Capability.CaptureTrend);
        await RequireAsync(Capability.ViewDeliveryReporting);
        if (!WorkSources().Any(s => s.Name == source)) throw new ReviewValidationException("Review.InvalidSource");
        return await packs.CaptureAsync(actor, source, options.Value.MaximumPublicationAgeHours);
    }

    private IReadOnlyList<ReviewSource> WorkSources() => sources.All
        .Where(s => s.Capabilities.HasFlag(SourceCapabilities.Work))
        .Select(s => new ReviewSource(s.Name, s.DisplayName)).ToArray();

    private Task<ReviewActor> RequireAsync(string capability)
    {
        if (!Enabled) throw new UnauthorizedAccessException();
        return access.RequireAsync(capability);
    }
}
