using System.Globalization;
using System.Text;

namespace ProgrammePulse.Services.Integrations.FileImport;

/// <summary>
/// A fictional agency's exports, in the canonical import shape, for a first
/// sales conversation: the prospect sees the Evidence Check find real-looking
/// problems before they have shared any of their own data. Every Evidence
/// Check rule that can apply alongside recorded time fires at least once
/// (DeliveryExportImportServiceTests pins that), and the mess is the ordinary kind — a status nobody mapped, an item with no
/// estimate, time booked to "internal". People and firms are invented;
/// emails use the reserved example.com domain.
///
/// Dates are generated relative to <c>today</c> so "overdue" stays overdue
/// and "due next week" stays in the future whenever the sample is used.
/// </summary>
public static class DeliveryExportSample
{
    private sealed record Person(string Name, string Email);

    private static readonly Person Alex = new("Alex Morgan", "alex.morgan@example.com");
    private static readonly Person Jordan = new("Jordan Lee", "jordan.lee@example.com");
    private static readonly Person Sam = new("Sam Patel", "sam.patel@example.com");
    private static readonly Person Priya = new("Priya Shah", "priya.shah@example.com");
    private static readonly Person Chris = new("Chris Evans", "chris.evans@example.com");
    private static readonly Person Dana = new("Dana Brooks", "dana.brooks@example.com");

    private sealed record Item(string Project, string Workstream, string Id, string Title, string Status, Person? Assignee, int? DueInDays, decimal? Estimate);

    private sealed record Time(string EntryId, string? WorkItemId, Person? Person, int DaysAgo, decimal Hours, bool? Billable);

    private static readonly Item[] Items =
    [
        new("Website rebuild", "Design", "WEB-101", "Homepage design", "Done", Alex, -20, 16),
        new("Website rebuild", "Build", "WEB-102", "Navigation build", "Done", Alex, -14, 12),
        new("Website rebuild", "Build", "WEB-103", "Checkout payment flow", "In progress", Jordan, -6, 24),
        new("Website rebuild", "Build", "WEB-104", "Product search", "In progress", Jordan, 10, 20),
        new("Website rebuild", "Launch", "WEB-105", "Accessibility audit", "Waiting on client", Sam, -3, 8),
        new("Website rebuild", "Build", "WEB-106", "Content migration", "Blocked", Priya, 4, 30),
        new("Website rebuild", "Launch", "WEB-107", "Analytics tagging", "Ready", null, 12, null),
        new("Website rebuild", "Launch", "WEB-108", "SEO redirects", "In review", Sam, null, 6),
        new("Website rebuild", "Launch", "WEB-109", "Cookie banner", "Done", Alex, -30, 4),
        new("Website rebuild", "Build", "WEB-110", "Performance tuning", "Backlog", null, null, null),
        new("Mobile app phase 2", "Features", "APP-201", "Login with single sign-on", "Done", Chris, -10, 20),
        new("Mobile app phase 2", "Features", "APP-202", "Push notifications", "In progress", Chris, -2, 16),
        new("Mobile app phase 2", "Features", "APP-203", "Offline mode", "In development", Dana, 20, null),
        new("Mobile app phase 2", "Release", "APP-204", "App store submission", "Parked", Dana, 6, 4),
        new("Mobile app phase 2", "Features", "APP-205", "Crash reporting", "QA", Dana, 3, 6),
        new("Mobile app phase 2", "Features", "APP-206", "Dark mode", "To do", null, null, 10),
        new("Mobile app phase 2", "Release", "APP-207", "Payments SDK upgrade", "On hold", Chris, -5, 8),
        new("Mobile app phase 2", "Release", "APP-208", "Release 2.1 notes", "Done", Dana, -1, 2),
        new("Data platform retainer", "Support", "DATA-301", "Nightly load failure fix", "Done", Priya, -9, 6),
        new("Data platform retainer", "Support", "DATA-302", "Warehouse cost review", "In progress", Priya, -12, null),
        new("Data platform retainer", "Reporting", "DATA-303", "Dashboard refresh", "Needs info", Sam, 7, 5),
        new("Data platform retainer", "Governance", "DATA-304", "Access review", "Ready", null, null, 3),
        new("Data platform retainer", "Governance", "DATA-305", "Data retention policy", "With legal", Priya, -8, 4),
        new("Data platform retainer", "Support", "DATA-306", "Monthly support allowance", "In progress", Priya, null, 40)
    ];

    private static readonly Time[] Entries =
    [
        new("T-1001", "WEB-101", Alex, 13, 7.5m, true),
        new("T-1002", "WEB-101", Alex, 12, 6.5m, true),
        new("T-1003", "WEB-102", Alex, 11, 7.5m, true),
        new("T-1004", "WEB-102", Alex, 10, 7.5m, true),
        new("T-1005", "WEB-102", Alex, 9, 3m, true),
        new("T-1006", "WEB-103", Jordan, 12, 7.5m, true),
        new("T-1007", "WEB-103", Jordan, 11, 7.5m, true),
        new("T-1008", "WEB-103", Jordan, 10, 7.5m, true),
        new("T-1009", "WEB-103", Jordan, 9, 7.5m, true),
        new("T-1010", "WEB-104", Jordan, 4, 8m, null),
        new("T-1011", "WEB-106", Priya, 6, 6m, true),
        new("T-1012", "WEB-108", Sam, 5, 5m, true),
        new("T-1013", "WEB-110", Alex, 3, 7m, false),
        new("T-1014", "APP-201", Chris, 13, 7.5m, true),
        new("T-1015", "APP-201", Chris, 12, 7.5m, true),
        new("T-1016", "APP-201", Chris, 11, 7m, true),
        new("T-1017", "APP-202", Chris, 6, 6m, true),
        new("T-1018", "APP-202", null, 5, 6m, true),
        new("T-1019", "APP-205", Dana, 4, 3m, null),
        new("T-1020", "APP-206", Dana, 2, 4m, true),
        new("T-1021", "DATA-301", Priya, 9, 6m, true),
        new("T-1022", "DATA-302", Priya, 8, 9m, null),
        new("T-1023", "DATA-306", Priya, 7, 12.5m, true),
        new("T-1024", "DATA-306", Priya, 3, 12.5m, true),
        new("T-1025", null, Sam, 6, 7.5m, false),
        new("T-1026", null, Jordan, 5, 3.5m, null),
        new("T-1027", null, null, 4, 6m, null),
        new("T-1028", null, Chris, 2, 2.5m, false)
    ];

    public static string WorkItemsCsv(DateOnly today)
    {
        var csv = new StringBuilder("Project,Workstream,WorkItemId,Title,Status,Assignee,AssigneeEmail,DueDate,EstimatedHours\n");
        foreach (var item in Items)
        {
            csv.Append(string.Join(',',
                item.Project, item.Workstream, item.Id, item.Title, item.Status,
                item.Assignee?.Name ?? "", item.Assignee?.Email ?? "",
                item.DueInDays is { } days ? Date(today.AddDays(days)) : "",
                item.Estimate?.ToString(CultureInfo.InvariantCulture) ?? ""));
            csv.Append('\n');
        }
        return csv.ToString();
    }

    public static string TimeEntriesCsv(DateOnly today)
    {
        var csv = new StringBuilder("EntryId,WorkItemId,Person,PersonEmail,Date,Hours,Billable\n");
        foreach (var entry in Entries)
        {
            csv.Append(string.Join(',',
                entry.EntryId, entry.WorkItemId ?? "", entry.Person?.Name ?? "", entry.Person?.Email ?? "",
                Date(today.AddDays(-entry.DaysAgo)), entry.Hours.ToString(CultureInfo.InvariantCulture),
                entry.Billable switch { true => "yes", false => "no", null => "" }));
            csv.Append('\n');
        }
        return csv.ToString();
    }

    public static string Csv(DeliveryExportKind kind, DateOnly today) =>
        kind == DeliveryExportKind.WorkItems ? WorkItemsCsv(today) : TimeEntriesCsv(today);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
