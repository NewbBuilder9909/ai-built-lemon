using System.Globalization;
using System.Text;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Tests.Personas;

namespace ProgrammePulse.Tests.Demo;

/// <summary>
/// Northstar Digital: a fictional 25-person delivery agency with four client
/// programmes and its own internal work, over twelve weeks of history and
/// eight weeks of forward bookings. Hand-authored so every value case the
/// product is sold on shows up in real pages, and so a failing test names a
/// business rule rather than a random seed:
///
/// - Harbour Health (fixed price) is overrunning: effort above estimate,
///   overdue milestones, blocked integration work, one engineer holding the
///   HL7 knowledge, and a contractor nobody has matched to a profile.
/// - Kestrel Insurance (time and materials) is the healthy comparison.
/// - Civic Transport (retainer) is drifting: support hours above the
///   allowance, statuses nobody mapped, time not linked to work, hours of
///   unknown billability.
/// - Brightwater Retail is pipeline: a draft contract and bookings from
///   week two, which push the shared DevOps specialist past his hours.
///
/// Everything is relative to <c>today</c>, so overdue stays overdue whenever
/// the data is loaded. Nothing here is random: the timesheet generator is a
/// fixed function of the definitions, and <see cref="NorthstarDemoDatasetTests"/>
/// pins the outcomes. People and firms are invented; emails use the
/// reserved <c>.test</c> and <c>.example</c> domains.
/// </summary>
public static class NorthstarDemoDataset
{
    public const int HistoryDays = 84;
    public const int ForwardWeeks = 8;

    public const string Harbour = "Patient Portal Replacement";
    public const string Kestrel = "Claims Modernisation";
    public const string Civic = "Journey Planner Managed Service";
    public const string Brightwater = "E-commerce Replatform";
    public const string Internal = "Northstar Internal";

    // ---- People -------------------------------------------------------------

    /// <param name="Utilisation">Share of contracted hours the person records.</param>
    /// <param name="Fallback">
    /// Work item that takes whatever capacity their own items leave; empty
    /// means time recorded against no item at all; null means unrecorded.
    /// </param>
    /// <param name="StartedDaysAgo">When they began recording time (new starters, contractors).</param>
    public sealed record DemoPerson(
        string Key, string FullName, string Email, string Role, string JobTitle, string Team,
        decimal HoursPerWeek, decimal CostPerHour, decimal Utilisation, string? Fallback,
        int StartedDaysAgo = HistoryDays, PersonaDefinition? Persona = null, bool IsStaff = true);

    private static DemoPerson Persona(PersonaDefinition persona, string key, string team, decimal cost, decimal utilisation, string? fallback) =>
        new(key, persona.FullName, persona.Email, persona.MemberGroup, persona.JobTitle, team, 37.5m, cost, utilisation, fallback, Persona: persona);

    private static DemoPerson Staff(string key, string name, string role, string title, string team, decimal cost, decimal utilisation,
        string? fallback, decimal hours = 37.5m, int started = HistoryDays) =>
        new(key, name, $"{name.Split(' ')[0].ToLowerInvariant()}.{name.Split(' ')[^1].ToLowerInvariant()}@northstar.test",
            role, title, team, hours, cost, utilisation, fallback, started);

    public static readonly IReadOnlyList<DemoPerson> People =
    [
        Persona(NorthstarPersonas.Developer, "alex", "Health", 46m, 0.92m, "HHT-098"),
        Persona(NorthstarPersonas.ProjectManager, "sarah", "Health", 52m, 0.88m, "HHT-099"),
        Persona(NorthstarPersonas.Board, "james", "Board", 0m, 0m, null),
        Persona(NorthstarPersonas.TenantAdmin, "emma", "Leadership", 78m, 0.30m, "INT-099"),
        Persona(NorthstarPersonas.Analyst, "priya", "Delivery office", 40m, 0.60m, "INT-104"),
        Staff("daniel", "Daniel Okafor", StaffRole.HolidayApprover, "Head of Delivery", "Delivery office", 68m, 0.85m, "KES-098"),
        Staff("hannah", "Hannah Price", StaffRole.TeamLead, "Project Manager", "Public sector", 50m, 0.90m, "CTA-320"),
        Staff("marcus", "Marcus Chen", StaffRole.TeamLead, "Technical Lead", "Health", 70m, 1.05m, "HHT-098"),
        Staff("olivia", "Olivia Bennett", StaffRole.Staff, "Business Analyst", "Health", 44m, 0.85m, "HHT-097"),
        Staff("ravi", "Ravi Kapoor", StaffRole.Staff, "Business Analyst", "Insurance", 44m, 0.80m, "KES-099"),
        Staff("chloe", "Chloe Martin", StaffRole.Staff, "UX Designer", "Health", 45m, 0.90m, "HHT-097"),
        Staff("tom", "Tom Fletcher", StaffRole.Staff, "UX Designer", "Insurance", 45m, 0.85m, "KES-099", hours: 22.5m),
        Staff("aisha", "Aisha Rahman", StaffRole.Staff, "Frontend Developer", "Health", 46m, 0.90m, "HHT-098"),
        Staff("ben", "Ben Carter", StaffRole.Staff, "Frontend Developer", "Insurance", 42m, 0.88m, "KES-099"),
        Staff("lucy", "Lucy Hughes", StaffRole.Staff, "Frontend Developer", "Public sector", 42m, 0.90m, ""),
        Staff("kwame", "Kwame Asante", StaffRole.Staff, "Senior Backend Developer", "Insurance", 58m, 0.95m, "KES-099"),
        Staff("sophie", "Sophie Turner", StaffRole.Staff, "Backend Developer", "Health", 48m, 0.95m, "HHT-098"),
        Staff("mateusz", "Mateusz Nowak", StaffRole.Staff, "Backend Developer", "Insurance", 48m, 0.50m, "INT-099"),
        Staff("grace", "Grace Liu", StaffRole.Staff, "Backend Developer", "Public sector", 48m, 0.92m, "CTA-304"),
        Staff("ellie", "Ellie Robinson", StaffRole.Staff, "QA Engineer", "Health", 40m, 0.90m, "HHT-098"),
        Staff("jack", "Jack Thompson", StaffRole.Staff, "QA Engineer", "Insurance", 40m, 0.88m, "KES-099"),
        Staff("fatima", "Fatima Ali", StaffRole.Staff, "QA Engineer", "Public sector", 40m, 0.80m, "CTA-304"),
        Staff("owen", "Owen Griffiths", StaffRole.Staff, "DevOps Engineer", "Platform", 62m, 1.15m, "INT-099"),
        Staff("niamh", "Niamh Kelly", StaffRole.Staff, "DevOps Engineer", "Platform", 50m, 0.55m, "INT-099", started: 40),
        Staff("leo", "Leo Walsh", StaffRole.Staff, "Solutions Architect", "Leadership", 72m, 0.75m, "INT-099"),
        // A contractor on the Harbour migration. Deliberately not a staff
        // profile: his time and his assignment arrive in the export with an
        // email nobody has matched, so they land on the identity queue.
        new("sam", "Sam Okoro", "sam.okoro@contractor.example", StaffRole.Staff, "Contract Data Engineer", "Health",
            37.5m, 0m, 1.0m, "HHT-214", StartedDaysAgo: 42, IsStaff: false)
    ];

    public static DemoPerson Person(string key) => People.Single(p => p.Key == key);

    // ---- Work items -----------------------------------------------------------

    /// <param name="Spent">Hours the generator records against the item by today.</param>
    public sealed record DemoWorkItem(
        string Programme, string Project, string Workstream, string Id, string Title, string Status, string? Assignee,
        int StartDaysAgo, int? DueInDays, decimal? Estimate, decimal Spent, bool Milestone = false);

    private static DemoWorkItem H(string project, string workstream, string id, string title, string status, string? who, int start, int? due, decimal? est, decimal spent, bool milestone = false) =>
        new(Harbour, project, workstream, id, title, status, who, start, due, est, spent, milestone);

    private static DemoWorkItem K(string project, string workstream, string id, string title, string status, string? who, int start, int? due, decimal? est, decimal spent, bool milestone = false) =>
        new(Kestrel, project, workstream, id, title, status, who, start, due, est, spent, milestone);

    private static DemoWorkItem C(string workstream, string id, string title, string status, string? who, int start, int? due, decimal? est, decimal spent, bool milestone = false) =>
        new(Civic, "Managed service", workstream, id, title, status, who, start, due, est, spent, milestone);

    private static DemoWorkItem B(string id, string title, string status, string? who, int? due, decimal? est, bool milestone = false) =>
        new(Brightwater, "Replatform discovery", "Discovery", id, title, status, who, 0, due, est, 0m, milestone);

    private static DemoWorkItem I(string project, string workstream, string id, string title, string status, string? who, int start, int? due, decimal? est, decimal spent) =>
        new(Internal, project, workstream, id, title, status, who, start, due, est, spent);

    private const string Portal = "Portal build";
    private const string Integration = "Integration layer";
    private const string ClaimsApi = "Claims API";
    private const string Mobile = "Adjuster mobile app";

    public static readonly IReadOnlyList<DemoWorkItem> WorkItems =
    [
        // Harbour Health: fixed price, overrunning.
        H(Portal, "Discovery", "HHT-101", "User research synthesis", "Done", "olivia", 84, -70, 40, 44),
        H(Portal, "Discovery", "HHT-102", "Service blueprint and journey maps", "Done", "chloe", 84, -68, 32, 30),
        H(Portal, "Discovery", "HHT-103", "Discovery report sign-off", "Done", "sarah", 75, -63, 6, 8, milestone: true),
        H(Portal, "Discovery", "HHT-104", "User stories: appointments", "Done", "olivia", 60, -30, 20, 24),
        H(Portal, "Discovery", "HHT-105", "Clinical safety case inputs", "In progress", "olivia", 20, 8, 16, 10),
        H(Portal, "Discovery", "HHT-106", "Appointment booking prototypes", "Done", "chloe", 64, -35, 32, 36),
        H(Portal, "Discovery", "HHT-107", "Clinical safety officer review", "Waiting on client", "olivia", 10, -2, 4, 2),
        H(Portal, "Identity and access", "HHT-110", "NHS login integration spike", "Done", "alex", 72, -52, 16, 22),
        H(Portal, "Identity and access", "HHT-111", "Patient registration flow", "Done", "alex", 62, -40, 40, 52),
        H(Portal, "Identity and access", "HHT-112", "Multi-factor authentication", "Done", "aisha", 62, -35, 24, 26),
        H(Portal, "Identity and access", "HHT-113", "Proxy access for carers", "In progress", "alex", 30, -4, 32, 38),
        H(Portal, "Identity and access", "HHT-114", "Identity service go-live", "Ready", null, 0, 12, 8, 0, milestone: true),
        H(Portal, "Appointments", "HHT-120", "Appointment booking UI", "In review", "aisha", 45, -6, 48, 58),
        H(Portal, "Appointments", "HHT-121", "Clinic availability API", "In progress", "sophie", 40, 8, 40, 34),
        H(Portal, "Appointments", "HHT-122", "Appointment reminders by SMS", "Descoped", null, 0, null, 24, 0),
        H(Portal, "Appointments", "HHT-123", "Cancel and rebook", "To do", "aisha", 0, 20, 24, 0),
        H(Portal, "Appointments", "HHT-124", "Appointments beta release", "Ready", "sarah", 0, 5, 4, 0, milestone: true),
        H(Portal, "Appointments", "HHT-125", "Appointment confirmation emails", "In progress", "aisha", 15, 9, 12, 8),
        H(Portal, "Accessibility and launch", "HHT-130", "WCAG 2.2 AA audit fixes", "Blocked", "ellie", 20, -2, 24, 12),
        H(Portal, "Accessibility and launch", "HHT-131", "External accessibility audit", "Waiting on supplier", "chloe", 25, -3, 8, 6, milestone: true),
        H(Portal, "Accessibility and launch", "HHT-132", "Regression test pack", "In progress", "ellie", 35, 10, 40, 30),
        H(Portal, "Accessibility and launch", "HHT-133", "Go-live readiness review", "Planned", "sarah", 0, 25, 6, 0, milestone: true),
        H(Portal, "Accessibility and launch", "HHT-134", "Performance test", "Backlog", null, 0, null, null, 0),
        H(Portal, "Accessibility and launch", "HHT-135", "Cookie and privacy notices", "Done", "alex", 20, -8, 6, 7),
        H(Portal, "Accessibility and launch", "HHT-136", "Service assessment preparation", "Ready", "sarah", 0, 18, 12, 0),
        H(Portal, "Delivery", "HHT-097", "Backlog refinement", "In progress", "olivia", 84, null, null, 0),
        H(Portal, "Delivery", "HHT-098", "Defects and support", "In progress", "marcus", 84, null, null, 0),
        H(Portal, "Delivery", "HHT-099", "Project management", "In progress", "sarah", 84, null, null, 0),
        H(Integration, "HL7 feeds", "HHT-201", "HL7 ADT feed mapping", "Done", "marcus", 80, -45, 40, 64),
        H(Integration, "HL7 feeds", "HHT-202", "Results feed (ORU) integration", "Blocked", "marcus", 50, -10, 48, 40),
        H(Integration, "HL7 feeds", "HHT-203", "Interface engine hardening", "In progress", "sophie", 30, 14, 32, 20),
        H(Integration, "HL7 feeds", "HHT-204", "Integration test environment", "On hold", "owen", 55, -14, 16, 18),
        H(Integration, "HL7 feeds", "HHT-205", "HL7 feeds sign-off", "Ready", "marcus", 0, -7, 4, 0, milestone: true),
        H(Integration, "HL7 feeds", "HHT-206", "Production environment build", "In progress", "owen", 25, 6, 32, 28),
        H(Integration, "Data migration", "HHT-210", "Legacy data profiling", "Done", "sophie", 70, -42, 24, 36),
        H(Integration, "Data migration", "HHT-211", "Migration scripts", "In development", "sophie", 45, -1, 60, 70),
        H(Integration, "Data migration", "HHT-212", "Migration dry run", "Ready", "sam", 0, -5, 16, 0, milestone: true),
        H(Integration, "Data migration", "HHT-213", "Data reconciliation report", "Backlog", null, 0, 30, null, 0),
        H(Integration, "Data migration", "HHT-214", "Migration tooling", "In progress", "sam", 42, 5, 40, 55),
        H(Integration, "Data migration", "HHT-215", "Migration rollback plan", "To do", "sophie", 0, 16, 8, 0),

        // Kestrel Insurance: time and materials, healthy.
        K(ClaimsApi, "API design", "KES-101", "Claims domain model", "Done", "kwame", 84, -60, 32, 30),
        K(ClaimsApi, "API design", "KES-102", "OpenAPI contract v1", "Done", "ravi", 80, -58, 16, 16),
        K(ClaimsApi, "API design", "KES-103", "API design sign-off", "Done", "daniel", 70, -55, 4, 4, milestone: true),
        K(ClaimsApi, "API design", "KES-104", "Claims rules catalogue", "Done", "ravi", 65, -35, 24, 28),
        K(ClaimsApi, "API design", "KES-105", "Acceptance criteria: pricing", "In progress", "ravi", 20, 5, 12, 8),
        K(ClaimsApi, "Build", "KES-110", "First notice of loss endpoint", "Done", "kwame", 62, -40, 40, 38),
        K(ClaimsApi, "Build", "KES-111", "Policy lookup integration", "Done", "mateusz", 60, -35, 32, 36),
        K(ClaimsApi, "Build", "KES-112", "Claims pricing engine", "In progress", "kwame", 40, 10, 60, 42),
        K(ClaimsApi, "Build", "KES-113", "Document upload service", "In review", "mateusz", 30, 3, 24, 22),
        K(ClaimsApi, "Build", "KES-114", "Fraud signals webhook", "Ready", "kwame", 0, 18, 24, 0),
        K(ClaimsApi, "Build", "KES-115", "Claims audit trail", "Done", "mateusz", 45, -20, 20, 18),
        K(ClaimsApi, "Build", "KES-116", "Claim status notifications", "Done", "mateusz", 25, -5, 16, 15),
        K(ClaimsApi, "Build", "KES-117", "Reserve calculation API", "In progress", "kwame", 15, 14, 32, 12),
        K(ClaimsApi, "Performance and security", "KES-120", "Load test at twice peak", "Testing", "jack", 20, 2, 16, 14),
        K(ClaimsApi, "Performance and security", "KES-121", "Penetration test remediation", "To do", "mateusz", 0, 21, 16, 0),
        K(ClaimsApi, "Performance and security", "KES-122", "Release 1.0 to production", "Ready", "daniel", 0, 14, 4, 0, milestone: true),
        K(ClaimsApi, "Performance and security", "KES-123", "CI pipeline for mobile app", "Done", "niamh", 35, -15, 16, 20),
        K(ClaimsApi, "Performance and security", "KES-124", "Kubernetes cluster sizing", "Done", "owen", 45, -25, 12, 16),
        K(ClaimsApi, "Performance and security", "KES-125", "Secrets rotation", "Done", "niamh", 20, -3, 8, 8),
        K(ClaimsApi, "Delivery", "KES-098", "Delivery management", "In progress", "daniel", 84, null, null, 0),
        K(ClaimsApi, "Delivery", "KES-099", "Sprint ceremonies and support", "In progress", "kwame", 84, null, null, 0),
        K(Mobile, "Features", "KES-201", "Offline claim capture", "Done", "ben", 70, -30, 40, 44),
        K(Mobile, "Features", "KES-202", "Photo evidence upload", "Done", "ben", 50, -18, 24, 22),
        K(Mobile, "Features", "KES-203", "Adjuster dashboard", "In progress", "ben", 25, 7, 32, 24),
        K(Mobile, "Features", "KES-204", "Push notifications", "In progress", "ben", 12, 12, 16, 6),
        K(Mobile, "Features", "KES-205", "Usability testing round 2", "Done", "tom", 40, -12, 16, 18),
        K(Mobile, "Features", "KES-206", "Design system tokens", "Done", "tom", 70, -40, 24, 20),
        K(Mobile, "Features", "KES-207", "Accessibility pass on mobile", "In review", "tom", 15, 4, 12, 10),
        K(Mobile, "Release", "KES-210", "App store submission", "Ready", "jack", 0, 20, 6, 0, milestone: true),
        K(Mobile, "Release", "KES-211", "Test automation suite", "In progress", "jack", 60, 15, 48, 40),
        K(Mobile, "Release", "KES-212", "Beta programme with 20 adjusters", "Ready", "daniel", 0, 10, 8, 0),

        // Civic Transport: a retainer, drifting, in the client's own vocabulary.
        C("Support", "CTA-301", "Timetable import failures", "Resolved", "grace", 80, -70, 8, 11),
        C("Support", "CTA-302", "Real-time feed latency", "With client", "grace", 40, -8, 12, 16),
        C("Support", "CTA-303", "Accessibility complaint: route planner", "Awaiting CAB", "lucy", 20, -1, 6, 5),
        C("Support", "CTA-304", "Monthly support allowance", "In progress", "grace", 84, null, 120, 0),
        C("Support", "CTA-305", "Password reset emails delayed", "Done", "lucy", 30, -20, 4, 3),
        C("Support", "CTA-306", "Out-of-hours incident review", "Closed", "grace", 15, -10, null, 6),
        C("Support", "CTA-307", "Regression test: timetable release", "Done", "fatima", 50, -30, 16, 18),
        C("Support", "CTA-308", "Stop data correction requests", "Done", "grace", 60, -50, 6, 5),
        C("Support", "CTA-309", "Map tiles provider change", "In progress", "lucy", 10, 12, 16, 6),
        C("Enhancements", "CTA-310", "Step-free route option", "In progress", "lucy", 45, 6, 40, 36),
        C("Enhancements", "CTA-311", "Journey sharing link", "UAT", "lucy", 35, -2, 24, 28),
        C("Enhancements", "CTA-312", "Bus stop search rewrite", "Backlog", null, 0, null, null, 0),
        C("Enhancements", "CTA-313", "Welsh language content", "Ready", null, 0, 10, 16, 0),
        C("Enhancements", "CTA-314", "Quarterly release", "Ready", "hannah", 0, 9, 4, 0, milestone: true),
        C("Enhancements", "CTA-315", "Accessibility statement update", "Done", "fatima", 25, -15, 4, 4),
        C("Governance", "CTA-320", "Monthly service report", "In progress", "hannah", 84, null, null, 0),
        C("Governance", "CTA-321", "Disaster recovery test", "Scheduled", "owen", 10, 4, 12, 4),
        C("Governance", "CTA-322", "Supplier security questionnaire", "With legal", "hannah", 20, -12, 4, 3),

        // Brightwater Retail: pipeline, not started.
        B("BRW-102", "Current platform assessment", "Ready", "leo", 21, 32),
        B("BRW-103", "Discovery kick-off", "Ready", "hannah", 14, 4, milestone: true),
        B("BRW-104", "Customer journey mapping", "Backlog", "chloe", 28, 24),
        B("BRW-105", "Technical architecture options", "Backlog", "leo", 35, 40),

        // Internal and pre-sales: non-billable.
        I("Internal tooling", "Platform", "INT-099", "Internal admin and training", "In progress", "emma", 84, null, null, 0),
        I("Internal tooling", "Platform", "INT-101", "Azure landing zone upgrade", "In progress", "owen", 60, 10, 40, 44),
        I("Internal tooling", "Platform", "INT-102", "Laptop build automation", "Done", "niamh", 40, -10, 16, 14),
        I("Internal tooling", "Platform", "INT-103", "Timesheet reminders bot", "Parked", "mateusz", 30, null, 8, 6),
        I("Internal tooling", "Delivery office", "INT-104", "Delivery metrics pack", "In progress", "priya", 84, null, null, 0),
        I("Internal tooling", "Delivery office", "INT-105", "Security awareness training", "Done", "emma", 40, -30, null, 10),
        I("Pre-sales", "Bids", "PRE-101", "Brightwater bid", "Done", "leo", 50, -20, 40, 46),
        I("Pre-sales", "Bids", "PRE-102", "Harbour phase 2 proposal", "In progress", "leo", 10, 10, 16, 8),
        I("Pre-sales", "Bids", "PRE-103", "Framework tender response", "In progress", "emma", 20, 5, 24, 12),
        I("Pre-sales", "Bids", "PRE-104", "Kestrel phase 2 estimate", "In progress", "daniel", 8, 6, 12, 6)
    ];

    public static DemoWorkItem Item(string id) => WorkItems.Single(w => w.Id == id);

    /// <summary>
    /// Billability as a timesheet would record it. Civic governance is the
    /// deliberate gap: nobody said whether it is covered by the retainer.
    /// </summary>
    public static bool? Billable(DemoWorkItem? item) => item switch
    {
        null => null,
        { Programme: Internal } => false,
        { Programme: Brightwater } => false,
        { Programme: Civic, Workstream: "Governance" } => null,
        _ => true
    };

    // ---- Leave ----------------------------------------------------------------

    public sealed record DemoLeave(string Person, int FromDaysAgo, int ToDaysAgo, LeaveType Type, LeaveRequestStatus Status, string? Notes = null);

    /// <summary>Negative "days ago" is in the future.</summary>
    public static readonly IReadOnlyList<DemoLeave> Leave =
    [
        new("marcus", 60, 56, LeaveType.Holiday, LeaveRequestStatus.Approved),
        new("aisha", 40, 36, LeaveType.Holiday, LeaveRequestStatus.Approved),
        new("kwame", 20, 18, LeaveType.Holiday, LeaveRequestStatus.Approved),
        new("grace", 12, 12, LeaveType.Sick, LeaveRequestStatus.Approved),
        new("owen", -7, -11, LeaveType.Holiday, LeaveRequestStatus.Approved, "Booked before the Brightwater work was scheduled."),
        new("ellie", -10, -14, LeaveType.Holiday, LeaveRequestStatus.Approved),
        new("ben", -21, -25, LeaveType.Holiday, LeaveRequestStatus.Pending, "Family wedding."),
        new("sophie", -30, -31, LeaveType.Holiday, LeaveRequestStatus.Pending),
        new("lucy", -5, -5, LeaveType.Holiday, LeaveRequestStatus.Pending, "Moving house."),
        new("marcus", -18, -22, LeaveType.Holiday, LeaveRequestStatus.Rejected, "Clashes with the HL7 go-live window.")
    ];

    public static bool IsOnLeave(string person, DateOnly date, DateOnly today) =>
        Leave.Any(l => l.Person == person && l.Status == LeaveRequestStatus.Approved
            && date >= today.AddDays(-l.FromDaysAgo) && date <= today.AddDays(-l.ToDaysAgo));

    // ---- Timesheets -----------------------------------------------------------

    public sealed record DemoTimeEntry(string EntryId, string? WorkItemId, DemoPerson Person, DateOnly Date, decimal Hours, bool? Billable);

    /// <summary>
    /// Each working day, a person records their daily share of contracted
    /// hours times their utilisation (with a fixed ±10% weekly rhythm): up to
    /// two thirds of it per item against their own items in start order,
    /// until each reaches its <see cref="DemoWorkItem.Spent"/>, and the rest
    /// against their fallback item.
    /// </summary>
    public static IReadOnlyList<DemoTimeEntry> TimeEntries(DateOnly today)
    {
        var entries = new List<DemoTimeEntry>();
        var sequence = 0;

        foreach (var person in People.Where(p => p.Utilisation > 0))
        {
            var queue = WorkItems
                .Where(w => w.Assignee == person.Key && w.Spent > 0)
                .OrderByDescending(w => w.StartDaysAgo).ThenBy(w => w.Id, StringComparer.Ordinal)
                .ToList();
            var remaining = queue.ToDictionary(w => w.Id, w => w.Spent);
            var fallback = person.Fallback is { Length: > 0 } id ? Item(id) : null;

            for (var daysAgo = Math.Min(HistoryDays, person.StartedDaysAgo); daysAgo >= 1; daysAgo--)
            {
                var date = today.AddDays(-daysAgo);
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || IsOnLeave(person.Key, date, today))
                    continue;

                var rhythm = (((int)date.DayOfWeek + person.Key.Length) % 3) switch { 0 => 0.9m, 1 => 1.0m, _ => 1.1m };
                var capacity = HalfHour(person.HoursPerWeek / 5m * person.Utilisation * rhythm);
                // Nobody spends a whole day on one thing: meetings, support
                // and the next item take the rest, as in a real timesheet.
                var perItem = Math.Max(1m, HalfHour(capacity * 0.65m));

                foreach (var item in queue.Where(w => w.StartDaysAgo >= daysAgo && remaining[w.Id] > 0))
                {
                    if (capacity <= 0)
                        break;
                    var hours = Math.Min(Math.Min(capacity, perItem), remaining[item.Id]);
                    entries.Add(new($"NS-{++sequence:00000}", item.Id, person, date, hours, Billable(item)));
                    remaining[item.Id] -= hours;
                    capacity -= hours;
                }

                if (capacity > 0 && person.Fallback is not null)
                    entries.Add(new($"NS-{++sequence:00000}", fallback?.Id, person, date, capacity, fallback is null ? null : Billable(fallback)));
            }
        }

        return entries;
    }

    private static decimal HalfHour(decimal hours) => Math.Round(hours * 2m, MidpointRounding.AwayFromZero) / 2m;

    // ---- Bookings ---------------------------------------------------------------

    /// <param name="Person">Null for a resource the scheduling tool names but nobody has matched.</param>
    public sealed record DemoBooking(string? Person, string ResourceName, string Project, decimal HoursPerWeek, int FromWeek, int ToWeek);

    private static DemoBooking Book(string person, string project, decimal hours, int from = -4, int to = ForwardWeeks - 1) =>
        new(person, Person(person).FullName, project, hours, from, to);

    /// <summary>
    /// Week 0 is the current week. Owen's bookings add up to 45 hours from
    /// week 0 and 53 from week 2, against a 37.5-hour contract and a week of
    /// approved leave: the over-allocation the demo walks through.
    /// </summary>
    public static readonly IReadOnlyList<DemoBooking> Bookings =
    [
        Book("alex", Portal, 32), Book("aisha", Portal, 30), Book("olivia", Portal, 30), Book("ellie", Portal, 35),
        Book("sarah", Portal, 15), Book("sarah", Integration, 10), Book("chloe", Portal, 20),
        Book("chloe", "Replatform discovery", 15, from: 2),
        Book("marcus", Integration, 37.5m), Book("sophie", Integration, 35),
        Book("owen", Integration, 20), Book("owen", ClaimsApi, 15), Book("owen", "Managed service", 10),
        Book("owen", "Replatform discovery", 8, from: 2),
        Book("niamh", "Internal tooling", 20),
        Book("daniel", ClaimsApi, 15), Book("daniel", Mobile, 10), Book("ravi", ClaimsApi, 30), Book("kwame", ClaimsApi, 35),
        Book("mateusz", ClaimsApi, 15), Book("ben", Mobile, 32), Book("tom", Mobile, 20), Book("jack", Mobile, 20),
        Book("jack", ClaimsApi, 12),
        Book("hannah", "Managed service", 20), Book("hannah", "Replatform discovery", 10, from: 2),
        Book("grace", "Managed service", 35), Book("lucy", "Managed service", 32), Book("fatima", "Managed service", 25),
        Book("leo", "Replatform discovery", 20, from: 2), Book("leo", "Pre-sales", 10),
        new("sam", "Sam Okoro", Integration, 37.5m, -4, 3),
        new(null, "Agency DevOps (to be confirmed)", "Replatform discovery", 20, 3, ForwardWeeks - 1)
    ];

    public static DateOnly WeekStart(DateOnly today, int week) => WeeklyProjectCount.WeekStartOf(today).AddDays(7 * week);

    // ---- Portfolio, contracts and RAID ------------------------------------------

    public sealed record DemoCustomer(Guid Key, string Name, string Programme, decimal Budget);

    public static readonly IReadOnlyList<DemoCustomer> Customers =
    [
        new(Guid.Parse("c0000000-0000-4000-8000-000000000001"), "Harbour Health NHS Trust", Harbour, 240_000m),
        new(Guid.Parse("c0000000-0000-4000-8000-000000000002"), "Kestrel Insurance", Kestrel, 180_000m),
        new(Guid.Parse("c0000000-0000-4000-8000-000000000003"), "Civic Transport Authority", Civic, 96_000m),
        new(Guid.Parse("c0000000-0000-4000-8000-000000000004"), "Brightwater Retail", Brightwater, 320_000m)
    ];

    public sealed record DemoContract(string Customer, string Reference, CommercialModel Model, decimal? TotalValue, decimal? AnnualValue,
        decimal? BillRate, int StartDaysAgo, int EndInDays, ContractStatus Status, string Notes);

    public static readonly IReadOnlyList<DemoContract> Contracts =
    [
        new("Harbour Health NHS Trust", "HHT-FP-2026-01", CommercialModel.FixedPrice, 240_000m, null, null, 120, 60, ContractStatus.Active,
            "Fixed price for portal build and integration. Change requests priced separately."),
        new("Kestrel Insurance", "KES-TM-2026-04", CommercialModel.TimeAndMaterials, null, null, 95m, 150, 120, ContractStatus.Active,
            "Blended day rate, billed monthly in arrears."),
        new("Civic Transport Authority", "CTA-RET-2026-02", CommercialModel.Ongoing, null, 96_000m, null, 200, 165, ContractStatus.Active,
            "Managed service retainer: 120 hours a month of support and enhancements."),
        new("Brightwater Retail", "BRW-FP-2026-07", CommercialModel.FixedPrice, 320_000m, null, null, -14, 180, ContractStatus.Draft,
            "Awaiting signature. Discovery bookings start in week two.")
    ];

    public sealed record DemoCost(string Contract, string Description, decimal Amount, int DaysAgo);

    public static readonly IReadOnlyList<DemoCost> NonLabourCosts =
    [
        new("HHT-FP-2026-01", "Third-party penetration test", 6_500m, 20),
        new("HHT-FP-2026-01", "External accessibility audit", 4_200m, 10),
        new("KES-TM-2026-04", "Device lab subscription", 1_200m, 75),
        new("KES-TM-2026-04", "Device lab subscription", 1_200m, 45),
        new("KES-TM-2026-04", "Device lab subscription", 1_200m, 15),
        new("CTA-RET-2026-02", "Hosting pass-through", 2_100m, 70),
        new("CTA-RET-2026-02", "Hosting pass-through", 2_100m, 40),
        new("CTA-RET-2026-02", "Hosting pass-through", 2_100m, 10)
    ];

    public sealed record DemoRaid(Guid Key, string Project, string Title, string Description, SeverityLevel Severity, bool IsRisk, int Status);

    private static Guid RaidKey(int n) => Guid.Parse($"d0000000-0000-4000-8000-{n:000000000000}");

    /// <summary>Status is a <see cref="RiskStatus"/> for risks and an <see cref="IssueStatus"/> for issues.</summary>
    public static readonly IReadOnlyList<DemoRaid> Raid =
    [
        new(RaidKey(1), Integration, "HL7 supplier test environment availability",
            "The trust's integration engine supplier has no test slot before go-live.", SeverityLevel.Critical, true, (int)RiskStatus.Open),
        new(RaidKey(2), Integration, "One engineer holds the HL7 knowledge",
            "Only Marcus has worked on the ADT and ORU mappings; no validated cover.", SeverityLevel.High, true, (int)RiskStatus.Open),
        new(RaidKey(3), Portal, "NHS login onboarding lead time",
            "Assurance submission takes six weeks; started late.", SeverityLevel.High, true, (int)RiskStatus.Mitigating),
        new(RaidKey(4), Portal, "Accessibility audit findings delay go-live",
            "Audit report not yet received; fixes cannot be estimated.", SeverityLevel.Medium, true, (int)RiskStatus.Open),
        new(RaidKey(5), Integration, "Client test environment down since sprint 9",
            "Results feed work is blocked until the trust restores the environment.", SeverityLevel.Critical, false, (int)IssueStatus.Open),
        new(RaidKey(6), Integration, "Migration scripts over estimate",
            "Legacy data quality worse than profiled; 70 hours against 60.", SeverityLevel.Medium, false, (int)IssueStatus.InProgress),
        new(RaidKey(7), ClaimsApi, "Penetration test window may slip",
            "Supplier has offered a later slot.", SeverityLevel.Low, true, (int)RiskStatus.Open),
        new(RaidKey(8), Mobile, "Push notification certificate expired",
            "Renewed and redeployed.", SeverityLevel.Medium, false, (int)IssueStatus.Resolved),
        new(RaidKey(9), "Managed service", "Retainer hours trending above allowance",
            "Support demand has exceeded 120 hours in two of the last three months.", SeverityLevel.High, true, (int)RiskStatus.Open),
        new(RaidKey(10), "Managed service", "Change approvals taking ten days or more",
            "Fixes wait on the client's change advisory board.", SeverityLevel.Medium, false, (int)IssueStatus.Open),
        new(RaidKey(11), "Replatform discovery", "Bookings made before contract signature",
            "Three people are booked from week two on an unsigned contract.", SeverityLevel.Medium, true, (int)RiskStatus.Open)
    ];

    public sealed record DemoChange(Guid Key, string Project, string Title, string Description, ChangeRequestStatus Status, string RequestedBy, string? DecidedBy);

    public static readonly IReadOnlyList<DemoChange> ChangeRequests =
    [
        new(Guid.Parse("e0000000-0000-4000-8000-000000000001"), Portal, "Add NHS login for proxy users",
            "Carers sign in with their own NHS login; estimated 40 hours.", ChangeRequestStatus.Proposed, "sarah", null),
        new(Guid.Parse("e0000000-0000-4000-8000-000000000002"), Portal, "Descope SMS reminders",
            "Trust will use its existing reminder service.", ChangeRequestStatus.Approved, "sarah", "emma"),
        new(Guid.Parse("e0000000-0000-4000-8000-000000000003"), ClaimsApi, "Extra sprint for fraud signals webhook",
            "Client-funded under the T&M contract.", ChangeRequestStatus.Approved, "daniel", "emma"),
        new(Guid.Parse("e0000000-0000-4000-8000-000000000004"), "Managed service", "Welsh language content",
            "Outside the retainer scope; quote requested.", ChangeRequestStatus.Proposed, "hannah", null)
    ];

    // ---- Skills and continuity ----------------------------------------------------

    public sealed record DemoSkill(string Key, string Name, SkillKind Kind);

    public static readonly IReadOnlyList<DemoSkill> Skills =
    [
        new("csharp", "C#", SkillKind.Language),
        new("typescript", "TypeScript", SkillKind.Language),
        new("react", "React", SkillKind.Framework),
        new("terraform", "Terraform", SkillKind.Framework),
        new("azure-infrastructure", "Azure infrastructure", SkillKind.Practice),
        new("hl7-fhir", "HL7 and FHIR integration", SkillKind.Domain),
        new("accessibility", "Accessibility (WCAG 2.2)", SkillKind.Practice),
        new("test-automation", "Test automation", SkillKind.Practice),
        new("ux-research", "User research", SkillKind.Practice),
        new("business-analysis", "Business analysis", SkillKind.Practice),
        new("delivery-management", "Delivery management", SkillKind.Practice)
    ];

    public enum DemoAssertionState { Validated, Submitted, Challenged, Rejected, Lapsed }

    public sealed record DemoAssertion(string Person, string Skill, ProficiencyLevel Level, DemoAssertionState State, string? Reviewer = null);

    /// <summary>
    /// HL7 and Azure infrastructure each have exactly one validated
    /// practitioner, which is the continuity exposure the coverage page
    /// exists to find. React has healthy cover; one validation has lapsed.
    /// </summary>
    public static readonly IReadOnlyList<DemoAssertion> Assertions =
    [
        new("marcus", "hl7-fhir", ProficiencyLevel.Lead, DemoAssertionState.Validated, "daniel"),
        new("marcus", "csharp", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "daniel"),
        new("sophie", "hl7-fhir", ProficiencyLevel.Working, DemoAssertionState.Validated, "marcus"),
        new("sophie", "csharp", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "marcus"),
        new("owen", "azure-infrastructure", ProficiencyLevel.Lead, DemoAssertionState.Validated, "daniel"),
        new("owen", "terraform", ProficiencyLevel.Lead, DemoAssertionState.Validated, "daniel"),
        new("niamh", "terraform", ProficiencyLevel.Working, DemoAssertionState.Submitted),
        new("niamh", "azure-infrastructure", ProficiencyLevel.Working, DemoAssertionState.Submitted),
        new("mateusz", "azure-infrastructure", ProficiencyLevel.Practitioner, DemoAssertionState.Rejected, "daniel"),
        new("kwame", "csharp", ProficiencyLevel.Lead, DemoAssertionState.Validated, "daniel"),
        new("mateusz", "csharp", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "kwame"),
        new("grace", "csharp", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "hannah"),
        new("alex", "react", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "marcus"),
        new("alex", "typescript", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "marcus"),
        new("aisha", "react", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "marcus"),
        new("aisha", "accessibility", ProficiencyLevel.Working, DemoAssertionState.Validated, "sarah"),
        new("ben", "react", ProficiencyLevel.Working, DemoAssertionState.Challenged, "daniel"),
        new("lucy", "react", ProficiencyLevel.Practitioner, DemoAssertionState.Lapsed, "hannah"),
        new("chloe", "accessibility", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "sarah"),
        new("chloe", "ux-research", ProficiencyLevel.Lead, DemoAssertionState.Validated, "sarah"),
        new("tom", "ux-research", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "daniel"),
        new("ellie", "accessibility", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "sarah"),
        new("ellie", "test-automation", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "marcus"),
        new("jack", "test-automation", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "daniel"),
        new("fatima", "test-automation", ProficiencyLevel.Working, DemoAssertionState.Submitted),
        new("olivia", "business-analysis", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "sarah"),
        new("ravi", "business-analysis", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "daniel"),
        new("sarah", "delivery-management", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "daniel"),
        new("hannah", "delivery-management", ProficiencyLevel.Practitioner, DemoAssertionState.Validated, "daniel"),
        new("daniel", "delivery-management", ProficiencyLevel.Lead, DemoAssertionState.Validated, "emma")
    ];

    public sealed record DemoComponent(string Key, string Name, string Description, string Owner, string? Backup,
        CoverageActionType? Action = null, string? ActionOwner = null, int? ActionDueInDays = null, string? ActionRationale = null);

    public static readonly IReadOnlyList<DemoComponent> Components =
    [
        new("hl7-gateway", "HL7 gateway", "ADT and results feeds between the trust's PAS and the portal.", "marcus", null,
            CoverageActionType.Pair, "sophie", 21, "Sole maintainer; Sophie validated at Working. Pair on the ORU feed."),
        new("azure-landing-zone", "Azure landing zone", "Shared subscriptions, networking and policy for every client environment.", "owen", null,
            CoverageActionType.DocumentRunbook, "owen", -3, "Owen is over-allocated and on leave in week two; nothing is written down."),
        new("claims-pricing-engine", "Claims pricing engine", "Rules and rating for Kestrel's claims API.", "kwame", "mateusz"),
        new("patient-portal-identity", "Patient portal identity", "NHS login, registration and proxy access.", "alex", "aisha"),
        new("journey-data-feed", "Journey planner data feed", "Timetable and real-time ingestion for Civic Transport.", "grace", null)
    ];

    // ---- Estimate calibration ------------------------------------------------------

    public enum DemoEstimateReview { Comparable, Excluded, Pending }

    /// <summary>
    /// Estimates captured before work started, then reviewed once it was
    /// done. The Health team's estimates run light and the Insurance team's
    /// hold, which is the calibration story; one is excluded with a reason and
    /// two completed items still await review. The last three are captured on
    /// work not yet started.
    /// </summary>
    public sealed record DemoEstimate(string Item, string Estimator, DemoEstimateReview Review, string? Note = null);

    public static readonly IReadOnlyList<DemoEstimate> Estimates =
    [
        new("HHT-101", "olivia", DemoEstimateReview.Comparable), new("HHT-102", "chloe", DemoEstimateReview.Comparable),
        new("HHT-104", "olivia", DemoEstimateReview.Comparable), new("HHT-106", "chloe", DemoEstimateReview.Comparable),
        new("HHT-110", "alex", DemoEstimateReview.Comparable), new("HHT-111", "alex", DemoEstimateReview.Comparable),
        new("HHT-112", "aisha", DemoEstimateReview.Comparable), new("HHT-201", "marcus", DemoEstimateReview.Comparable),
        new("HHT-210", "sophie", DemoEstimateReview.Comparable), new("HHT-135", "alex", DemoEstimateReview.Pending),
        new("KES-101", "kwame", DemoEstimateReview.Comparable), new("KES-102", "ravi", DemoEstimateReview.Comparable),
        new("KES-104", "ravi", DemoEstimateReview.Excluded, "Scope doubled after the claims rules workshop, so this is not the work that was estimated."),
        new("KES-110", "kwame", DemoEstimateReview.Comparable), new("KES-111", "mateusz", DemoEstimateReview.Comparable),
        new("KES-115", "mateusz", DemoEstimateReview.Comparable), new("KES-116", "mateusz", DemoEstimateReview.Pending),
        new("KES-201", "ben", DemoEstimateReview.Comparable), new("KES-202", "ben", DemoEstimateReview.Comparable),
        new("KES-205", "tom", DemoEstimateReview.Comparable), new("KES-206", "tom", DemoEstimateReview.Comparable),
        new("CTA-305", "lucy", DemoEstimateReview.Comparable), new("CTA-307", "fatima", DemoEstimateReview.Comparable),
        new("HHT-123", "aisha", DemoEstimateReview.Pending), new("KES-114", "kwame", DemoEstimateReview.Pending),
        new("KES-121", "mateusz", DemoEstimateReview.Pending)
    ];

    /// <summary>
    /// Someone whose rate changed inside the window: <see cref="PreviousCostPerHour"/>
    /// until <see cref="ChangedDaysAgo"/>, their <see cref="DemoPerson.CostPerHour"/>
    /// since. Everyone else has had one rate since joining.
    /// </summary>
    public sealed record DemoRateChange(string Person, decimal PreviousCostPerHour, int ChangedDaysAgo);

    public static readonly IReadOnlyList<DemoRateChange> RateChanges =
    [
        new("kwame", 52m, 30) // promoted to senior a month ago
    ];

    /// <summary>When everyone's current rate history starts: before any contract in the data.</summary>
    public const int JoinedDaysAgo = 365;

    // ---- CSV exports, in the canonical import shape ---------------------------------

    /// <param name="asPlanned">
    /// The export as it stood before any of this work began: everything that
    /// has since started is "Ready". Imported first so that estimates can be
    /// captured the way the product requires, before work starts.
    /// </param>
    public static string WorkItemsCsv(DateOnly today, bool asPlanned = false)
    {
        var csv = new StringBuilder("Programme,Project,Workstream,WorkItemId,Title,Status,Assignee,AssigneeEmail,DueDate,EstimatedHours,Milestone\n");
        foreach (var item in WorkItems)
        {
            var who = item.Assignee is null ? null : Person(item.Assignee);
            var status = asPlanned && item.StartDaysAgo > 0 ? "Ready" : item.Status;
            csv.Append(string.Join(',',
                item.Programme, item.Project, item.Workstream, item.Id, Quote(item.Title), status,
                who?.FullName ?? "", who?.Email ?? "",
                item.DueInDays is { } due ? Date(today.AddDays(due)) : "",
                item.Estimate?.ToString(CultureInfo.InvariantCulture) ?? "",
                item.Milestone ? "yes" : "no"));
            csv.Append('\n');
        }
        return csv.ToString();
    }

    public static string TimeEntriesCsv(DateOnly today)
    {
        var csv = new StringBuilder("EntryId,WorkItemId,Person,PersonEmail,Date,Hours,Billable\n");
        foreach (var entry in TimeEntries(today))
        {
            csv.Append(string.Join(',',
                entry.EntryId, entry.WorkItemId ?? "", entry.Person.FullName, entry.Person.Email,
                Date(entry.Date), entry.Hours.ToString(CultureInfo.InvariantCulture),
                entry.Billable switch { true => "yes", false => "no", null => "" }));
            csv.Append('\n');
        }
        return csv.ToString();
    }

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
