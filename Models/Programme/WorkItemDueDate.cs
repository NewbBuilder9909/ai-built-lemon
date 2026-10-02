namespace ProgrammePulse.Models.Programme;

/// <summary>
/// The one rule for "overdue", used by every page and report. Before it
/// existed the Programme Overview said 14 and the Evidence Check said 9 for
/// the same Northstar data, because each counted its own way
/// (docs/design-and-pmo-data-review-2026-09-30.md, B3).
/// </summary>
public static class WorkItemDueDate
{
    /// <summary>
    /// Still open, its status can be read, and its due date is before today
    /// (UTC, whole days: an item due today is not late until tomorrow). An
    /// item whose status can't be read is left out, because nobody knows
    /// whether it is finished; <see cref="IsPastDueWithUnreadableStatus"/>
    /// counts those separately so a page can say so instead of dropping them.
    /// </summary>
    public static bool IsOverdueOn(this WorkItem item, DateTime asOfUtc) =>
        !item.Stage.IsClosed()
        && item.Stage != WorkItemLifecycleStage.Unmapped
        && item.DueDateUtc is { } due
        && due.Date < asOfUtc.Date;

    /// <summary>Past its due date, but its status can't be read, so it can't be called overdue.</summary>
    public static bool IsPastDueWithUnreadableStatus(this WorkItem item, DateTime asOfUtc) =>
        item.Stage == WorkItemLifecycleStage.Unmapped
        && item.DueDateUtc is { } due
        && due.Date < asOfUtc.Date;

    /// <summary>Whole days past the due date; 0 when not past due.</summary>
    public static int DaysPastDue(this WorkItem item, DateTime asOfUtc) =>
        item.DueDateUtc is { } due && due.Date < asOfUtc.Date ? (asOfUtc.Date - due.Date).Days : 0;
}
