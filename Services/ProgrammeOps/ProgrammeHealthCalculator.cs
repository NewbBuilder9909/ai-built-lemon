using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>One evidence row per business programme; no money, inferred benefits or synthetic RAG.</summary>
public static class ProgrammeHealthCalculator
{
    public static IReadOnlyList<ProgrammeHealthRow> Build(
        IReadOnlyList<Programme> programmes, IReadOnlyList<Customer> customers,
        IReadOnlyList<Project> projects, IReadOnlyList<Workstream> workstreams,
        IReadOnlyList<WorkItem> workItems, IReadOnlyList<WorkItem> allTenantWorkItems,
        IReadOnlyList<Risk> risks, IReadOnlyList<Issue> issues, IReadOnlyList<ChangeRequest> changes,
        IReadOnlyList<Dependency> dependencies, IReadOnlyList<WorkstreamBaseline> baselines,
        IReadOnlyList<ProgrammeStakeholder> stakeholders, IReadOnlyList<StaffProfile> staff, DateTime now)
    {
        var customerNames = customers.ToDictionary(c => c.CustomerKey, c => c.Name);
        var staffNames = staff.Where(s => s.IsActive).ToDictionary(s => s.StaffKey, s => s.FullName);
        var projectsByProgramme = projects.ToLookup(p => p.ProgrammeKey);
        var streamsByProject = workstreams.ToLookup(w => w.ProjectKey);
        var itemsByStream = workItems.ToLookup(w => w.WorkstreamKey);
        var risksByProject = risks.Where(r => r.Status != RiskStatus.Closed).ToLookup(r => r.ProjectKey);
        var issuesByProject = issues.Where(i => i.Status != IssueStatus.Resolved).ToLookup(i => i.ProjectKey);
        var changesByProject = changes.Where(c => c.Status == ChangeRequestStatus.Proposed).ToLookup(c => c.ProjectKey);
        var dependenciesByItem = dependencies.ToLookup(d => d.WorkItemKey);
        var targets = allTenantWorkItems.ToDictionary(i => i.WorkItemKey);
        var baselineKeys = baselines.Select(b => b.WorkstreamKey).ToHashSet();
        var ownersByProgramme = stakeholders.Where(s => s.Role == StakeholderRole.Accountable).ToLookup(s => s.ProgrammeKey);
        var rows = new List<ProgrammeHealthRow>();

        foreach (var programme in programmes.OrderBy(p => p.Name))
        {
            var programmeProjects = projectsByProgramme[programme.ProgrammeKey].ToList();
            var streams = programmeProjects.SelectMany(p => streamsByProject[p.ProjectKey]).ToList();
            var items = streams.SelectMany(w => itemsByStream[w.WorkstreamKey]).ToList();
            var open = items.Where(i => !Closed(i)).ToList();
            var openRisks = programmeProjects.SelectMany(p => risksByProject[p.ProjectKey]).ToList();
            var openIssues = programmeProjects.Sum(p => issuesByProject[p.ProjectKey].Count());
            var proposed = programmeProjects.Sum(p => changesByProject[p.ProjectKey].Count());
            var highRisks = openRisks.Count(r => r.Severity is SeverityLevel.High or SeverityLevel.Critical);
            var blocked = open.Count(i => i.Stage == WorkItemLifecycleStage.Blocked);
            var overdue = open.Count(i => i.IsOverdueOn(now));
            var missingEstimates = open.Count(i => i.EstimatedHours is null);
            var unmapped = items.Count(i => i.Stage == WorkItemLifecycleStage.Unmapped);
            var missingBaselines = streams.Count(w => !baselineKeys.Contains(w.WorkstreamKey));
            // Include an upstream dependency in another programme: scoping must not hide its impact.
            var unresolved = open.SelectMany(i => dependenciesByItem[i.WorkItemKey])
                .Count(d => !targets.TryGetValue(d.DependsOnWorkItemKey, out var target) || !Closed(target));
            var owners = ownersByProgramme[programme.ProgrammeKey]
                .Select(s => s.StaffKey is { } key ? staffNames.GetValueOrDefault(key) : s.ExternalName)
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
            var milestones = open.Where(i => i.IsMilestone).ToList();
            var next = milestones.Where(i => i.DueDateUtc is not null).OrderBy(i => i.DueDateUtc).FirstOrDefault();
            var reasons = new List<string>();
            if (blocked > 0) reasons.Add($"{blocked} blocked work items");
            if (overdue > 0) reasons.Add($"{overdue} overdue work items");
            if (highRisks > 0) reasons.Add($"{highRisks} high/critical open risks");
            if (openIssues > 0) reasons.Add($"{openIssues} open issues");
            if (proposed > 0) reasons.Add($"{proposed} changes awaiting decision");
            if (unresolved > 0) reasons.Add($"{unresolved} unresolved dependencies");
            var exceptions = reasons.Count > 0;
            if (items.Count == 0) reasons.Add("No delivery work items recorded");
            if (missingEstimates > 0) reasons.Add($"{missingEstimates} open items without estimates");
            if (unmapped > 0) reasons.Add($"{unmapped} unmapped workflow states");
            if (missingBaselines > 0) reasons.Add($"{missingBaselines} workstreams without a locked baseline");
            if (owners.Count != 1) reasons.Add(owners.Count == 0 ? "No resolved accountable owner" : "Multiple accountable owners: review accountability");
            if (milestones.Any(i => i.DueDateUtc is null)) reasons.Add("Open milestones have missing dates");
            var signal = exceptions ? "Review required" : reasons.Count > 0 ? "Evidence incomplete" : "No exceptions observed";
            rows.Add(new(programme.ProgrammeKey, programme.Name,
                programme.CustomerKey is { } customer ? customerNames.GetValueOrDefault(customer, "Unmapped customer") : "No customer assigned",
                items.Count, items.Count(i => i.Stage == WorkItemLifecycleStage.Done),
                items.Count(i => i.Stage == WorkItemLifecycleStage.Cancelled), blocked, overdue,
                openRisks.Count, highRisks, openIssues, proposed, unresolved, missingEstimates, unmapped,
                missingBaselines, owners.Count == 0 ? "Unassigned" : string.Join(", ", owners),
                next?.Title, next?.DueDateUtc, milestones.Count(i => i.DueDateUtc is null),
                signal, reasons.Count == 0 ? "Review source freshness and completeness before making a decision." : string.Join("; ", reasons)));
        }
        return rows;
    }

    private static bool Closed(WorkItem item) => item.Stage is WorkItemLifecycleStage.Done or WorkItemLifecycleStage.Cancelled;
}
