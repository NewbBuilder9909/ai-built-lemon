using System.Text;

namespace ProgrammePulse.Services.Integrations.FileImport;

/// <summary>Which canonical file an upload claims to be.</summary>
public enum DeliveryExportKind
{
    WorkItems,
    TimeEntries
}

/// <summary>
/// One canonical column. <see cref="Aliases"/> are the header spellings real
/// exports already use (ClickUp "Task ID", Jira "Issue key", Harvest "Spent
/// date"), so a customer can often upload an export unedited — matched
/// case-, space- and punctuation-insensitively.
/// </summary>
public sealed record DeliveryExportColumn(string Name, bool Required, string Description, string Example, params string[] Aliases);

/// <summary>
/// The two canonical files a paid diagnostic works from (see
/// docs/commercial/paid-diagnostic-offer.md): work items and recorded time.
/// Deliberately source-neutral — this is the shape a PMO can produce from
/// any tool or spreadsheet, not a vendor's API contract.
/// </summary>
public static class DeliveryExportSchema
{
    public const int MaxIdLength = 128;
    public const int MaxTitleLength = 512;
    public const int MaxNameLength = 256;
    public const int MaxStatusLength = 128;
    public const int MaxPersonLength = 128;

    public static readonly IReadOnlyList<DeliveryExportColumn> WorkItemColumns =
    [
        new("Project", true, "Project or client engagement the item belongs to.", "Website rebuild", "project name", "list", "board", "space"),
        new("WorkItemId", true, "Stable id from the source tool; re-importing the same id updates the item instead of duplicating it.", "WEB-142", "id", "task id", "issue key", "key", "item id", "issue id"),
        new("Title", true, "What the work is.", "Checkout payment flow", "name", "task name", "summary", "task", "item"),
        new("Status", true, "The source tool's own status text. Mapped to a lifecycle stage; anything unrecognised is kept and shown as Unmapped, never guessed.", "In progress", "state", "task status", "issue status"),
        new("Workstream", false, "Phase, epic or stream inside the project. Defaults to \"Work items\".", "Payments", "phase", "epic", "folder", "stream", "epic name"),
        new("Assignee", false, "Person responsible.", "Alex Morgan", "owner", "assigned to", "assignee name", "assignees"),
        new("AssigneeEmail", false, "Their email — the most reliable way to match them to a staff profile.", "alex@example.com", "owner email", "email", "assignee email address"),
        new("DueDate", false, "yyyy-MM-dd or dd/MM/yyyy.", "2026-10-31", "due", "due on", "target date", "due date utc"),
        new("EstimatedHours", false, "Original estimate in hours.", "16", "estimate", "estimate hours", "original estimate hours", "estimated time hours", "time estimate hours"),
        new("Milestone", false, "yes/no.", "no", "is milestone"),
        new("ParentId", false, "WorkItemId of the parent item, if any.", "WEB-100", "parent", "parent key", "parent task id"),
        new("Programme", false, "Programme or client portfolio the project belongs to. Defaults to \"Imported delivery data\"; re-importing a project under a different programme moves it.", "Digital channels", "programme name", "program", "program name", "portfolio")
    ];

    public static readonly IReadOnlyList<DeliveryExportColumn> TimeEntryColumns =
    [
        new("EntryId", false, "Stable id from the time tool. Without it, an edited row is treated as a new entry and its old version is removed, which the import shows you before confirming.", "T-90311", "id", "time entry id", "entry", "worklog id"),
        new("WorkItemId", false, "The WorkItemId this time was spent on — must already have been imported. Blank means time not linked to an item.", "WEB-142", "task id", "issue key", "key", "item id", "issue id"),
        new("Person", false, "Who recorded it.", "Alex Morgan", "user", "name", "staff", "resource", "team member", "user name"),
        new("PersonEmail", false, "Their email.", "alex@example.com", "email", "user email", "staff email"),
        new("Date", true, "yyyy-MM-dd or dd/MM/yyyy.", "2026-09-21", "work date", "spent date", "day", "date worked"),
        new("Hours", true, "Decimal hours, e.g. 1.5.", "3.5", "duration hours", "time hours", "hours spent", "time spent hours", "duration"),
        new("Billable", false, "yes/no. Blank means billability is unknown, and is reported that way.", "yes", "is billable")
    ];

    public static IReadOnlyList<DeliveryExportColumn> ColumnsFor(DeliveryExportKind kind) =>
        kind == DeliveryExportKind.WorkItems ? WorkItemColumns : TimeEntryColumns;

    /// <summary>A header row plus one example row, for the downloadable template.</summary>
    public static string TemplateCsv(DeliveryExportKind kind)
    {
        var columns = ColumnsFor(kind);
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', columns.Select(c => Quote(c.Name))));
        builder.AppendLine(string.Join(',', columns.Select(c => Quote(c.Example))));
        return builder.ToString();
    }

    /// <summary>"Work Item ID", "work_item_id" and "WorkItemId" all normalise to "workitemid".</summary>
    public static string NormaliseHeader(string header) =>
        new(header.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
