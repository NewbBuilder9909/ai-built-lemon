using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProgrammePulse.Tests.Scenarios.Patchwork;

public sealed record PatchworkFile(string File, string Organisation, string Tool, string Format, DateOnly ExtractedOn);

/// <summary>
/// One planted problem. <see cref="VisibleIn"/> says whether a single tool's
/// own data shows it, or whether it only exists once files from different
/// organisations are put side by side. <see cref="Records"/> are strings that
/// appear verbatim in the named files (keys, ids, names).
/// </summary>
public sealed record PatchworkFinding(
    string Id, string Title, string Kind, string VisibleIn, IReadOnlyList<string> Files, IReadOnlyList<string> Records, string Figure, string Detail);

public sealed record PatchworkTrap(string Id, string Title, IReadOnlyList<string> Files, string Detail);

public sealed record PatchworkAnswerKey(
    string Scenario, DateOnly AsOf, string Client, string Programme, IReadOnlyList<string> Organisations,
    IReadOnlyList<PatchworkFile> Files, IReadOnlyList<PatchworkFinding> Findings, IReadOnlyList<PatchworkTrap> IngestionTraps)
{
    public const string FileName = "answer-key.json";

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json).ReplaceLineEndings("\r\n") + "\r\n";

    public static PatchworkAnswerKey FromJson(string json) => JsonSerializer.Deserialize<PatchworkAnswerKey>(json, Json)!;

    public static PatchworkAnswerKey Build(PatchworkWorld w)
    {
        static string H(decimal hours) => hours.ToString("0.##", CultureInfo.InvariantCulture);
        const string SingleJira = "single tool (Jira)";
        const string SingleClickUp = "single tool (ClickUp)";
        const string Across = "across organisations";

        var kel = w.KelvaroItems.Where(i => !i.IsEpic).ToList();
        var committed = kel.Where(i => i.Sprint == PatchworkWorld.CurrentSprint && !i.IsDone).ToList();
        var noOwner = committed.Where(i => i.AssigneeKey is null).Select(i => i.Key).ToList();
        var overdue = kel.Where(i => !i.IsDone && i.Due < w.AsOf).Select(i => i.Key).ToList();
        var openKel = kel.Count(i => !i.IsDone);
        var noEstimate = committed.Where(i => i.EstimateHours is null).Select(i => i.Key).ToList();

        string[] standard = ["to do", "in progress", "review", "complete", "blocked"];
        var unreadable = w.BrantoftTasks.Where(t => !standard.Contains(t.Status) && t.Due < w.AsOf).ToList();

        var kelKeys = w.KelvaroItems.Select(i => i.Key).ToHashSet();
        var orphans = w.KelvaroWorklogs.Where(l => !kelKeys.Contains(l.IssueKey)).ToList();

        var rafeTempo = w.KelvaroWorklogs.Where(l => l.PersonKey == "rlindqvist" && w.DoubleLoggedDays.Contains(l.Date)).ToList();
        var rafeClickUp = w.BrantoftTime.Where(e => e.PersonKey == "rlindqvist" && w.DoubleLoggedDays.Contains(DateOnly.FromDateTime(e.StartUtc))).ToList();

        var m3 = w.Milestones.Single(m => m.Id == "M3");
        var betaEpic = kel.Where(i => i.ParentKey == "KEL-3" && !i.IsDone).ToList();
        var betaSignOff = w.DunmarrowBoard.SelectMany(g => g.Items).Single(i => i.Name == "Beta design sign-off");

        var m5 = w.Milestones.Single(m => m.Id == "M5");
        var cutOver = w.BrantoftTasks.Single(t => t.Name == "Payment provider cut-over");

        var m6 = w.Milestones.Single(m => m.Id == "M6");
        var dryRun = w.OmbretonItems.Single(i => i.Key == "OMB-4");

        var teo = w.DunmarrowHarvest.Where(h => h.PersonKey == "tbrandt").ToList();

        var boardNames = w.DunmarrowBoard.SelectMany(g => g.Items).Select(i => i.Name).ToList();
        var harvestTotal = w.DunmarrowHarvest.Sum(h => h.Hours);
        var harvestNamed = w.DunmarrowHarvest.Where(h => boardNames.Any(n => h.Notes.Contains(n, StringComparison.OrdinalIgnoreCase))).Sum(h => h.Hours);

        var lastFourWeeks = w.WorkDays().Where(d => w.WeekIndex(d) >= 4 && w.WeekIndex(d) <= 7).ToHashSet();
        var kelvaroBooked = w.OstrevaneTimesheet.Where(r => r.Category == "Supplier capacity: Kelvaro" && lastFourWeeks.Contains(r.Date)).Sum(r => r.Booked);
        var kelvaroLogged = w.KelvaroWorklogs.Where(l => lastFourWeeks.Contains(l.Date)).Sum(l => l.Hours);
        var rafeLogged = w.KelvaroWorklogs.Where(l => l.PersonKey == "rlindqvist" && lastFourWeeks.Contains(l.Date)).Sum(l => l.Hours);

        var files = new List<PatchworkFile>
        {
            new(PatchworkExports.MilestonePlan, PatchworkCast.Client, "Spreadsheet (milestone plan)", "CSV, UK dates, RAG letters", w.AsOf),
            new(PatchworkExports.Planner, PatchworkCast.Client, "Hub Planner", "Timesheet export CSV: booked against actual, h:mm, UK dates", w.AsOf),
            new(PatchworkExports.KelvaroJira, PatchworkCast.Kelvaro, "Jira Cloud", "Export CSV (all fields), 2025 headers, seconds, dd/MMM/yy dates", w.AsOf),
            new(PatchworkExports.KelvaroTempo, PatchworkCast.Kelvaro, "Tempo Timesheets", "Logged time raw export, post-August-2024 column names", w.AsOf),
            new(PatchworkExports.BrantoftTasks, PatchworkCast.Brantoft, "ClickUp", "List export CSV, POSIX milliseconds and US month-first text", w.AsOf),
            new(PatchworkExports.BrantoftTime, PatchworkCast.Brantoft, "ClickUp", "Time tracking export CSV, milliseconds", w.AsOf),
            new(PatchworkExports.DunmarrowBoard, PatchworkCast.Dunmarrow, "monday.com", "Export board to Excel (.xlsx), groups as blocks, subitem rows", w.AsOf),
            new(PatchworkExports.DunmarrowHarvest, PatchworkCast.Dunmarrow, "Harvest", "Detailed time report CSV, decimal hours, cost columns removed", w.AsOf),
            new(PatchworkExports.OmbretonJira, PatchworkCast.Ombreton, "Jira (older export)", "Export CSV, pre-2025 headers, repeated Log Work columns", w.OmbretonExtractedOn),
        };

        var findings = new List<PatchworkFinding>
        {
            new("F01", "Committed work with no owner", "evidence gap", SingleJira, [PatchworkExports.KelvaroJira], noOwner,
                $"{noOwner.Count} of {committed.Count} open items in {PatchworkWorld.CurrentSprint}",
                "In the current sprint with no assignee."),
            new("F02", "Open work past its due date", "delivery exception", SingleJira, [PatchworkExports.KelvaroJira], overdue,
                $"{overdue.Count} of {openKel} open items", "Due date before the export date and not Done (epics excluded)."),
            new("F03", "Committed work with no estimate", "evidence gap", SingleJira, [PatchworkExports.KelvaroJira], noEstimate,
                $"{noEstimate.Count} of {committed.Count} open items in {PatchworkWorld.CurrentSprint}", "Original Estimate is empty."),
            new("F04", "Past due with a status that has no standard meaning", "evidence gap", SingleClickUp, [PatchworkExports.BrantoftTasks],
                unreadable.Select(t => t.Id).ToList(), $"{unreadable.Count} of {w.BrantoftTasks.Count} tasks",
                "Custom statuses (" + string.Join(", ", unreadable.Select(t => t.Status).Distinct()) + ") can't be read as done or not done, so they don't show as overdue."),
            new("F05", "Time logged against items missing from the work-item export", "evidence gap", "single vendor (Jira with Tempo)",
                [PatchworkExports.KelvaroTempo, PatchworkExports.KelvaroJira], orphans.Select(o => o.WorklogId.ToString(CultureInfo.InvariantCulture)).ToList(),
                $"{orphans.Count} worklogs, {H(orphans.Sum(o => o.Hours))} hours", "Worklogs point at " + string.Join(" and ", orphans.Select(o => o.IssueKey).Distinct()) + ", which are not in the Jira export (moved to another project or deleted)."),
            new("F06", "The same person paid twice for the same day", "delivery exception", Across,
                [PatchworkExports.KelvaroTempo, PatchworkExports.BrantoftTime],
                rafeTempo.Select(l => l.WorklogId.ToString(CultureInfo.InvariantCulture)).Concat(rafeClickUp.Select(e => e.EntryId)).Append("Rafe Lindqvist").ToList(),
                $"{w.DoubleLoggedDays.Count} days, {H(rafeTempo.Sum(l => l.Hours))} hours billed by each supplier",
                "Rafe Lindqvist, seconded from Kelvaro to Brantoft on Thursdays and Fridays, has a full day in Kelvaro's Tempo and in Brantoft's ClickUp on the same days. Each supplier's data looks normal on its own."),
            new("F07", "Milestone reported green while the delivery data says otherwise", "delivery exception", Across,
                [PatchworkExports.MilestonePlan, PatchworkExports.KelvaroJira, PatchworkExports.DunmarrowBoard],
                ["M3", .. betaEpic.Select(i => i.Key), betaSignOff.ItemId.ToString(CultureInfo.InvariantCulture)],
                $"M3 is G {w.AsOf.DayNumber - m3.Baseline.DayNumber} days after its date; {betaEpic.Count} open beta items, {betaEpic.Count(i => i.Due < w.AsOf)} overdue; design sign-off Stuck",
                $"The plan was last updated {m3.LastUpdated:yyyy-MM-dd} from a supplier update."),
            new("F08", "One milestone, two dates", "delivery exception", Across, [PatchworkExports.MilestonePlan, PatchworkExports.BrantoftTasks],
                ["M5", cutOver.Id], $"Plan {m5.Forecast:yyyy-MM-dd}, supplier {cutOver.Due:yyyy-MM-dd} ({cutOver.Due!.Value.DayNumber - m5.Forecast.DayNumber} days later)",
                "The supplier's own task moved; the client's plan didn't."),
            new("F09", "Milestone marked complete on a supplier's stale data", "evidence gap", Across, [PatchworkExports.MilestonePlan, PatchworkExports.OmbretonJira],
                ["M6", dryRun.Key], $"Plan says Complete; {dryRun.Key} is {dryRun.Status}; the export is {PatchworkWorld.OmbretonDaysStale} days old",
                "Ombreton's export was taken three weeks before the others, and the plan was updated from a call, not the data."),
            new("F10", "Hours from someone nobody planned or assigned", "evidence gap", Across,
                [PatchworkExports.DunmarrowHarvest, PatchworkExports.Planner, PatchworkExports.DunmarrowBoard], ["Brandt"],
                $"{teo.Count} entries, {H(teo.Sum(t => t.Hours))} hours", "Teo Brandt, a contractor, appears only in Harvest: not in the client's planner, not on the board, where the illustration item has no owner."),
            new("F11", "One person, several names", "reconciliation", Across,
                [PatchworkExports.Planner, PatchworkExports.BrantoftTasks, PatchworkExports.BrantoftTime, PatchworkExports.OmbretonJira],
                ["Samuel Adeyemi", "Sam Adeyemi", "Jessica Carrow", "Jess Carrow", "hana.sato", "ewan.teague"], "3 naming schemes",
                "The planner uses full names, ClickUp short names, and Ombreton's worklogs account names. No file carries an email that joins them."),
            new("F12", "Supplier time that can't be tied to a deliverable", "evidence gap", Across, [PatchworkExports.DunmarrowHarvest, PatchworkExports.DunmarrowBoard], [],
                $"{H(harvestTotal - harvestNamed)} of {H(harvestTotal)} hours", "Harvest records project and task type, not board items. Only notes that repeat a board item's name can be linked; the rest can't."),
            new("F13", "A supplier burning more than the client booked", "delivery exception", Across, [PatchworkExports.KelvaroTempo, PatchworkExports.Planner], ["Rafe Lindqvist"],
                $"Last four weeks: {H(kelvaroLogged)} hours logged against {H(kelvaroBooked)} booked ({H(Math.Round((kelvaroLogged / kelvaroBooked - 1) * 100))}% over)",
                $"Includes {H(rafeLogged)} hours from Rafe Lindqvist, who isn't booked at all, and QA logged at full time against a half-time booking."),
        };

        var traps = new List<PatchworkTrap>
        {
            new("T1", "Renamed columns", [PatchworkExports.KelvaroJira, PatchworkExports.OmbretonJira, PatchworkExports.KelvaroTempo],
                "Kelvaro's Jira says \"Work item key\" (2025), Ombreton's says \"Issue key\". Tempo says \"Logged Hours\" and \"Work Item Key\" (after August 2024)."),
            new("T2", "Five units for time", [PatchworkExports.KelvaroJira, PatchworkExports.BrantoftTasks, PatchworkExports.Planner, PatchworkExports.DunmarrowHarvest, PatchworkExports.DunmarrowBoard],
                "Seconds (Jira), milliseconds (ClickUp), h:mm (Hub Planner), decimal hours (Harvest, Tempo), h:mm:ss (monday)."),
            new("T3", "Five date styles", [PatchworkExports.KelvaroJira, PatchworkExports.BrantoftTasks, PatchworkExports.Planner, PatchworkExports.DunmarrowHarvest, PatchworkExports.MilestonePlan],
                "02/Oct/26 9:00 AM (Jira), POSIX milliseconds and month-first text (ClickUp), dd/MM/yyyy (planner and plan), yyyy-MM-dd (Harvest, monday)."),
            new("T4", "Two columns, one meaning", [PatchworkExports.KelvaroJira, PatchworkExports.OmbretonJira],
                "Jira exports carry both a key (KEL-14) and a numeric id. Time tools refer to the key. \"Parent\" holds the parent's numeric id, not its key."),
            new("T5", "Repeated headers", [PatchworkExports.OmbretonJira], "One \"Log Work\" column per worklog, all with the same header, as \"comment;date;account;seconds\"."),
            new("T6", "A spreadsheet, not a table", [PatchworkExports.DunmarrowBoard],
                "The monday export is .xlsx: board name, then each group as its own block with its own header row; subitems sit under their parent with a different header."),
            new("T7", "Names split or shortened", [PatchworkExports.DunmarrowHarvest, PatchworkExports.BrantoftTime],
                "Harvest splits First Name and Last Name; ClickUp uses short display names; Tempo and Jira use full names."),
        };

        return new PatchworkAnswerKey("Patchwork", w.AsOf, PatchworkCast.Client, PatchworkCast.Programme, PatchworkCast.Organisations, files, findings, traps);
    }
}
