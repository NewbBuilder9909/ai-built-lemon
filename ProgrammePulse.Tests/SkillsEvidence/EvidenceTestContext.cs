using Microsoft.Extensions.Options;
using ProgrammePulse.Tests.ProgrammeOps;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Integrations.GitHub;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.SkillsEvidence;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// Scaffolding for the engineering-evidence tests: two tenants, a
/// connected GitHub account in each, a scripted client and a real
/// ingestion service.
///
/// Two tenants always, for the same reason as SkillsEvidenceTestContext:
/// an isolation defect does not show up in a single-tenant fixture.
/// </summary>
public sealed class EvidenceTestContext
{
    public static readonly Guid TenantA = SkillsEvidenceTestContext.TenantA;
    public static readonly Guid TenantB = SkillsEvidenceTestContext.TenantB;

    public const string RepoA = "acme-ltd/web";
    public const string RepoB = "rival-ltd/app";

    public static readonly DateTimeOffset Start = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

    public FakeEngineeringEvidenceRepository Repository { get; } = new();
    public FakeSkillsEvidenceRepository SkillsRepository { get; } = new();

    /// <summary>
    /// Slice 4 added a gate: evidence collection refuses to run without a
    /// current data-processing decision. Both tenants get a valid one by
    /// default so these tests exercise the ingestion itself; the gate has
    /// its own tests in EvidenceProcessingGateTests.
    /// </summary>
    public FakeContinuityRepository Continuity { get; } = new();
    public FakeSkillsEvidenceAuditLogRepository AuditLog { get; } = new();
    public ProgrammeOps.FakeStaffRepository StaffRepository { get; } = new();
    public FakeGitHubEvidenceClient Client { get; } = new();
    public FixedTimeProvider Time { get; } = new(Start);
    public StubEvidenceCredentialProtector Protector { get; } = new();
    public FakeSuggestionRepository Suggestions { get; } = new();

    /// <summary>
    /// The real in-process latch, one per fixture. Shared with the
    /// ProgrammeOps connectors in production; a fresh one per test keeps
    /// them independent.
    /// </summary>
    public SyncRunGuard RunGuard { get; } = new();
    public FakeSyncRunRepository SyncRuns { get; } = new();
    public SyncRunCoordinator Coordinator { get; }

    public EvidenceActorResolver Resolver { get; }
    public GitHubEvidenceIngestionService Ingestion { get; }
    public EvidencePortfolioQueryService Portfolio { get; }
    public SkillsEvidenceDataParticipant Participant { get; }

    public EvidenceConnection ConnectionA { get; }
    public EvidenceConnection ConnectionB { get; }

    public StaffProfile Alex { get; }
    public StaffProfile Sarah { get; }
    public StaffProfile Rhian { get; }

    public EvidenceTestContext(SkillsEvidenceOptions? options = null)
    {
        Coordinator = new SyncRunCoordinator(
            RunGuard,
            SyncRuns,
            Options.Create(new ProgrammeOpsOptions { SyncLeaseSeconds = 300 }),
            Time);
        Resolver = new EvidenceActorResolver(Repository, StaffRepository);
        Portfolio = new EvidencePortfolioQueryService(Repository);
        Participant = new SkillsEvidenceDataParticipant(SkillsRepository, Repository, Suggestions, AuditLog, StaffRepository);

        Ingestion = new GitHubEvidenceIngestionService(
            Client, Repository, Protector, Resolver, AuditLog, Continuity, SyncRuns,
            Options.Create(options ?? new SkillsEvidenceOptions { RawPayloadRetentionDays = 30 }),
            Coordinator,
            Time);

        PermitCollection(TenantA);
        PermitCollection(TenantB);

        Alex = AddStaff("Alex Morgan", 101, "alex@acme.test", TenantA);
        Sarah = AddStaff("Sarah Evans", 102, "sarah@acme.test", TenantA);
        Rhian = AddStaff("Rhian Pugh", 201, "rhian@rival.test", TenantB);

        ConnectionA = Connect(TenantA, "acme-ltd", RepoA);
        ConnectionB = Connect(TenantB, "rival-ltd", RepoB);
    }

    public StaffProfile AddStaff(string name, int memberId, string email, Guid tenantId, bool isActive = true)
    {
        var staff = new StaffProfile
        {
            StaffKey = Guid.NewGuid(),
            MemberId = memberId,
            FullName = name,
            Email = email,
            IsActive = isActive,
            TenantId = tenantId,
            CreatedAtUtc = Start.UtcDateTime,
            UpdatedAtUtc = Start.UtcDateTime
        };

        StaffRepository.Staff.Add(staff);
        return staff;
    }

    public EvidenceConnection Connect(Guid tenantId, string account, params string[] repositories)
    {
        var connection = new EvidenceConnection
        {
            ConnectionKey = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = GitHubEvidenceMapper.ProviderName,
            SourceAccountId = account,
            DisplayName = $"github.com/{account}",
            ApiBaseUrl = EvidenceHostPolicy.GitHubDotComApi,
            SelectedRepositories = repositories,
            Status = EvidenceConnectionStatus.Active,
            ProtectedCredentialJson = StubEvidenceCredentialProtector.Ciphertext,
            CreatedAtUtc = Start.UtcDateTime,
            UpdatedAtUtc = Start.UtcDateTime
        };

        Repository.Connections.Add(connection);
        return connection;
    }

    /// <summary>Approves an external account as a staff member, as the admin queue would.</summary>
    public async Task<EvidenceActorLink> ApproveAsync(EvidenceConnection connection, string externalActorId, StaffProfile staff, string? login = null)
    {
        var link = await Repository.CreateActorLinkAsync(new EvidenceActorLink
        {
            LinkKey = Guid.NewGuid(),
            TenantId = connection.TenantId,
            ConnectionKey = connection.ConnectionKey,
            Provider = connection.Provider,
            ExternalActorId = externalActorId,
            ExternalLogin = login,
            StaffKey = staff.StaffKey,
            ApprovedAtUtc = Time.Now.UtcDateTime
        });

        await Repository.AttributeEvidenceToStaffAsync(
            connection.ConnectionKey, externalActorId, staff.StaffKey, connection.TenantId, Time.Now.UtcDateTime);

        return link;
    }

    public Task<EvidenceIngestionResult> RunAsync(EvidenceConnection connection) =>
        Ingestion.RunAsync(connection.ConnectionKey, connection.TenantId, triggeredByMemberId: 1);

    /// <summary>Records a valid decision, so collection is permitted.</summary>
    public EvidenceProcessingDecision PermitCollection(Guid tenantId)
    {
        var decision = new EvidenceProcessingDecision
        {
            DecisionKey = Guid.NewGuid(),
            TenantId = tenantId,
            LawfulBasis = EvidenceLawfulBasis.LegitimateInterests,
            WorkerNoticeGiven = true,
            WorkerNoticeReference = "Staff handbook s.9, briefed 2026-09-01",
            DpiaCompleted = true,
            DpiaReference = "DPIA-2026-14",
            DpiaCompletedAtUtc = Start.UtcDateTime,
            Purpose = "Finding expertise and planning cover",
            DecidedByStaffKey = Guid.NewGuid(),
            DecidedAtUtc = Start.UtcDateTime,
            ReviewDueOn = DateOnly.FromDateTime(Start.UtcDateTime).AddYears(1)
        };

        Continuity.Decisions.Add(decision);
        return decision;
    }

    /// <summary>Withdraws the tenant's decision, so collection is blocked.</summary>
    public void BlockCollection(Guid tenantId) =>
        Continuity.Decisions.RemoveAll(d => d.TenantId == tenantId);
}

/// <summary>
/// Stands in for Data Protection. <see cref="Readable"/> false reproduces
/// a rotated key ring or tampered ciphertext, which must stop a run rather
/// than fall back to anything.
/// </summary>
public sealed class StubEvidenceCredentialProtector : IEvidenceCredentialProtector
{
    public const string Ciphertext = "protected-credential";

    public bool Readable { get; set; } = true;

    public string Protect(EvidenceCredential credential) => Ciphertext;

    public EvidenceCredential? Unprotect(string? protectedCredentialJson) =>
        protectedCredentialJson is null || !Readable ? null : new EvidenceCredential("token");
}
