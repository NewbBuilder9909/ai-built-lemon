using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.ViewModels.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// "My work": the work items assigned to one person, with the hours they
/// have logged against each. Moved out of StaffPortalController. It lives in
/// ProgrammeOps because it reads programme data; ProgrammeOps may depend on
/// Staff, but not the other way round (FeatureAreaDependencyTests).
/// </summary>
public interface IMyWorkQueryService
{
    Task<MyWorkViewModel> BuildAsync(Guid staffKey, Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class MyWorkQueryService(IProgrammeReadRepository programmeRepository, TimeProvider? timeProvider = null) : IMyWorkQueryService
{
    public async Task<MyWorkViewModel> BuildAsync(Guid staffKey, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var workItems = await programmeRepository.GetWorkItemsAsync(tenantId, cancellationToken);
        var loggedByWorkItem = await programmeRepository.GetLoggedHoursByWorkItemAsync(tenantId, staffKey, cancellationToken);

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        // Open work first (soonest due at the top, so overdue items lead),
        // finished work after — a member's first question is "what's next".
        var rows = workItems
            .Where(w => w.AssignedStaffKey == staffKey)
            .Select(w => new MyWorkItemRowViewModel(
                w.Title,
                w.Stage.ToDisplayLabel(),
                w.DueDateUtc,
                w.EstimatedHours,
                loggedByWorkItem.GetValueOrDefault(w.WorkItemKey),
                IsOverdue: w.IsOverdueOn(now),
                IsClosed: w.Stage.IsClosed()))
            .OrderBy(r => r.IsClosed)
            .ThenBy(r => r.DueDateUtc ?? DateTime.MaxValue)
            .ToList();

        return new MyWorkViewModel(rows, rows.Sum(r => r.LoggedHours));
    }
}
