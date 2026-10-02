namespace ProgrammePulse.Models.Programme;

/// <summary>
/// Reader-facing text for <see cref="WorkItemLifecycleStage"/>. Views used
/// <c>Stage.ToString()</c>, which put identifiers such as "InReview" and
/// "InProgress" on customer-facing pages.
/// </summary>
public static class WorkItemLifecycleStageDisplay
{
    public static string ToDisplayLabel(this WorkItemLifecycleStage stage) => stage switch
    {
        WorkItemLifecycleStage.InProgress => "In progress",
        WorkItemLifecycleStage.InReview => "In review",
        // Kept distinct from any real stage: an unrecognised source status is
        // shown, never guessed (see the enum's own remarks).
        WorkItemLifecycleStage.Unmapped => "Unmapped status",
        _ => stage.ToString()
    };

    /// <summary>
    /// Finished one way or another, so it can no longer be overdue. The same
    /// rule the Programme Overview's Overdue KPI applies.
    /// </summary>
    public static bool IsClosed(this WorkItemLifecycleStage stage) =>
        stage is WorkItemLifecycleStage.Done or WorkItemLifecycleStage.Cancelled;
}
