using ProgrammePulse.Services.Integrations.Resilience;

namespace ProgrammePulse.Tests.Integrations;

public class SyncRunGuardTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Second_entry_for_the_same_tenant_and_source_is_refused_until_the_first_lease_is_disposed()
    {
        var guard = new SyncRunGuard();

        var first = guard.TryEnter(TenantA, "ClickUp");
        Assert.NotNull(first);
        Assert.True(guard.IsRunning(TenantA, "ClickUp"));
        Assert.Null(guard.TryEnter(TenantA, "ClickUp"));

        first!.Dispose();

        Assert.False(guard.IsRunning(TenantA, "ClickUp"));
        Assert.NotNull(guard.TryEnter(TenantA, "ClickUp"));
    }

    [Fact]
    public void Sources_are_independent()
    {
        var guard = new SyncRunGuard();

        using var clickUp = guard.TryEnter(TenantA, "ClickUp");

        Assert.NotNull(guard.TryEnter(TenantA, "HubPlanner"));
    }

    /// <summary>
    /// The behaviour this whole phase's SyncRunGuard change exists for: two
    /// different tenants syncing the same source must never serialize
    /// against each other in-process.
    /// </summary>
    [Fact]
    public void Tenants_are_independent_for_the_same_source()
    {
        var guard = new SyncRunGuard();

        using var tenantARun = guard.TryEnter(TenantA, "ClickUp");
        using var tenantBRun = guard.TryEnter(TenantB, "ClickUp");

        Assert.NotNull(tenantBRun);
        Assert.True(guard.IsRunning(TenantA, "ClickUp"));
        Assert.True(guard.IsRunning(TenantB, "ClickUp"));
        Assert.False(guard.IsRunning(Guid.NewGuid(), "ClickUp"));
    }

    [Fact]
    public void Disposing_a_lease_twice_does_not_over_release()
    {
        var guard = new SyncRunGuard();

        var lease = guard.TryEnter(TenantA, "ClickUp")!;
        lease.Dispose();
        lease.Dispose();

        Assert.NotNull(guard.TryEnter(TenantA, "ClickUp"));
        Assert.Null(guard.TryEnter(TenantA, "ClickUp"));
    }
}
