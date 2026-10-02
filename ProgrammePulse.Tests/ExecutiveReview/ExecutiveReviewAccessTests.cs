using Microsoft.Extensions.Options;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ExecutiveReview;

namespace ProgrammePulse.Tests.ExecutiveReview;

public class ExecutiveReviewAccessTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_service_entry_point_requires_access_before_touching_a_repository(bool enabled)
    {
        var access = new DeniedAccess();
        var service = new ExecutiveReviewService(access, null!, null!, null!, null!,
            Options.Create(new ExecutiveReviewOptions { Enabled = enabled }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetMarketAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveMarketAsync(new(), 0));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetOverviewAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetPackAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CaptureAsync("ClickUp"));
        Assert.Equal(enabled ? 5 : 0, access.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Decision_service_guards_every_entry_point_before_repository_access(bool enabled)
    {
        var access = new DeniedAccess();
        var service = new ExecutiveDecisionService(access, null!, null!, null!, null!,
            Options.Create(new ExecutiveReviewOptions { Enabled = enabled }), TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetQueueAsync(null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetDetailAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetEditorAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(null!));
        foreach (var action in Enum.GetValues<DecisionAction>())
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyAsync(Guid.NewGuid(), 1, new(action, "note")));
            if (enabled) Assert.Equal(action is DecisionAction.AcceptEvidence or DecisionAction.DisputeEvidence
                ? Capability.ReviewExecutiveEvidence : Capability.ManageExecutiveDecisions, access.LastCapability);
        }
        Assert.Equal(enabled ? 11 : 0, access.Calls);
    }

    private sealed class DeniedAccess : IReviewAccess
    {
        public int Calls { get; private set; }
        public string? LastCapability { get; private set; }
        public Task<ReviewActor> RequireAsync(string capability)
        {
            Calls++;
            LastCapability = capability;
            throw new UnauthorizedAccessException();
        }
    }
}
