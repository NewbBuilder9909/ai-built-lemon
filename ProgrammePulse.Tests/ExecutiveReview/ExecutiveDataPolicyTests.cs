using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ExecutiveReview;

namespace ProgrammePulse.Tests.ExecutiveReview;

public sealed class ExecutiveDataPolicyTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(3650, true)]
    [InlineData(3651, false)]
    public void Retention_configuration_is_bounded_and_disabled_by_default(int days, bool valid)
    {
        Assert.Equal(0, new ExecutiveDataOptions().RetentionDays);
        var options = new ExecutiveDataOptions { RetentionDays = days };
        Assert.Equal(valid, Validator.TryValidateObject(options, new ValidationContext(options), [], true));
    }

    [Fact]
    public void Operations_require_explicit_reason_and_correct_target_shape()
    {
        foreach (var operation in Enum.GetValues<ExecutiveDataOperation>())
        {
            var request = Request(operation);
            ExecutiveDataPolicy.Validate(request, 30);
            request.Reason = null;
            Assert.Throws<ReviewValidationException>(() => ExecutiveDataPolicy.Validate(request, 30));
            request = Request(operation);
            request.TargetKey = request.TargetKey is null ? Guid.NewGuid() : null;
            Assert.Throws<ReviewValidationException>(() => ExecutiveDataPolicy.Validate(request, 30));
            request = Request(operation);
            request.Reason = (ExecutiveDataReason)999;
            Assert.Throws<ReviewValidationException>(() => ExecutiveDataPolicy.Validate(request, 30));
        }
        Assert.Throws<ReviewValidationException>(() => ExecutiveDataPolicy.Validate(Request((ExecutiveDataOperation)999), 30));
        Assert.Throws<ReviewValidationException>(() => ExecutiveDataPolicy.Validate(new(), 30));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3651)]
    public void Retention_cannot_run_with_disabled_or_invalid_configuration(int days) =>
        Assert.Equal("Review.Data.RetentionDisabled", Assert.Throws<ReviewValidationException>(() =>
            ExecutiveDataPolicy.Validate(Request(ExecutiveDataOperation.ApplyRetention), days)).Message);

    [Theory]
    [InlineData(DecisionStatus.Draft, false)]
    [InlineData(DecisionStatus.Reviewed, false)]
    [InlineData(DecisionStatus.Decided, false)]
    [InlineData(DecisionStatus.Closed, true)]
    [InlineData(DecisionStatus.Dismissed, true)]
    public void Only_old_terminal_journals_expire_and_exact_cutoff_is_retained(DecisionStatus status, bool expires)
    {
        var cutoff = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var revision = Revision(status, cutoff.AddSeconds(-1));
        Assert.Equal(expires, ExecutiveDataPolicy.ExpiredDecisions([revision], cutoff).Contains(revision.DecisionKey));
        Assert.Empty(ExecutiveDataPolicy.ExpiredDecisions([revision with { RecordedAtUtc = cutoff }], cutoff));
        Assert.Empty(ExecutiveDataPolicy.ExpiredDecisions([revision, revision with { Version = 2, Status = DecisionStatus.Draft }], cutoff));
    }

    [Fact]
    public void Subject_reference_detection_includes_prior_owner_action_owner_and_audit_actor()
    {
        var revision = Revision(DecisionStatus.Closed, DateTime.UtcNow);
        var key = Guid.NewGuid();
        Assert.True(ExecutiveDataPolicy.ReferencesSubject(revision, revision.Definition.OwnerStaffKey, 999));
        Assert.True(ExecutiveDataPolicy.ReferencesSubject(revision, key, revision.ActorMemberId));
        Assert.True(ExecutiveDataPolicy.ReferencesSubject(revision with { ActionOwnerStaffKey = key }, key, 999));
        Assert.False(ExecutiveDataPolicy.ReferencesSubject(revision, key, 999));
    }

    [Fact]
    public async Task Lifecycle_service_requires_tenant_admin_access_before_validation_or_repositories()
    {
        var access = new DeniedAccess();
        var service = new ExecutiveDataService(access, null!, Options.Create(new ExecutiveDataOptions()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetPageAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PreviewAsync(new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyAsync(new(), "", false));
        Assert.Equal(3, access.Calls);
    }

    private static ExecutiveDataRequest Request(ExecutiveDataOperation operation) => new()
    {
        Operation = operation,
        TargetKey = operation is ExecutiveDataOperation.RedactDecision or ExecutiveDataOperation.WithdrawPack ? Guid.NewGuid() : null,
        Reason = operation switch
        {
            ExecutiveDataOperation.ApplyRetention => ExecutiveDataReason.RetentionPolicy,
            ExecutiveDataOperation.PurgeTenantData => ExecutiveDataReason.TenantOffboarding,
            _ => ExecutiveDataReason.PersonalData
        }
    };

    private static DecisionRevision Revision(DecisionStatus status, DateTime at) => new(Guid.NewGuid(), 1,
        new(Guid.NewGuid(), Guid.NewGuid(), "Title", "Material", "Options", "Recommendation", Guid.NewGuid(), new(2026, 9, 1)),
        status, EvidenceDisposition.Unreviewed, null, null, null, null, null, null, null, "Created", "", 12, at);

    private sealed class DeniedAccess : IReviewAccess
    {
        public int Calls { get; private set; }
        public Task<ReviewActor> RequireAsync(string capability)
        {
            Assert.Equal(Capability.ManageStaff, capability);
            Calls++;
            throw new UnauthorizedAccessException();
        }
    }

    [Fact]
    public async Task Tenant_admin_cannot_preview_or_apply_purge_even_with_confirmation()
    {
        var service = new ExecutiveDataService(new TenantAdminAccess(), null!, Options.Create(new ExecutiveDataOptions()));
        var purge = Request(ExecutiveDataOperation.PurgeTenantData);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PreviewAsync(purge));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyAsync(purge, "valid-looking-token", true));
    }

    private sealed class TenantAdminAccess : IReviewAccess
    {
        public Task<ReviewActor> RequireAsync(string capability) => capability == Capability.ManageStaff
            ? Task.FromResult(new ReviewActor(Guid.NewGuid(), 1))
            : throw new UnauthorizedAccessException();
    }
}
