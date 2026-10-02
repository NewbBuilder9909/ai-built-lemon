namespace ProgrammePulse.Models.Programme;

/// <summary>
/// Canonical, source-agnostic lifecycle stage for a WorkItem. Any ingestion
/// source (ClickUp today, a calendar or other PM tool later) maps its own
/// free-text status onto this fixed set rather than Silver/Gold ever seeing
/// source-specific status strings.
/// </summary>
public enum WorkItemLifecycleStage
{
    Backlog,
    Ready,
    InProgress,
    Blocked,
    InReview,
    Done,
    Cancelled,

    /// <summary>
    /// A source status that no mapper recognised yet. Kept visible (never
    /// silently dropped or defaulted to Backlog) so unmapped upstream statuses
    /// surface on the overview instead of disappearing.
    /// </summary>
    Unmapped
}
