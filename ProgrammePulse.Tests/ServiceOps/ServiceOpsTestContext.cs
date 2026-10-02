using Microsoft.Extensions.Options;
using ProgrammePulse.Tests.ProgrammeOps;
using ProgrammePulse.Models.Integrations.Freshdesk.Raw;
using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Integrations.Freshdesk;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.ServiceOps;

namespace ProgrammePulse.Tests.ServiceOps;

/// <summary>
/// A scripted Freshdesk. Reopens, deletions, rate limits, unknown
/// component tags and desks with more history than one run's page budget
/// are all expressible as data — none of them can be produced on demand
/// against a real desk, which is exactly why they are fixtures.
///
/// A fixture replay is not a live vendor validation. The application
/// labels service figures built this way as an unverified replay until a
/// real desk has completed a clean run.
/// </summary>
public sealed class FakeFreshdeskClient : IFreshdeskClient
{
    public readonly Queue<FreshdeskPage> TicketPages = new();
    public readonly Queue<FreshdeskPage> WithdrawnPages = new();
    public readonly List<DateTime?> ObservedSince = [];

    public bool AccessLost { get; set; }

    public string? VerifiedAccount { get; set; } = "acme.freshdesk.com";

    public Task<string?> VerifyAsync(string apiBaseUrl, string apiToken, CancellationToken cancellationToken) =>
        Task.FromResult(VerifiedAccount);

    public Task<FreshdeskPage> GetTicketsUpdatedSinceAsync(
        string apiBaseUrl, string apiToken, DateTime? updatedSinceUtc, CancellationToken cancellationToken)
    {
        ObservedSince.Add(updatedSinceUtc);

        if (AccessLost)
        {
            throw new DeskAccessLostException("acme.freshdesk.com");
        }

        return Task.FromResult(TicketPages.Count > 0
            ? TicketPages.Dequeue()
            : new FreshdeskPage([], updatedSinceUtc, true));
    }

    public Task<FreshdeskPage> GetWithdrawnTicketsAsync(
        string apiBaseUrl, string apiToken, DateTime? updatedSinceUtc, CancellationToken cancellationToken)
    {
        if (AccessLost)
        {
            throw new DeskAccessLostException("acme.freshdesk.com");
        }

        return Task.FromResult(WithdrawnPages.Count > 0
            ? WithdrawnPages.Dequeue()
            : new FreshdeskPage([], updatedSinceUtc, true));
    }

    public FakeFreshdeskClient WithTickets(FreshdeskPage page)
    {
        TicketPages.Enqueue(page);
        return this;
    }

    public FakeFreshdeskClient WithWithdrawn(FreshdeskPage page)
    {
        WithdrawnPages.Enqueue(page);
        return this;
    }

    // ---- fixture builders ----

    public const int StatusOpen = 2;
    public const int StatusPending = 3;
    public const int StatusResolved = 4;
    public const int StatusClosed = 5;

    public static FreshdeskTicket Ticket(
        string id,
        DateTime createdAtUtc,
        int status = StatusOpen,
        int priority = 2,
        string? component = "billing",
        DateTime? resolvedAtUtc = null,
        DateTime? reopenedAtUtc = null,
        DateTime? updatedAtUtc = null,
        bool deleted = false,
        bool spam = false,
        string? responderId = null,
        string? responderName = null,
        params string[] issueKeys) =>
        new(
            Id: id,
            CreatedAtUtc: createdAtUtc,
            UpdatedAtUtc: updatedAtUtc ?? resolvedAtUtc ?? createdAtUtc,
            StatusCode: status,
            RawStatus: status.ToString(),
            PriorityCode: priority,
            ComponentTag: component,
            CaseType: "Incident",
            IsDeleted: deleted,
            IsSpam: spam,
            ResolvedAtUtc: resolvedAtUtc,
            ClosedAtUtc: status == StatusClosed ? resolvedAtUtc : null,
            ReopenedAtUtc: reopenedAtUtc,
            ResponderId: responderId,
            ResponderName: responderName,
            LinkedIssueKeys: issueKeys,
            SourceUrl: $"https://acme.freshdesk.com/a/tickets/{id}");
}

/// <summary>Stands in for Data Protection; <see cref="Readable"/> false reproduces a rotated key ring.</summary>
public sealed class StubDeskCredentialProtector : IDeskCredentialProtector
{
    public const string Ciphertext = "protected-desk-credential";

    public bool Readable { get; set; } = true;

    public string Protect(DeskCredential credential) => Ciphertext;

    public DeskCredential? Unprotect(string? protectedCredentialJson) =>
        protectedCredentialJson is null || !Readable ? null : new DeskCredential("token", "acme");
}

/// <summary>
/// Scaffolding for the Service Ops tests: two tenants, a connected desk
/// in each, a scripted client and a real ingestion service.
///
/// Two tenants always — an isolation defect does not show up in a
/// single-tenant fixture.
/// </summary>
public sealed class ServiceOpsTestContext
{
    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    public static readonly DateTimeOffset Start = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    public FakeServiceOpsRepository Repository { get; } = new();
    public FakeFreshdeskClient Client { get; } = new();
    public StubDeskCredentialProtector Protector { get; } = new();
    public SkillsEvidence.FixedTimeProvider Time { get; } = new(Start);
    public ProgrammeOps.FakeStaffRepository StaffRepository { get; } = new();

    /// <summary>The real in-process latch, one per fixture so tests stay independent.</summary>
    public SyncRunGuard RunGuard { get; } = new();
    public FakeSyncRunRepository SyncRuns { get; } = new();
    public SyncRunCoordinator Coordinator { get; }

    public SupportCodeLinkService LinkService { get; }
    public ServiceHealthQueryService Health { get; }
    public FreshdeskIngestionService Ingestion { get; }
    public ServiceOpsDataParticipant Participant { get; }

    public DeskConnection DeskA { get; }
    public DeskConnection DeskB { get; }

    public StaffProfile Alex { get; }
    public StaffProfile Sarah { get; }
    public StaffProfile Rhian { get; }

    public ServiceOpsTestContext(params string[] approvedComponents)
    {
        Coordinator = new SyncRunCoordinator(
            RunGuard,
            SyncRuns,
            Options.Create(new ProgrammeOpsOptions { SyncLeaseSeconds = 300 }),
            Time);
        LinkService = new SupportCodeLinkService(Repository, Time);
        Health = new ServiceHealthQueryService(Repository);
        Participant = new ServiceOpsDataParticipant(Repository, StaffRepository);
        Ingestion = new FreshdeskIngestionService(
            Client, Repository, Protector, LinkService, SyncRuns,
            Options.Create(new ServiceOpsOptions()), Coordinator, Time);

        Alex = AddStaff("Alex Morgan", 101, "alex@acme.test", TenantA);
        Sarah = AddStaff("Sarah Evans", 102, "sarah@acme.test", TenantA);
        Rhian = AddStaff("Rhian Pugh", 201, "rhian@rival.test", TenantB);

        var components = approvedComponents.Length > 0 ? approvedComponents : ["billing", "sync"];
        DeskA = Connect(TenantA, "acme", components);
        DeskB = Connect(TenantB, "rival", components);
    }

    public StaffProfile AddStaff(string name, int memberId, string email, Guid tenantId)
    {
        var staff = new StaffProfile
        {
            StaffKey = Guid.NewGuid(),
            MemberId = memberId,
            FullName = name,
            Email = email,
            IsActive = true,
            TenantId = tenantId,
            CreatedAtUtc = Start.UtcDateTime,
            UpdatedAtUtc = Start.UtcDateTime
        };

        StaffRepository.Staff.Add(staff);
        return staff;
    }

    public DeskConnection Connect(Guid tenantId, string account, IReadOnlyList<string> components)
    {
        var connection = new DeskConnection
        {
            ConnectionKey = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = DeskHostPolicy.FreshdeskProvider,
            SourceAccountId = account,
            DisplayName = $"{account}.freshdesk.com",
            ApiBaseUrl = DeskHostPolicy.CanonicalizeFreshdesk(account)!,
            ApprovedComponents = components,
            Status = DeskConnectionStatus.Active,
            ProtectedCredentialJson = StubDeskCredentialProtector.Ciphertext,
            CreatedAtUtc = Start.UtcDateTime,
            UpdatedAtUtc = Start.UtcDateTime
        };

        Repository.Connections.Add(connection);
        return connection;
    }

    public Task<DeskIngestionResult> RunAsync(DeskConnection? connection = null)
    {
        var target = connection ?? DeskA;
        return Ingestion.RunAsync(target.ConnectionKey, target.TenantId, triggeredByMemberId: 1);
    }

    /// <summary>The default reporting window used by most tests: a wide one around the fixture dates.</summary>
    public Task<ServiceHealthReport> ReportAsync(Guid? tenantId = null) =>
        Health.BuildAsync(tenantId ?? TenantA, new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 1));
}
