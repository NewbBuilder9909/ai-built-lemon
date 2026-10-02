using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.Controllers;

/// <summary>
/// A stand-in integration, following this project's hand-rolled fake
/// convention. Counts its runs so a test asserting a refusal can also
/// assert the source was never reached — the difference between
/// "refused" and "ran, then refused".
/// </summary>
public sealed class FakeSyncSource(
    string name,
    string displayName,
    string featureKey,
    SourceCapabilities capabilities = SourceCapabilities.Work) : ISyncSource
{
    public string Name { get; } = name;
    public string DisplayName { get; } = displayName;
    public string FeatureKey { get; } = featureKey;
    public SourceCapabilities Capabilities { get; } = capabilities;

    public int RunCount { get; private set; }

    public Task<SyncOutcome> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default)
    {
        RunCount++;
        return Task.FromResult(new SyncOutcome("1 thing published", 0));
    }

    public static FakeSyncSource ClickUpLike() => new("ClickUp", "ClickUp", ProductFeature.ClickUpSync, SourceCapabilities.Work | SourceCapabilities.Time);

    public static FakeSyncSource HubPlannerLike() => new("HubPlanner", "Hub Planner", ProductFeature.HubPlannerSync, SourceCapabilities.Resource);
}

/// <summary>In-memory registry over a fixed set of sources, using the real ordering/lookup rules.</summary>
public sealed class FakeSyncSourceRegistry(params ISyncSource[] sources) : ISyncSourceRegistry
{
    private readonly SyncSourceRegistry _inner = new(sources);

    public IReadOnlyList<ISyncSource> All => _inner.All;

    public ISyncSource? Find(string? name) => _inner.Find(name);
}

/// <summary>Feature gate that enables only the named feature keys.</summary>
public sealed class FakeFeatureGate(params string[] enabledFeatures) : IFeatureGate
{
    private readonly HashSet<string> _enabled = new(enabledFeatures, StringComparer.OrdinalIgnoreCase);

    public Task<FeatureGateDecision> EvaluateAsync(string feature) =>
        Task.FromResult(_enabled.Contains(feature)
            ? new FeatureGateDecision(true, FeatureGateOutcome.Enabled, "Test")
            : new FeatureGateDecision(false, FeatureGateOutcome.NotInPlan, "Test"));

    public async Task<bool> IsEnabledAsync(string feature) => (await EvaluateAsync(feature)).Enabled;
}
