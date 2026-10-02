using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace ProgrammePulse.Tests.Scenarios.Patchwork;

/// <summary>
/// Writes the scenario as each organisation's tool would export it. Column
/// names, units and date styles follow each vendor's export as publicly
/// documented in 2024–2026 (see TestScenarios/Patchwork/README.md for the
/// sources and for what is approximate). The differences between files are
/// the point: the same week of work arrives as seconds, milliseconds,
/// decimal hours and h:mm; as "02/Oct/26 9:00 AM", POSIX milliseconds, US
/// month-first text and UK day-first dates.
/// </summary>
public static class PatchworkExports
{
    public const string MilestonePlan = "ostrevane-milestone-plan.csv";
    public const string Planner = "ostrevane-hubplanner-timesheet.csv";
    public const string KelvaroJira = "kelvaro-jira-work-items.csv";
    public const string KelvaroTempo = "kelvaro-tempo-worklogs.csv";
    public const string BrantoftTasks = "brantoft-clickup-tasks.csv";
    public const string BrantoftTime = "brantoft-clickup-time-entries.csv";
    public const string DunmarrowBoard = "dunmarrow-monday-board.xlsx";
    public const string DunmarrowHarvest = "dunmarrow-harvest-time.csv";
    public const string OmbretonJira = "ombreton-jira-issues.csv";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static IReadOnlyDictionary<string, byte[]> Write(PatchworkWorld world) => new Dictionary<string, byte[]>
    {
        [MilestonePlan] = Utf8(MilestonePlanCsv(world)),
        [Planner] = Utf8(PlannerCsv(world)),
        [KelvaroJira] = Utf8(KelvaroJiraCsv(world)),
        [KelvaroTempo] = Utf8(TempoCsv(world)),
        [BrantoftTasks] = Utf8(ClickUpTasksCsv(world)),
        [BrantoftTime] = Utf8(ClickUpTimeCsv(world)),
        [DunmarrowBoard] = MondayXlsx(world),
        [DunmarrowHarvest] = Utf8(HarvestCsv(world)),
        [OmbretonJira] = Utf8(OmbretonJiraCsv(world)),
    };

    // ---- Client PMO: a milestone plan kept in a spreadsheet, UK dates.

    private static string MilestonePlanCsv(PatchworkWorld world) => Csv(
        ["ID", "Milestone", "Supplier", "Baseline date", "Forecast date", "RAG", "Last updated", "Notes"],
        world.Milestones.Select(m => new[] { m.Id, m.Name, m.Supplier, Uk(m.Baseline), Uk(m.Forecast), m.Rag, Uk(m.LastUpdated), m.Notes }));

    // ---- Client PMO: Hub Planner timesheet export. Booked against actual, h:mm.

    private static string PlannerCsv(PatchworkWorld world) => Csv(
        ["Date", "Resource Name", "Project Name", "Project Status", "Category", "Booked Time", "Actual Time", "Note"],
        world.OstrevaneTimesheet.Select(r => new[]
        {
            Uk(r.Date), r.ResourceName, PatchworkCast.Programme, "Active", r.Category, HourMinutes(r.Booked),
            r.Actual is { } actual ? HourMinutes(actual) : "", r.Note
        }));

    // ---- Kelvaro: Jira Cloud "Export CSV (all fields)", 2025 headers ("Work item key").

    private static string KelvaroJiraCsv(PatchworkWorld world)
    {
        var byKey = world.KelvaroItems.ToDictionary(i => i.Key);
        return Csv(
            ["Summary", "Work item key", "Work item id", "Work type", "Status", "Status Category", "Project key", "Project name", "Priority",
             "Assignee", "Reporter", "Created", "Updated", "Resolved", "Due date", "Labels", "Original Estimate", "Remaining Estimate",
             "Time Spent", "Sprint", "Parent", "Parent summary"],
            world.KelvaroItems.Select(i =>
            {
                var parent = i.ParentKey is { } key ? byKey[key] : null;
                return new[]
                {
                    i.Summary, i.Key, i.Id.ToString(Invariant), i.Type, i.Status, StatusCategory(i.Status), "KEL", "Ostrevane Passenger App", i.Priority,
                    Name(world, i.AssigneeKey), Name(world, i.ReporterKey), JiraDate(i.Created), JiraDate(i.Updated), i.Resolved is { } r ? JiraDate(r) : "",
                    i.Due is { } due ? JiraDate(due.ToDateTime(TimeOnly.MinValue)) : "", i.IsEpic ? "" : "ostrevane",
                    Seconds(i.EstimateHours), Seconds(i.EstimateHours is { } e ? Math.Max(0, i.IsDone ? 0 : e - i.SpentHours) : null),
                    i.SpentHours > 0 ? Seconds(i.SpentHours) : "", i.Sprint ?? "", parent?.Id.ToString(Invariant) ?? "", parent?.Summary ?? ""
                };
            }));
    }

    // ---- Kelvaro: Tempo Timesheets logged-time raw export, column names after Tempo's August 2024 change.

    private static string TempoCsv(PatchworkWorld world) => Csv(
        ["Worklog Id", "Work Item Key", "Work Item Summary", "Logged Hours", "Logged Seconds", "Billable Hours", "Billable Seconds",
         "Work date", "Full name", "Account Key", "Account Name", "Work Description", "Project Key", "Project Name"],
        world.KelvaroWorklogs.Select(l => new[]
        {
            l.WorklogId.ToString(Invariant), l.IssueKey, l.IssueSummary, Dec(l.Hours), ((int)(l.Hours * 3600)).ToString(Invariant),
            Dec(l.Billable ? l.Hours : 0), ((int)((l.Billable ? l.Hours : 0) * 3600)).ToString(Invariant),
            PatchworkWorld.LocalMorning(l.Date).ToString("yyyy-MM-dd HH:mm", Invariant), world.Person(l.PersonKey).FullName,
            "OST-TM", "Ostrevane time and materials", l.Description, "KEL", "Ostrevane Passenger App"
        }));

    // ---- Brantoft: ClickUp list export, POSIX milliseconds plus "Text" columns in US month-first style.

    private static string ClickUpTasksCsv(PatchworkWorld world) => Csv(
        ["Task ID", "Task Name", "Task Content", "Status", "Date Created", "Date Created Text", "Due Date", "Due Date Text", "Start Date",
         "Start Date Text", "Parent ID", "Assignees", "Tags", "Priority", "List Name", "Folder Name", "Space Name", "Time Estimated",
         "Time Estimated Text", "Time Spent", "Time Spent Text"],
        world.BrantoftTasks.Select(t => new[]
        {
            t.Id, t.Name, t.Content, t.Status, Posix(t.CreatedUtc), UsText(t.CreatedUtc), Posix(t.Due), UsDate(t.Due), Posix(t.Start), UsDate(t.Start), "",
            "[" + string.Join(", ", t.AssigneeKeys.Select(k => PatchworkWorld.ClickUpName(world.Person(k)))) + "]",
            "[" + string.Join(", ", t.Tags) + "]", t.Priority, t.List, "Ticketing Platform", "Ostrevane",
            Milliseconds(t.EstimateHours), Duration(t.EstimateHours), Milliseconds(t.SpentHours > 0 ? t.SpentHours : null), Duration(t.SpentHours > 0 ? t.SpentHours : null)
        }));

    // ---- Brantoft: ClickUp time-tracking export.

    private static string ClickUpTimeCsv(PatchworkWorld world)
    {
        var tasks = world.BrantoftTasks.ToDictionary(t => t.Id);
        var userIds = world.People.Select((p, i) => (p.Key, Id: 81_000_000 + i * 4_141)).ToDictionary(x => x.Key, x => x.Id);
        return Csv(
            ["User ID", "Username", "Time Entry ID", "Description", "Billable", "Time Labels", "Start", "Start Text", "Stop", "Stop Text",
             "Time Tracked", "Time Tracked Text", "Space Name", "Folder Name", "List Name", "Task ID", "Task Name", "Task Status"],
            world.BrantoftTime.Select(e =>
            {
                var task = tasks[e.TaskId];
                var stop = e.StartUtc.AddHours((double)e.Hours);
                return new[]
                {
                    userIds[e.PersonKey].ToString(Invariant), PatchworkWorld.ClickUpName(world.Person(e.PersonKey)), e.EntryId, e.Description,
                    e.Billable ? "TRUE" : "FALSE", "", Posix(e.StartUtc), UsText(e.StartUtc), Posix(stop), UsText(stop),
                    Milliseconds(e.Hours), Duration(e.Hours), "Ostrevane", "Ticketing Platform", task.List, task.Id, task.Name, task.Status
                };
            }));
    }

    // ---- Dunmarrow: Harvest detailed time report. Cost columns left empty, as a supplier would before sharing.

    private static string HarvestCsv(PatchworkWorld world) => Csv(
        ["Date", "Client", "Project", "Project Code", "Task", "Notes", "Hours", "Hours Rounded", "Billable?", "Invoiced?", "Approved?",
         "First Name", "Last Name", "Employee?", "Roles", "Billable Rate", "Billable Amount", "Cost Rate", "Cost Amount", "Currency",
         "External Reference URL"],
        world.DunmarrowHarvest.Select(h =>
        {
            var person = world.Person(h.PersonKey);
            return new[]
            {
                h.Date.ToString("yyyy-MM-dd", Invariant), PatchworkCast.Client, "Ostrevane design retainer", "OST-DES", h.Task, h.Notes,
                Dec(h.Hours), Dec(Math.Round(h.Hours * 4, MidpointRounding.AwayFromZero) / 4), h.Billable ? "Yes" : "No", "No", "Yes",
                person.FirstName, person.LastName, person.Key == "tbrandt" ? "No" : "Yes", person.Role, Money(h.Rate), Money(h.Rate * h.Hours),
                "", "", "British Pound - GBP", ""
            };
        }));

    // ---- Ombreton: Jira export taken three weeks earlier, older headers ("Issue key") and repeated "Log Work" columns.

    private static string OmbretonJiraCsv(PatchworkWorld world)
    {
        var logColumns = world.OmbretonItems.Max(i => i.Logs.Count);
        var header = new List<string>
        {
            "Summary", "Issue key", "Issue id", "Issue Type", "Status", "Project key", "Project name", "Priority", "Assignee", "Reporter",
            "Created", "Updated", "Resolved", "Due date", "Original Estimate", "Remaining Estimate", "Time Spent"
        };
        header.AddRange(Enumerable.Repeat("Log Work", logColumns));
        return Csv(header, world.OmbretonItems.Select(i =>
        {
            var row = new List<string>
            {
                i.Summary, i.Key, i.Id.ToString(Invariant), i.Type, i.Status, "OMB", "Ostrevane data migration", i.Priority, Name(world, i.AssigneeKey),
                Name(world, i.ReporterKey), JiraDate(i.Created), JiraDate(i.Updated), i.Resolved is { } r ? JiraDate(r) : "",
                i.Due is { } due ? JiraDate(due.ToDateTime(TimeOnly.MinValue)) : "", Seconds(i.EstimateHours),
                Seconds(i.EstimateHours is { } e ? Math.Max(0, i.IsDone ? 0 : e - i.SpentHours) : null), i.SpentHours > 0 ? Seconds(i.SpentHours) : ""
            };
            row.AddRange(i.Logs.Select(l => $"{l.Comment};{JiraDate(l.StartedLocal)};{l.AuthorUsername};{l.Seconds}"));
            row.AddRange(Enumerable.Repeat("", logColumns - i.Logs.Count));
            return row;
        }));
    }

    // ---- Dunmarrow: monday.com "Export board to Excel": board name, then each group as its own block.

    public static IReadOnlyList<IReadOnlyList<string>> MondayRows(PatchworkWorld world)
    {
        var rows = new List<IReadOnlyList<string>> { new[] { "Ostrevane - Design and accessibility" }, Array.Empty<string>() };
        foreach (var group in world.DunmarrowBoard)
        {
            rows.Add([group.Title]);
            rows.Add(["Name", "Person", "Status", "Timeline", "Due date", "Time Tracking", "Jira link", "Item ID"]);
            foreach (var item in group.Items)
            {
                rows.Add([
                    item.Name, item.PersonKey is { } p ? world.Person(p).FullName : "", item.Status,
                    item.TimelineStart is { } from && item.TimelineEnd is { } to ? $"{Iso(from)} - {Iso(to)}" : "",
                    item.Due is { } due ? Iso(due) : "", item.TrackedHours is { } tracked ? HoursMinutesSeconds(tracked) : "",
                    item.JiraLink ?? "", item.ItemId.ToString(Invariant)
                ]);
                if (item.Subitems.Count > 0)
                {
                    rows.Add(["", "Subitems", "Owner", "Status", "Date", "Item ID"]);
                    rows.AddRange(item.Subitems.Select(s => (IReadOnlyList<string>)
                        ["", s.Name, world.Person(s.PersonKey).FullName, s.Status, s.Date is { } d ? Iso(d) : "", s.ItemId.ToString(Invariant)]));
                }
            }

            rows.Add(Array.Empty<string>());
        }

        return rows;
    }

    private static byte[] MondayXlsx(PatchworkWorld world) => Xlsx.Write("Ostrevane - Design and accessibility", MondayRows(world));

    // ---- Formatting.

    private static string Name(PatchworkWorld world, string? key) => key is null ? "" : world.Person(key).FullName;

    private static string StatusCategory(string status) => status switch
    {
        "Done" => "Done",
        "To Do" => "To Do",
        _ => "In Progress"
    };

    /// <summary>Jira's export style: 02/Oct/26 9:00 AM.</summary>
    private static string JiraDate(DateTime value) => value.ToString("dd/MMM/yy h:mm tt", Invariant);

    private static string Seconds(decimal? hours) => hours is { } h ? ((long)(h * 3600)).ToString(Invariant) : "";

    private static string Milliseconds(decimal? hours) => hours is { } h ? ((long)(h * 3_600_000)).ToString(Invariant) : "";

    private static string Duration(decimal? hours)
    {
        if (hours is not { } h)
        {
            return "";
        }

        var minutes = (int)Math.Round(h * 60);
        return minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}m";
    }

    private static string HourMinutes(decimal hours)
    {
        var minutes = (int)Math.Round(hours * 60);
        return $"{minutes / 60}:{minutes % 60:00}";
    }

    private static string HoursMinutesSeconds(decimal hours)
    {
        var minutes = (int)Math.Round(hours * 60);
        return $"{minutes / 60}:{minutes % 60:00}:00";
    }

    private static string Posix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds().ToString(Invariant);

    private static string Posix(DateOnly? day) => day is { } d ? Posix(d.ToDateTime(new TimeOnly(4, 0))) : "";

    private static string UsText(DateTime value) => value.ToString("M/d/yyyy, h:mm:ss tt", Invariant);

    private static string UsDate(DateOnly? day) => day is { } d ? d.ToString("M/d/yyyy", Invariant) : "";

    private static string Uk(DateOnly day) => day.ToString("dd/MM/yyyy", Invariant);

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", Invariant);

    private static string Dec(decimal value) => value.ToString("0.##", Invariant);

    private static string Money(decimal value) => value.ToString("0.00", Invariant);

    private static byte[] Utf8(string text) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    /// <summary>RFC 4180: CRLF rows, a field quoted when it holds a comma, quote or line break.</summary>
    public static string Csv(IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
    {
        var builder = new StringBuilder();
        void Line(IEnumerable<string> fields) => builder.Append(string.Join(',', fields.Select(Quote))).Append("\r\n");
        Line(header);
        foreach (var row in rows)
        {
            Line(row);
        }

        return builder.ToString();
    }

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}

/// <summary>
/// The smallest workbook Excel, LibreOffice and Google Sheets all open: one
/// sheet, every cell an inline string. Written by hand so the test project
/// needs no spreadsheet library, with fixed entry times so the bytes depend
/// only on the content.
/// </summary>
public static class Xlsx
{
    public const string SheetPath = "xl/worksheets/sheet1.xml";

    private static readonly DateTimeOffset FixedTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static byte[] Write(string sheetName, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "</Types>");
            Add(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");
            Add(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                $"<sheets><sheet name=\"{SecurityElement.Escape(sheetName[..Math.Min(31, sheetName.Length)])}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "</Relationships>");
            Add(zip, SheetPath, SheetXml(rows));
        }

        return stream.ToArray();
    }

    public static string SheetXml(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var r = 0; r < rows.Count; r++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Count; c++)
            {
                if (rows[r][c].Length == 0)
                {
                    continue;
                }

                builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{Column(c)}{r + 1}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(rows[r][c])}</t></is></c>");
            }

            builder.Append("</row>");
        }

        return builder.Append("</sheetData></worksheet>").ToString();
    }

    public static string ReadSheet(byte[] workbook)
    {
        using var zip = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry(SheetPath)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTime;
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Column(int index)
    {
        var name = string.Empty;
        for (index++; index > 0; index = (index - 1) / 26)
        {
            name = (char)('A' + (index - 1) % 26) + name;
        }

        return name;
    }
}
