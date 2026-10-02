using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.Integrations.FileImport;

/// <summary>
/// Maps a source tool's free-text status to a lifecycle stage by keyword.
/// Unlike an API connector there is no status category to lean on, so the
/// rule is conservative: a status that matches nothing, or matches two
/// contradictory stages, is <see cref="WorkItemLifecycleStage.Unmapped"/> —
/// visible as a coverage gap rather than silently counted as progress. The
/// raw text is always kept on the work item alongside the mapped stage.
/// </summary>
public static class ImportedStatusMapper
{
    // Order matters only for readability; ambiguity is detected, not resolved by order.
    private static readonly (WorkItemLifecycleStage Stage, string[] Keywords)[] Rules =
    [
        (WorkItemLifecycleStage.Cancelled, ["cancel", "wontdo", "wontfix", "abandon", "descoped", "rejected", "obsolete"]),
        (WorkItemLifecycleStage.Blocked, ["block", "onhold", "stuck", "impeded", "paused"]),
        (WorkItemLifecycleStage.Done, ["done", "complete", "closed", "resolved", "delivered", "released", "shipped", "finished", "accepted", "live"]),
        (WorkItemLifecycleStage.InReview, ["review", "qa", "test", "uat", "verif", "approval", "signoff"]),
        (WorkItemLifecycleStage.InProgress, ["progress", "doing", "active", "started", "development", "indev", "working", "wip", "build"]),
        (WorkItemLifecycleStage.Ready, ["ready", "selected", "planned", "scheduled", "committed"]),
        (WorkItemLifecycleStage.Backlog, ["backlog", "todo", "open", "new", "notstarted", "icebox", "triage", "proposed"])
    ];

    public static WorkItemLifecycleStage Map(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            return WorkItemLifecycleStage.Unmapped;
        }

        var normalised = DeliveryExportSchema.NormaliseHeader(rawStatus);

        // An exact enum name ("InProgress", "Done") is unambiguous by definition.
        if (Enum.TryParse<WorkItemLifecycleStage>(normalised, ignoreCase: true, out var exact)
            && Enum.IsDefined(exact) && !int.TryParse(normalised, out _))
        {
            return exact;
        }

        var matches = Rules
            .Where(rule => rule.Keywords.Any(normalised.Contains))
            .Select(rule => rule.Stage)
            .Distinct()
            .ToList();

        // "Not started" contains "started"; "Ready for review" contains both.
        // Two stages means the text is doing something the keywords can't
        // see — resolve the handful of well-known phrases, refuse the rest.
        if (matches.Count == 2)
        {
            if (matches.Contains(WorkItemLifecycleStage.Backlog) && normalised.Contains("notstarted"))
                return WorkItemLifecycleStage.Backlog;
            if (matches.Contains(WorkItemLifecycleStage.Ready) && matches.Contains(WorkItemLifecycleStage.InReview))
                return WorkItemLifecycleStage.InReview;
            if (matches.Contains(WorkItemLifecycleStage.Blocked))
                return WorkItemLifecycleStage.Blocked;
        }

        return matches.Count == 1 ? matches[0] : WorkItemLifecycleStage.Unmapped;
    }
}
