namespace ProgrammePulse.Tests.Scenarios.Patchwork;

/// <summary>
/// The Patchwork scenario: one client programme delivered by four suppliers,
/// each working in its own tool. Every organisation, person and domain is
/// invented; domains use the reserved <c>.example</c> top-level domain, and the
/// organisation names were checked against company registers and web search
/// before use (1 Oct 2026). Nothing here describes a real organisation.
/// </summary>
public static class PatchworkCast
{
    public const string Client = "Ostrevane Transit";
    public const string Programme = "Fares and Journeys Replatform";
    public const string Kelvaro = "Kelvaro Digital";
    public const string Brantoft = "Brantoft Systems";
    public const string Dunmarrow = "Dunmarrow Studio";
    public const string Ombreton = "Ombreton Labs";

    public static readonly IReadOnlyList<string> Organisations = [Client, Kelvaro, Brantoft, Dunmarrow, Ombreton];
}

public sealed record PatchworkPerson(string Key, string FullName, string Organisation, string Role, string Domain)
{
    public string Email => $"{FullName.ToLowerInvariant().Replace(' ', '.')}@{Domain}";

    public string FirstName => FullName.Split(' ')[0];

    public string LastName => FullName.Split(' ')[^1];
}

public sealed record JiraLog(string Comment, DateTime StartedLocal, string AuthorUsername, int Seconds);

public sealed record JiraItem(
    string Key, long Id, string Type, string Summary, string Status, string? ParentKey, string? AssigneeKey, string ReporterKey,
    DateTime Created, DateTime Updated, DateTime? Resolved, DateOnly? Due, decimal? EstimateHours, string? Sprint, string Priority)
{
    public decimal SpentHours { get; set; }

    public List<JiraLog> Logs { get; } = [];

    public bool IsDone => Status == "Done";

    public bool IsEpic => Type == "Epic";
}

public sealed record TempoLog(long WorklogId, string IssueKey, string IssueSummary, string PersonKey, DateOnly Date, decimal Hours, bool Billable, string Description);

public sealed record ClickUpTask(
    string Id, string Name, string Content, string Status, string List, DateTime CreatedUtc, DateOnly? Start, DateOnly? Due,
    IReadOnlyList<string> AssigneeKeys, string Priority, decimal? EstimateHours, string[] Tags)
{
    public decimal SpentHours { get; set; }
}

public sealed record ClickUpTime(string EntryId, string PersonKey, string TaskId, DateTime StartUtc, decimal Hours, string Description, bool Billable);

public sealed record MondaySubitem(long ItemId, string Name, string PersonKey, string Status, DateOnly? Date);

public sealed record MondayItem(
    long ItemId, string Name, string? PersonKey, string Status, DateOnly? TimelineStart, DateOnly? TimelineEnd, DateOnly? Due,
    decimal? TrackedHours, string? JiraLink, IReadOnlyList<MondaySubitem> Subitems);

public sealed record MondayGroup(string Title, IReadOnlyList<MondayItem> Items);

public sealed record HarvestEntry(DateOnly Date, string PersonKey, string Task, string Notes, decimal Hours, bool Billable, decimal Rate);

public sealed record PlannerRow(DateOnly Date, string ResourceName, string Category, decimal Booked, decimal? Actual, string Note);

public sealed record Milestone(string Id, string Name, string Supplier, DateOnly Baseline, DateOnly Forecast, string Rag, DateOnly LastUpdated, string Notes);

/// <summary>
/// Builds the whole scenario from one date. Everything is relative to
/// <see cref="AsOf"/> (the day the exports were taken), so the same story can
/// be regenerated for any week; the structure, names and planted problems
/// never change.
/// </summary>
public sealed class PatchworkWorld
{
    public const int OmbretonDaysStale = 21;
    public const string CurrentSprint = "KEL Sprint 9";

    private static readonly TimeSpan Morning = TimeSpan.FromHours(9);

    public PatchworkWorld(DateOnly asOf)
    {
        AsOf = asOf;
        PeriodStart = MondayOf(asOf).AddDays(-49);
        People = BuildPeople();
        KelvaroItems = BuildKelvaroItems();
        OmbretonItems = BuildOmbretonItems();
        BrantoftTasks = BuildBrantoftTasks();
        DunmarrowBoard = BuildDunmarrowBoard();
        Milestones = BuildMilestones();
        DoubleLoggedDays = WorkDays().Where(d => d.DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Friday)
            .Where(d => WeekIndex(d) is 2 or 3 or 4).ToList();
        KelvaroWorklogs = BuildTempo();
        BrantoftTime = BuildClickUpTime();
        OmbretonLogs();
        DunmarrowHarvest = BuildHarvest();
        OstrevaneTimesheet = BuildPlanner();
    }

    public DateOnly AsOf { get; }

    /// <summary>Monday seven weeks before the week of <see cref="AsOf"/>: eight weeks of history.</summary>
    public DateOnly PeriodStart { get; }

    public DateOnly OmbretonExtractedOn => AsOf.AddDays(-OmbretonDaysStale);

    public IReadOnlyList<PatchworkPerson> People { get; }

    public IReadOnlyList<JiraItem> KelvaroItems { get; }

    public IReadOnlyList<TempoLog> KelvaroWorklogs { get; }

    public IReadOnlyList<JiraItem> OmbretonItems { get; }

    public IReadOnlyList<ClickUpTask> BrantoftTasks { get; }

    public IReadOnlyList<ClickUpTime> BrantoftTime { get; }

    public IReadOnlyList<MondayGroup> DunmarrowBoard { get; }

    public IReadOnlyList<HarvestEntry> DunmarrowHarvest { get; }

    public IReadOnlyList<PlannerRow> OstrevaneTimesheet { get; }

    public IReadOnlyList<Milestone> Milestones { get; }

    /// <summary>Days on which Rafe Lindqvist recorded a full day with both Kelvaro (Tempo) and Brantoft (ClickUp).</summary>
    public IReadOnlyList<DateOnly> DoubleLoggedDays { get; }

    public PatchworkPerson Person(string key) => People.Single(p => p.Key == key);

    public DateOnly D(int days) => AsOf.AddDays(days);

    public IEnumerable<DateOnly> WorkDays(DateOnly? until = null)
    {
        var last = until ?? AsOf.AddDays(-1);
        for (var day = PeriodStart; day <= last; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                yield return day;
            }
        }
    }

    public int WeekIndex(DateOnly day) => (day.DayNumber - PeriodStart.DayNumber) / 7;

    public static DateTime LocalMorning(DateOnly day) => day.ToDateTime(TimeOnly.FromTimeSpan(Morning));

    private DateTime At(int days, int hour = 10) => D(days).ToDateTime(new TimeOnly(hour, 0));

    private static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static IReadOnlyList<PatchworkPerson> BuildPeople() =>
    [
        new("ifarrant", "Imogen Farrant", PatchworkCast.Client, "Programme Director", "ostrevane.example"),
        new("cree", "Callum Ree", PatchworkCast.Client, "PMO Analyst", "ostrevane.example"),
        new("nokonjo", "Nadia Okonjo", PatchworkCast.Client, "Product Owner", "ostrevane.example"),
        new("twilk", "Tomasz Wilk", PatchworkCast.Client, "Payments Lead", "ostrevane.example"),

        new("praman", "Priya Raman", PatchworkCast.Kelvaro, "Delivery Manager", "kelvaro.example"),
        new("ohartley", "Owen Hartley", PatchworkCast.Kelvaro, "iOS Developer", "kelvaro.example"),
        new("lhaddad", "Leila Haddad", PatchworkCast.Kelvaro, "Android Developer", "kelvaro.example"),
        new("mcosta", "Mateus Costa", PatchworkCast.Kelvaro, "Front-end Developer", "kelvaro.example"),
        new("gnwosu", "Grace Nwosu", PatchworkCast.Kelvaro, "QA Engineer", "kelvaro.example"),
        new("rlindqvist", "Rafe Lindqvist", PatchworkCast.Kelvaro, "Developer, seconded to Brantoft two days a week", "kelvaro.example"),

        new("sadeyemi", "Samuel Adeyemi", PatchworkCast.Brantoft, "Technical Lead", "brantoft.example"),
        new("jcarrow", "Jessica Carrow", PatchworkCast.Brantoft, "Backend Developer", "brantoft.example"),
        new("amehta", "Arun Mehta", PatchworkCast.Brantoft, "Backend Developer", "brantoft.example"),
        new("bkowalczyk", "Bea Kowalczyk", PatchworkCast.Brantoft, "DevOps Engineer", "brantoft.example"),

        new("fcastell", "Freya Castell", PatchworkCast.Dunmarrow, "UX Lead", "dunmarrow.example"),
        new("jwhitlow", "Joss Whitlow", PatchworkCast.Dunmarrow, "Product Designer", "dunmarrow.example"),
        new("mvega", "Marisol Vega", PatchworkCast.Dunmarrow, "Accessibility Specialist", "dunmarrow.example"),
        new("tbrandt", "Teo Brandt", PatchworkCast.Dunmarrow, "Illustrator (contractor)", "dunmarrow.example"),

        new("hsato", "Hana Sato", PatchworkCast.Ombreton, "Data Engineer", "ombreton.example"),
        new("eteague", "Ewan Teague", PatchworkCast.Ombreton, "Data Engineer", "ombreton.example"),
    ];

    /// <summary>How Brantoft's ClickUp shows its people; the client's planner uses their full names.</summary>
    public static string ClickUpName(PatchworkPerson person) => person.Key switch
    {
        "sadeyemi" => "Sam Adeyemi",
        "jcarrow" => "Jess Carrow",
        _ => person.FullName
    };

    /// <summary>Ombreton's Jira worklogs carry the author's account name, not the display name.</summary>
    public static string JiraUsername(PatchworkPerson person) => person.FullName.ToLowerInvariant().Replace(' ', '.');

    private IReadOnlyList<JiraItem> BuildKelvaroItems()
    {
        var items = new List<JiraItem>();
        long id = 10400;
        void Add(string key, string type, string summary, string status, string? parent, string? assignee, int? due, decimal? estimate, string? sprint, string priority = "Medium", int created = -60)
        {
            id += 3;
            var resolved = status == "Done" && due is { } d ? At(d - 1, 16) : (DateTime?)null;
            var updated = resolved ?? At(status == "To Do" ? -6 : -1, 15);
            items.Add(new JiraItem(key, id, type, summary, status, parent, assignee, "praman", At(created, 11), updated, resolved,
                due is { } offset ? D(offset) : null, estimate, sprint, priority));
        }

        Add("KEL-1", "Epic", "Journey planner", "In Progress", null, "praman", null, null, null, created: -70);
        Add("KEL-2", "Epic", "Ticket wallet", "In Progress", null, "praman", null, null, null, created: -70);
        Add("KEL-3", "Epic", "Mobile beta", "In Progress", null, "praman", null, null, null, created: -70);
        Add("KEL-4", "Epic", "Accessibility fixes", "In Progress", null, "praman", null, null, null, created: -70);

        Add("KEL-5", "Story", "Journey search results list", "Done", "KEL-1", "mcosta", -30, 16, "KEL Sprint 7", created: -64);
        Add("KEL-6", "Story", "Saved journeys", "Done", "KEL-1", "mcosta", -24, 12, "KEL Sprint 7", created: -63);
        Add("KEL-8", "Story", "Live departure board", "In Progress", "KEL-1", "lhaddad", 4, 20, CurrentSprint, "High", created: -40);
        Add("KEL-9", "Story", "Disruption alerts by push notification", "To Do", "KEL-1", null, 9, 12, CurrentSprint, created: -30);
        Add("KEL-10", "Story", "Step-free route option", "In Review", "KEL-1", "ohartley", 2, 10, CurrentSprint, created: -35);
        Add("KEL-11", "Task", "Journey planner API error states", "Done", "KEL-1", "gnwosu", -12, 6, "KEL Sprint 8", created: -45);
        Add("KEL-13", "Story", "Wallet: buy a single ticket", "Done", "KEL-2", "ohartley", -18, 24, "KEL Sprint 8", "High", created: -58);
        Add("KEL-14", "Story", "Wallet: season ticket renewal", "In Progress", "KEL-2", "lhaddad", -5, 20, CurrentSprint, "High", created: -50);
        Add("KEL-15", "Story", "Wallet: refund request", "To Do", "KEL-2", null, 6, null, CurrentSprint, created: -28);
        Add("KEL-16", "Story", "Ticket QR code works offline", "Blocked", "KEL-2", "ohartley", -3, 14, CurrentSprint, "High", created: -44);
        Add("KEL-17", "Story", "Contactless top-up", "To Do", "KEL-2", "mcosta", 14, null, CurrentSprint, created: -26);
        Add("KEL-18", "Task", "Beta build pipeline (TestFlight and Play internal track)", "In Progress", "KEL-3", "rlindqvist", -8, 8, CurrentSprint, "High", created: -55);
        Add("KEL-19", "Task", "Beta crash reporting", "To Do", "KEL-3", null, -2, 6, CurrentSprint, created: -30);
        Add("KEL-20", "Story", "Beta feedback form", "In Progress", "KEL-3", "mcosta", 3, 8, CurrentSprint, created: -32);
        Add("KEL-21", "Task", "Beta release notes", "To Do", "KEL-3", "praman", 5, null, CurrentSprint, created: -20);
        Add("KEL-22", "Task", "Beta test plan", "Done", "KEL-3", "gnwosu", -15, 10, "KEL Sprint 8", created: -48);
        Add("KEL-23", "Story", "Screen reader labels in the wallet", "Done", "KEL-4", "lhaddad", -9, 8, "KEL Sprint 8", created: -42);
        Add("KEL-24", "Story", "Dynamic type support", "In Progress", "KEL-4", "ohartley", 8, null, CurrentSprint, created: -25);
        Add("KEL-25", "Story", "Colour contrast fixes", "In Review", "KEL-4", "mcosta", 1, 6, CurrentSprint, created: -27);
        Add("KEL-26", "Story", "Focus order on journey results", "To Do", "KEL-4", "gnwosu", 10, 4, CurrentSprint, created: -22);
        Add("KEL-27", "Task", "Regression suite: wallet", "Done", "KEL-2", "gnwosu", -20, 12, "KEL Sprint 8", created: -52);
        Add("KEL-28", "Task", "Analytics events", "To Do", "KEL-1", "praman", 21, 6, null, "Low", created: -18);
        Add("KEL-29", "Task", "App store listing copy", "To Do", "KEL-3", null, 18, 3, null, "Low", created: -16);
        Add("KEL-30", "Task", "Performance budget", "Done", "KEL-1", "mcosta", -40, 8, "KEL Sprint 7", created: -66);
        return items;
    }

    private IReadOnlyList<JiraItem> BuildOmbretonItems()
    {
        var items = new List<JiraItem>();
        long id = 20100;
        var extracted = OmbretonExtractedOn;
        void Add(string key, string summary, string status, string? assignee, int dueFromExtract, decimal estimate, int createdFromExtract)
        {
            id += 7;
            var due = extracted.AddDays(dueFromExtract);
            var resolved = status == "Done" ? due.AddDays(-1).ToDateTime(new TimeOnly(15, 30)) : (DateTime?)null;
            var updated = resolved ?? extracted.AddDays(-1).ToDateTime(new TimeOnly(17, 10));
            items.Add(new JiraItem(key, id, "Task", summary, status, null, assignee, "hsato",
                extracted.AddDays(createdFromExtract).ToDateTime(new TimeOnly(10, 0)), updated, resolved, due, estimate, null, "Medium"));
        }

        Add("OMB-1", "Profile legacy fares data", "Done", "hsato", -29, 16, -40);
        Add("OMB-2", "Map customer accounts to the new schema", "Done", "eteague", -19, 24, -38);
        Add("OMB-3", "Migration scripts: tickets and passes", "In Progress", "hsato", -4, 32, -30);
        Add("OMB-4", "Migration dry run", "In Progress", "eteague", -3, 16, -26);
        Add("OMB-5", "Reconciliation report: legacy against new", "To Do", "hsato", 7, 12, -20);
        Add("OMB-6", "Cut-over rehearsal", "To Do", "eteague", 26, 8, -15);
        Add("OMB-7", "Data retention sign-off", "To Do", null, 31, 4, -12);
        return items;
    }

    private IReadOnlyList<ClickUpTask> BuildBrantoftTasks()
    {
        var tasks = new List<ClickUpTask>();
        var n = 0;
        void Add(string name, string content, string status, string list, string[] assignees, int? start, int? due, decimal? estimate, string priority, params string[] tags)
        {
            n++;
            var taskId = "86c" + ToBase36(n * 7919 + 104729).PadLeft(6, '0');
            tasks.Add(new ClickUpTask(taskId, name, content, status, list, DateTime.SpecifyKind(At(-62 + n, 8), DateTimeKind.Utc),
                start is { } s ? D(s) : null, due is { } d ? D(d) : null, assignees, priority, estimate, tags));
        }

        Add("Fare rules engine", "Zones, caps and concessions as data, not code.", "complete", "Ticketing API", ["sadeyemi"], -55, -35, 40, "high", "api");
        Add("Ticket issuance endpoint", "", "complete", "Ticketing API", ["jcarrow"], -40, -25, 32, "high", "api");
        Add("Ticket validation endpoint", "", "complete", "Ticketing API", ["amehta"], -32, -18, 24, "normal", "api");
        Add("API beta release", "Milestone M2.", "complete", "Ticketing API", ["sadeyemi"], -14, -10, 8, "urgent", "api", "milestone");
        Add("Rate limiting", "", "in progress", "Ticketing API", ["amehta"], -9, 5, 12, "normal", "api");
        Add("Journey planner integration", "Kelvaro app calls this; see KEL-8.", "review", "Ticketing API", ["jcarrow"], -12, 3, 16, "normal", "api");
        Add("Partner API documentation", "", "parked", "Ticketing API", [], -20, -6, 10, "low", "docs");
        Add("Payment provider sandbox", "", "complete", "Payments", ["jcarrow"], -38, -20, 20, "high", "payments");
        Add("Tokenisation and stored cards", "", "in progress", "Payments", ["jcarrow"], -15, 6, 30, "high", "payments");
        Add("Refunds API", "", "to do", "Payments", ["amehta"], 2, 15, 24, "normal", "payments");
        Add("Payment provider cut-over", "Milestone M5. Provider confirmed a later slot on our side.", "to do", "Payments", ["sadeyemi"], 20, 29, 16, "urgent", "payments", "milestone");
        Add("PCI evidence pack", "Waiting for Ostrevane's QSA questionnaire.", "awaiting client", "Payments", ["sadeyemi"], -16, -4, 12, "high", "payments", "compliance");
        Add("Chargeback webhook", "", "awaiting client", "Payments", ["jcarrow"], -14, -9, 8, "normal", "payments");
        Add("Payment failure alerting", "", "to do", "Payments", ["bkowalczyk"], 4, 11, 6, "normal", "payments", "ops");
        Add("Production environment", "", "complete", "Platform", ["bkowalczyk"], -50, -30, 24, "high", "ops");
        Add("Blue-green deploys", "", "in progress", "Platform", ["bkowalczyk"], -10, 2, 16, "normal", "ops");
        Add("Load test: ticket issuance", "Rafe is on this Thursdays and Fridays.", "in progress", "Platform", ["rlindqvist"], -35, 7, 12, "normal", "ops");
        Add("Observability dashboards", "", "review", "Platform", ["bkowalczyk"], -20, 4, 10, "normal", "ops");
        Add("Secrets rotation", "", "blocked", "Platform", ["bkowalczyk"], -12, -1, 6, "high", "ops", "security");
        Add("Disaster recovery runbook", "", "to do", "Platform", [], 10, 20, 8, "low", "ops");
        return tasks;
    }

    private IReadOnlyList<MondayGroup> BuildDunmarrowBoard()
    {
        long id = 7182640000;
        MondayItem Item(string name, string? person, string status, int? from, int? to, int? due, decimal? tracked, string? jira, params MondaySubitem[] subitems)
        {
            id += 11;
            return new MondayItem(id, name, person, status, from is { } f ? D(f) : null, to is { } t ? D(t) : null, due is { } d ? D(d) : null, tracked, jira, subitems);
        }

        MondaySubitem Sub(string name, string person, string status, int? date)
        {
            id += 3;
            return new MondaySubitem(id, name, person, status, date is { } d ? D(d) : null);
        }

        return
        [
            new("In progress",
            [
                Item("Beta design sign-off", "fcastell", "Stuck", -10, -4, -4, 6m, "KEL-3"),
                Item("Accessibility audit: wallet", "mvega", "Working on it", -6, 5, 5, 18m, "KEL-4",
                    Sub("Screen reader pass", "mvega", "Done", -2),
                    Sub("Contrast pass", "mvega", "Working on it", 3)),
                Item("Payment screens final", "jwhitlow", "Working on it", -3, 7, 7, 9.5m, null),
                Item("Illustrations: onboarding", null, "Working on it", -12, 2, 2, null, null),
            ]),
            new("Up next",
            [
                Item("Accessibility audit: journey planner", "mvega", "Not started", 6, 15, 15, null, "KEL-4"),
                Item("App store screenshots", "jwhitlow", "Not started", 10, 18, 18, null, "KEL-3"),
                Item("Content style guide", "mvega", "Not started", 12, 20, 20, null, null),
            ]),
            new("Done",
            [
                Item("Discovery synthesis", "fcastell", "Done", -60, -46, -46, 32m, null),
                Item("Design system tokens", "jwhitlow", "Done", -45, -30, -30, 40.5m, null),
                Item("Journey planner flows", "jwhitlow", "Done", -35, -22, -22, 36m, "KEL-1"),
                Item("Wallet flows", "jwhitlow", "Done", -28, -15, -15, 30m, "KEL-2"),
            ]),
        ];
    }

    private IReadOnlyList<Milestone> BuildMilestones() =>
    [
        new("M1", "Discovery complete", PatchworkCast.Dunmarrow, D(-46), D(-46), "Complete", D(-45), ""),
        new("M2", "Ticketing API beta", PatchworkCast.Brantoft, D(-10), D(-10), "Complete", D(-9), ""),
        new("M3", "Mobile app beta", PatchworkCast.Kelvaro, D(-6), D(-6), "G", D(-12), "On track per supplier update"),
        new("M4", "Accessibility audit (wallet) passed", PatchworkCast.Dunmarrow, D(5), D(5), "A", D(-3), "Audit started"),
        new("M5", "Payment provider cut-over", PatchworkCast.Brantoft, D(12), D(12), "G", D(-10), ""),
        new("M6", "Data migration dry run", PatchworkCast.Ombreton, D(-24), D(-24), "Complete", D(-20), "Confirmed on weekly call"),
        new("M7", "Public launch", PatchworkCast.Client, D(40), D(40), "A", D(-3), "Depends on M3 and M5"),
    ];

    private IReadOnlyList<TempoLog> BuildTempo()
    {
        var logs = new List<TempoLog>();
        long worklogId = 300000;
        var items = KelvaroItems.ToDictionary(i => i.Key);
        void Log(string key, string person, DateOnly day, decimal hours, string description, bool billable = true)
        {
            worklogId += 13;
            var summary = items.TryGetValue(key, out var item) ? item.Summary : key == "KEL-7" ? "Offline journey cache" : "Map tiles licence check";
            logs.Add(new TempoLog(worklogId, key, summary, person, day, hours, billable, description));
            if (item is not null)
            {
                item.SpentHours += hours;
            }
        }

        var days = WorkDays().ToList();
        string Pick(string person, int dayIndex)
        {
            var own = KelvaroItems.Where(i => !i.IsEpic && i.AssigneeKey == person && i.Status != "To Do")
                .OrderBy(i => i.Resolved ?? DateTime.MaxValue).ThenBy(i => i.Created).ToList();
            return own.Count == 0 ? "KEL-3" : own[Math.Min(dayIndex * own.Count / days.Count, own.Count - 1)].Key;
        }

        var orphanTuesdays = days.Where(d => d.DayOfWeek == DayOfWeek.Tuesday).Take(3).ToHashSet();
        var orphanWednesdays = days.Where(d => d.DayOfWeek == DayOfWeek.Wednesday).Take(3).ToHashSet();

        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            Log(day.DayOfWeek == DayOfWeek.Monday ? "KEL-3" : "KEL-1", "praman", day, 2m, "Planning and stand-ups");
            Log("KEL-3", "praman", day, 2m, "Beta coordination with Ostrevane");

            Log(Pick("ohartley", i), "ohartley", day, 7.5m, "Development");

            if (orphanWednesdays.Contains(day))
            {
                Log("KEL-12", "lhaddad", day, 3.5m, "Licence review");
                Log(Pick("lhaddad", i), "lhaddad", day, 4m, "Development");
            }
            else
            {
                Log(Pick("lhaddad", i), "lhaddad", day, 7.5m, "Development");
            }

            if (orphanTuesdays.Contains(day))
            {
                Log("KEL-7", "mcosta", day, 4m, "Offline cache spike");
                Log(Pick("mcosta", i), "mcosta", day, 3.5m, "Development");
            }
            else
            {
                Log(Pick("mcosta", i), "mcosta", day, 7.5m, "Development");
            }

            Log(Pick("gnwosu", i), "gnwosu", day, 7.5m, "Testing");

            if (day.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday || DoubleLoggedDays.Contains(day))
            {
                Log("KEL-18", "rlindqvist", day, 7.5m, "Beta pipeline");
            }
        }

        return logs;
    }

    private IReadOnlyList<ClickUpTime> BuildClickUpTime()
    {
        var entries = new List<ClickUpTime>();
        var n = 0;
        var tasks = BrantoftTasks.ToDictionary(t => t.Id);
        void Track(ClickUpTask task, string person, DateOnly day, decimal hours, string description)
        {
            n++;
            entries.Add(new ClickUpTime(ToBase36(1_000_000 + n * 37).ToLowerInvariant(), person, task.Id, DateTime.SpecifyKind(LocalMorning(day).AddHours(-1), DateTimeKind.Utc), hours, description, true));
            tasks[task.Id].SpentHours += hours;
        }

        var days = WorkDays().ToList();
        ClickUpTask Pick(string person, int dayIndex)
        {
            var own = BrantoftTasks.Where(t => t.AssigneeKeys.Contains(person) && t.Status != "to do").OrderBy(t => t.Start).ToList();
            return own[Math.Min(dayIndex * own.Count / days.Count, own.Count - 1)];
        }

        var loadTest = BrantoftTasks.Single(t => t.Name == "Load test: ticket issuance");
        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            Track(Pick("sadeyemi", i), "sadeyemi", day, 7.5m, "");
            Track(Pick("jcarrow", i), "jcarrow", day, 7m, "");
            Track(Pick("amehta", i), "amehta", day, 7.5m, "");
            Track(Pick("bkowalczyk", i), "bkowalczyk", day, day.DayOfWeek == DayOfWeek.Friday ? 3.75m : 7.5m, "");
            if (day.DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Friday)
            {
                Track(loadTest, "rlindqvist", day, 7.5m, "Load test scripts");
            }
        }

        return entries;
    }

    private void OmbretonLogs()
    {
        var items = OmbretonItems.ToDictionary(i => i.Key);
        var plan = new (string Key, string Person, DateOnly From, DateOnly To)[]
        {
            ("OMB-1", "hsato", OmbretonExtractedOn.AddDays(-40), OmbretonExtractedOn.AddDays(-30)),
            ("OMB-3", "hsato", OmbretonExtractedOn.AddDays(-29), OmbretonExtractedOn.AddDays(-1)),
            ("OMB-2", "eteague", OmbretonExtractedOn.AddDays(-38), OmbretonExtractedOn.AddDays(-20)),
            ("OMB-4", "eteague", OmbretonExtractedOn.AddDays(-19), OmbretonExtractedOn.AddDays(-1)),
        };
        foreach (var (key, person, from, to) in plan)
        {
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || day < PeriodStart)
                {
                    continue;
                }

                items[key].Logs.Add(new JiraLog(key == "OMB-4" ? "Dry run prep" : "Scripts and checks", LocalMorning(day),
                    JiraUsername(Person(person)), 27000));
                items[key].SpentHours += 7.5m;
            }
        }
    }

    private IReadOnlyList<HarvestEntry> BuildHarvest()
    {
        var entries = new List<HarvestEntry>();
        var items = DunmarrowBoard.SelectMany(g => g.Items).ToList();
        string Working(string person, DateOnly day)
        {
            var active = items.Where(i => i.PersonKey == person && i.TimelineStart <= day && (i.TimelineEnd ?? day) >= day).Select(i => i.Name).FirstOrDefault();
            return active ?? (person == "mvega" ? "Accessibility review" : "Design QA");
        }

        foreach (var day in WorkDays())
        {
            if (day.DayOfWeek == DayOfWeek.Monday)
            {
                entries.Add(new(day, "fcastell", "Meetings", "Weekly call with Ostrevane", 1m, true, 110m));
                entries.Add(new(day, "fcastell", "Design", Working("fcastell", day), 5m, true, 110m));
            }
            else
            {
                entries.Add(new(day, "fcastell", day.DayOfWeek == DayOfWeek.Friday ? "Project Management" : "Design",
                    day.DayOfWeek == DayOfWeek.Friday ? "Status report and planning" : Working("fcastell", day), 6m, true, 110m));
            }

            entries.Add(new(day, "jwhitlow", "Design", Working("jwhitlow", day), 7.5m, true, 95m));

            if (WeekIndex(day) >= 2)
            {
                entries.Add(new(day, "mvega", "Accessibility", Working("mvega", day), 7m, true, 100m));
            }

            if (WeekIndex(day) >= 2 && day.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Thursday)
            {
                entries.Add(new(day, "tbrandt", "Illustration", "Onboarding illustrations", 4m, true, 85m));
            }
        }

        return entries;
    }

    private IReadOnlyList<PlannerRow> BuildPlanner()
    {
        var rows = new List<PlannerRow>();
        var bookings = new (string Person, string Category, decimal Booked, decimal? Actual, int FromWeek)[]
        {
            ("ifarrant", "Ostrevane staff", 3.75m, 4.5m, 0),
            ("cree", "Ostrevane staff", 7.5m, 7.5m, 0),
            ("nokonjo", "Ostrevane staff", 5m, 6m, 0),
            ("twilk", "Ostrevane staff", 2m, 2.5m, 3),
            ("praman", "Supplier capacity: Kelvaro", 3.75m, null, 0),
            ("ohartley", "Supplier capacity: Kelvaro", 7.5m, null, 0),
            ("lhaddad", "Supplier capacity: Kelvaro", 7.5m, null, 0),
            ("mcosta", "Supplier capacity: Kelvaro", 7.5m, null, 0),
            ("gnwosu", "Supplier capacity: Kelvaro", 3.75m, null, 0),
            ("sadeyemi", "Supplier capacity: Brantoft", 7.5m, null, 0),
            ("jcarrow", "Supplier capacity: Brantoft", 7.5m, null, 0),
            ("amehta", "Supplier capacity: Brantoft", 7.5m, null, 0),
            ("bkowalczyk", "Supplier capacity: Brantoft", 3.75m, null, 0),
            ("fcastell", "Supplier capacity: Dunmarrow", 3.75m, null, 0),
            ("jwhitlow", "Supplier capacity: Dunmarrow", 7.5m, null, 0),
            ("mvega", "Supplier capacity: Dunmarrow", 7.5m, null, 2),
            ("hsato", "Supplier capacity: Ombreton", 7.5m, null, 0),
            ("eteague", "Supplier capacity: Ombreton", 7.5m, null, 0),
        };

        foreach (var day in WorkDays())
        {
            foreach (var (person, category, booked, actual, fromWeek) in bookings)
            {
                if (WeekIndex(day) >= fromWeek)
                {
                    rows.Add(new PlannerRow(day, Person(person).FullName, category, booked, actual, ""));
                }
            }
        }

        return rows;
    }

    private static string ToBase36(int value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var result = string.Empty;
        do
        {
            result = digits[value % 36] + result;
            value /= 36;
        }
        while (value > 0);
        return result;
    }
}
