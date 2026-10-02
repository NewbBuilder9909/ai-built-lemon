using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.ViewModels.Reporting;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// PMO governance: the change-request log, programme stakeholders, the
/// dependency view, and locking a workstream's estimate baseline. Read
/// model and commands, moved out of StaffReportingController.
/// </summary>
public interface IGovernanceService
{
    Task<GovernanceViewModel> BuildAsync(Guid tenantId, Guid? programmeKey = null, Guid? customerKey = null);

    Task<CommandOutcome> ProposeChangeAsync(Guid tenantId, Guid projectKey, string? title, string? description, Guid? requestedByStaffKey);

    Task DecideChangeAsync(Guid tenantId, Guid changeRequestKey, ChangeRequestStatus status, Guid? decidedByStaffKey);

    Task<CommandOutcome> AddStakeholderAsync(Guid tenantId, Guid programmeKey, Guid? staffKey, string? externalName, StakeholderRole role);

    Task RemoveStakeholderAsync(Guid tenantId, Guid programmeStakeholderKey);

    Task<bool> LockBaselineAsync(Guid tenantId, Guid workstreamKey, decimal baselineHours, Guid? lockedByStaffKey, Guid? programmeKey = null, Guid? customerKey = null);
}

public sealed class GovernanceService(
    IProgrammeRepository programmeRepository,
    IStaffRepository staffRepository,
    TimeProvider timeProvider) : IGovernanceService
{
    public const string TitleRequired = "Enter a title for the change request.";
    public const string StakeholderRequired = "Choose a staff member or enter an external name.";

    private static readonly WorkItemLifecycleStage[] ClosedStages = [WorkItemLifecycleStage.Done, WorkItemLifecycleStage.Cancelled];

    public async Task<GovernanceViewModel> BuildAsync(Guid tenantId, Guid? programmeKey = null, Guid? customerKey = null)
    {
        var changeRequests = await programmeRepository.GetChangeRequestsAsync(tenantId);
        var stakeholders = await programmeRepository.GetStakeholdersAsync(tenantId);
        var dependencies = await programmeRepository.GetDependenciesAsync(tenantId);
        var projects = await programmeRepository.GetProjectsAsync(tenantId);
        var programmes = await programmeRepository.GetProgrammesAsync(tenantId);
        var staff = await staffRepository.GetByTenantAsync(tenantId);
        var workstreams = await programmeRepository.GetWorkstreamsAsync(tenantId);
        var workItems = await programmeRepository.GetWorkItemsAsync(tenantId);

        var scope = PortfolioScope.Resolve(programmes, await programmeRepository.GetCustomersAsync(tenantId), programmeKey, customerKey);
        if (programmeKey is not null || customerKey is not null)
        {
            programmes = programmes.Where(p => PortfolioScope.Includes(scope, p)).ToList();
            var keys = programmes.Select(p => p.ProgrammeKey).ToHashSet();
            projects = projects.Where(p => keys.Contains(p.ProgrammeKey)).ToList();
            var projectKeys = projects.Select(p => p.ProjectKey).ToHashSet();
            changeRequests = changeRequests.Where(c => projectKeys.Contains(c.ProjectKey)).ToList();
            stakeholders = stakeholders.Where(s => keys.Contains(s.ProgrammeKey)).ToList();
            var streamKeys = workstreams.Where(w => projectKeys.Contains(w.ProjectKey)).Select(w => w.WorkstreamKey).ToHashSet();
            var itemKeys = workItems.Where(i => streamKeys.Contains(i.WorkstreamKey)).Select(i => i.WorkItemKey).ToHashSet();
            dependencies = dependencies.Where(d => itemKeys.Contains(d.WorkItemKey)).ToList();
        }

        var projectNamesByKey = projects.ToDictionary(p => p.ProjectKey, p => p.Name);
        var programmeNamesByKey = programmes.ToDictionary(p => p.ProgrammeKey, p => p.Name);
        var staffNamesByKey = staff.ToDictionary(s => s.StaffKey, s => s.FullName);
        var workstreamNamesByKey = workstreams.ToDictionary(w => w.WorkstreamKey, w => w.Name);
        var workItemsByKey = workItems.ToDictionary(w => w.WorkItemKey);

        var changeRows = changeRequests
            .Select(c => new ChangeRequestRowViewModel(
                c.ChangeRequestKey,
                projectNamesByKey.GetValueOrDefault(c.ProjectKey, "(unknown project)"),
                c.Title,
                c.Description,
                c.Status,
                c.RequestedByStaffKey is Guid requestedBy ? staffNamesByKey.GetValueOrDefault(requestedBy, "(unknown staff)") : null,
                c.DecidedByStaffKey is Guid decidedBy ? staffNamesByKey.GetValueOrDefault(decidedBy, "(unknown staff)") : null,
                c.DecidedAtUtc))
            .OrderByDescending(c => c.Status == ChangeRequestStatus.Proposed)
            .ToList();

        var stakeholderRows = stakeholders
            .Select(s => new StakeholderRowViewModel(
                s.ProgrammeStakeholderKey,
                programmeNamesByKey.GetValueOrDefault(s.ProgrammeKey, "(unknown programme)"),
                s.StaffKey is Guid staffKey ? staffNamesByKey.GetValueOrDefault(staffKey, "(unknown staff)") : (s.ExternalName ?? "(unnamed)"),
                s.Role))
            .OrderBy(s => s.Role)
            .ToList();

        var dependencyRows = dependencies
            .Where(d => workItemsByKey.ContainsKey(d.WorkItemKey) && workItemsByKey.ContainsKey(d.DependsOnWorkItemKey))
            .Select(d =>
            {
                var workItem = workItemsByKey[d.WorkItemKey];
                var dependsOn = workItemsByKey[d.DependsOnWorkItemKey];
                return new DependencyRowViewModel(
                    d.DependencyKey,
                    workstreamNamesByKey.GetValueOrDefault(workItem.WorkstreamKey, "(unknown workstream)"),
                    workItem.Title,
                    workItem.Stage,
                    dependsOn.Title,
                    dependsOn.Stage,
                    !ClosedStages.Contains(dependsOn.Stage));
            })
            .OrderByDescending(d => d.IsUnresolved)
            .ToList();

        var projectOptions = projects.Select(p => new ProjectOptionViewModel(p.ProjectKey, p.Name)).ToList();
        var programmeOptions = programmes.Select(p => new ProgrammeOptionViewModel(p.ProgrammeKey, p.Name)).ToList();

        // Internal stakeholders are picked from the tenant's active staff; the
        // form used to offer only an external name.
        var staffOptions = staff
            .Where(s => s.IsActive)
            .OrderBy(s => s.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(s => new Models.ViewModels.ProgrammeOverview.StaffOptionViewModel(s.StaffKey, s.FullName, s.Email))
            .ToList();

        return new GovernanceViewModel(changeRows, stakeholderRows, dependencyRows, projectOptions, programmeOptions, staffOptions) { Scope = scope };
    }

    public async Task<CommandOutcome> ProposeChangeAsync(Guid tenantId, Guid projectKey, string? title, string? description, Guid? requestedByStaffKey)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return CommandOutcome.Invalid(TitleRequired);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await programmeRepository.CreateChangeRequestAsync(new ChangeRequest
        {
            ChangeRequestKey = Guid.NewGuid(),
            ProjectKey = projectKey,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Status = ChangeRequestStatus.Proposed,
            RequestedByStaffKey = requestedByStaffKey,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, tenantId);

        return CommandOutcome.Ok;
    }

    public Task DecideChangeAsync(Guid tenantId, Guid changeRequestKey, ChangeRequestStatus status, Guid? decidedByStaffKey) =>
        programmeRepository.DecideChangeRequestAsync(changeRequestKey, status, decidedByStaffKey, timeProvider.GetUtcNow().UtcDateTime, tenantId);

    public async Task<CommandOutcome> AddStakeholderAsync(Guid tenantId, Guid programmeKey, Guid? staffKey, string? externalName, StakeholderRole role)
    {
        if (staffKey is null && string.IsNullOrWhiteSpace(externalName))
        {
            return CommandOutcome.Invalid(StakeholderRequired);
        }

        await programmeRepository.AddStakeholderAsync(new ProgrammeStakeholder
        {
            ProgrammeStakeholderKey = Guid.NewGuid(),
            ProgrammeKey = programmeKey,
            StaffKey = staffKey,
            ExternalName = staffKey is null ? externalName?.Trim() : null,
            Role = role,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        }, tenantId);

        return CommandOutcome.Ok;
    }

    public Task RemoveStakeholderAsync(Guid tenantId, Guid programmeStakeholderKey) =>
        programmeRepository.RemoveStakeholderAsync(programmeStakeholderKey, tenantId);

    public async Task<bool> LockBaselineAsync(Guid tenantId, Guid workstreamKey, decimal baselineHours, Guid? lockedByStaffKey, Guid? programmeKey = null, Guid? customerKey = null)
    {
        var programmes = await programmeRepository.GetProgrammesAsync(tenantId);
        var scope = PortfolioScope.Resolve(programmes, await programmeRepository.GetCustomersAsync(tenantId), programmeKey, customerKey);
        var keys = programmes.Where(p => PortfolioScope.Includes(scope, p)).Select(p => p.ProgrammeKey).ToHashSet();
        var projectKeys = (await programmeRepository.GetProjectsAsync(tenantId)).Where(p => keys.Contains(p.ProgrammeKey)).Select(p => p.ProjectKey).ToHashSet();
        if (baselineHours < 0 || !(await programmeRepository.GetWorkstreamsAsync(tenantId)).Any(w => w.WorkstreamKey == workstreamKey && projectKeys.Contains(w.ProjectKey))) return false;
        await programmeRepository.LockBaselineAsync(workstreamKey, baselineHours, lockedByStaffKey, timeProvider.GetUtcNow().UtcDateTime, tenantId);
        return true;
    }
}
