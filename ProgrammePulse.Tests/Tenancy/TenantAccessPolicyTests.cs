using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.Tenancy;

public class TenantAccessPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    private static Tenant Make(TenantStatus status, DateTime? trialEnds = null) => new()
    {
        TenantKey = Guid.NewGuid(),
        Name = "Acme",
        ShortCode = "acme",
        IsActive = Tenant.IsUsableStatus(status),
        Status = status,
        Plan = TenantPlan.Starter,
        TrialEndsAtUtc = trialEnds,
        CreatedAtUtc = Now.AddMonths(-1)
    };

    [Fact]
    public void Active_tenant_is_allowed() =>
        Assert.True(TenantAccessPolicy.Evaluate(Make(TenantStatus.Active), Now).Allowed);

    [Fact]
    public void Trial_without_end_date_is_allowed() =>
        Assert.True(TenantAccessPolicy.Evaluate(Make(TenantStatus.Trial), Now).Allowed);

    [Fact]
    public void Trial_before_end_date_is_allowed() =>
        Assert.True(TenantAccessPolicy.Evaluate(Make(TenantStatus.Trial, Now.AddDays(1)), Now).Allowed);

    [Fact]
    public void Expired_trial_is_blocked_with_a_trial_reason()
    {
        var decision = TenantAccessPolicy.Evaluate(Make(TenantStatus.Trial, Now.AddSeconds(-1)), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("trial", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Suspended_tenant_is_blocked()
    {
        var decision = TenantAccessPolicy.Evaluate(Make(TenantStatus.Suspended), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("suspended", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Archived_tenant_is_blocked()
    {
        var decision = TenantAccessPolicy.Evaluate(Make(TenantStatus.Archived), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("archived", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Accessor_maps_a_blocked_tenant_to_unresolved_plus_blocked()
    {
        var state = TenantContextAccessor.Apply(Make(TenantStatus.Suspended), Now);

        Assert.False(state.IsResolved);
        Assert.Null(state.CurrentTenantId);
        Assert.Null(state.CurrentTenant);
        Assert.True(state.IsBlocked);
        Assert.NotNull(state.BlockedReason);
    }

    [Fact]
    public void Accessor_maps_an_allowed_tenant_to_resolved()
    {
        var tenant = Make(TenantStatus.Active);

        var state = TenantContextAccessor.Apply(tenant, Now);

        Assert.True(state.IsResolved);
        Assert.Equal(tenant.TenantKey, state.CurrentTenantId);
        Assert.Same(tenant, state.CurrentTenant);
        Assert.False(state.IsBlocked);
    }

    [Fact]
    public void Accessor_treats_a_dangling_tenant_reference_as_unresolved_not_blocked()
    {
        var state = TenantContextAccessor.Apply(null, Now);

        Assert.False(state.IsResolved);
        Assert.False(state.IsBlocked);
    }
}
