using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.SkillsEvidence;

namespace ProgrammePulse.Tests.SkillsEvidence;

public sealed class FakeSkillsEvidenceAuditLogRepository : ISkillsEvidenceAuditLogRepository
{
    public readonly List<SkillsEvidenceAuditLog> Entries = [];

    public Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
    {
        Entries.Add(new SkillsEvidenceAuditLog
        {
            LogKey = Guid.NewGuid(),
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ActorMemberId = actorMemberId,
            DetailJson = detailJson,
            TimestampUtc = timestampUtc
        });

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SkillsEvidenceAuditLog>> GetRecentAsync(int take, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<SkillsEvidenceAuditLog>>(
            Entries.Where(e => e.TenantId == tenantId).OrderByDescending(e => e.TimestampUtc).Take(take).ToList());

    public Task<IReadOnlyList<SkillsEvidenceAuditLog>> GetForEntityAsync(string entityId, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<SkillsEvidenceAuditLog>>(
            Entries.Where(e => e.EntityId == entityId && e.TenantId == tenantId).ToList());
}

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now = Now.Add(by);
}

/// <summary>
/// The scaffolding every SkillsEvidence test needs: two tenants, four
/// people and a working service, so each test file states only what it is
/// actually about.
///
/// Two tenants by default, always. Isolation defects do not show up in a
/// single-tenant fixture — they show up when a second tenant's key is in
/// scope and something forgets to filter.
/// </summary>
public sealed class SkillsEvidenceTestContext
{
    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    public static readonly DateTimeOffset Start = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

    public FakeSkillsEvidenceRepository Repository { get; } = new();
    public FakeSkillsEvidenceAuditLogRepository AuditLog { get; } = new();
    public ProgrammeOps.FakeStaffRepository StaffRepository { get; } = new();
    public FixedTimeProvider Time { get; } = new(Start);

    /// <summary>
    /// Empty for the Slice 1 tests — the participant now also covers
    /// engineering evidence, and these tests assert that a subject with no
    /// evidence exports and erases exactly as before.
    /// </summary>
    public FakeEngineeringEvidenceRepository EvidenceRepository { get; } = new();

    /// <summary>Empty for the Slice 1 tests; the suggestion layer has its own.</summary>
    public FakeSuggestionRepository Suggestions { get; } = new();

    public SkillAssertionService Service { get; }
    public SkillCoverageQueryService Coverage { get; }
    public SkillsEvidenceDataParticipant Participant { get; }

    /// <summary>Employee in tenant A — the subject of most tests.</summary>
    public StaffProfile Alex { get; }

    /// <summary>Reviewer in tenant A.</summary>
    public StaffProfile Sarah { get; }

    /// <summary>Second employee in tenant A, so coverage counts can exceed one.</summary>
    public StaffProfile Nia { get; }

    /// <summary>Employee in tenant B — the person whose data must never appear in tenant A's answers.</summary>
    public StaffProfile Rhian { get; }

    public SkillsEvidenceTestContext()
    {
        Service = new SkillAssertionService(Repository, AuditLog, Time);
        Coverage = new SkillCoverageQueryService(Repository, StaffRepository, Time);
        Participant = new SkillsEvidenceDataParticipant(Repository, EvidenceRepository, Suggestions, AuditLog, StaffRepository);

        Alex = AddStaff("Alex Morgan", 101, TenantA);
        Sarah = AddStaff("Sarah Evans", 102, TenantA);
        Nia = AddStaff("Nia Roberts", 103, TenantA);
        Rhian = AddStaff("Rhian Pugh", 201, TenantB);
    }

    public StaffProfile AddStaff(string name, int memberId, Guid tenantId, bool isActive = true)
    {
        var staff = new StaffProfile
        {
            StaffKey = Guid.NewGuid(),
            MemberId = memberId,
            FullName = name,
            Email = $"{name.Split(' ')[0].ToLowerInvariant()}@example.test",
            JobTitle = "Engineer",
            IsActive = isActive,
            TenantId = tenantId,
            CreatedAtUtc = Start.UtcDateTime,
            UpdatedAtUtc = Start.UtcDateTime
        };

        StaffRepository.Staff.Add(staff);
        return staff;
    }

    public async Task<SkillDefinition> AddSkillAsync(string key, Guid tenantId, SkillKind kind = SkillKind.Language) =>
        await Service.CreateSkillAsync(key, key.ToUpperInvariant(), kind, null, tenantId, actorMemberId: 1);

    /// <summary>Declare, then validate — the shortest path to a row that counts as cover.</summary>
    public async Task<StaffSkillAssertion> ValidatedAsync(
        StaffProfile subject, string skillKey, ProficiencyLevel level, StaffProfile reviewer, Guid tenantId)
    {
        var declared = await Service.DeclareAsync(subject.StaffKey, skillKey, level, "did the work", tenantId, subject.MemberId);
        return await Service.ValidateAsync(
            declared.AssertionKey, reviewer.StaffKey, level, "seen it first hand", null, tenantId, reviewer.MemberId);
    }
}
