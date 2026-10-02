using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// In-memory stand-in for SkillsEvidenceRepository. A fake, not a mock —
/// the convention in this repository (see CLAUDE.md and
/// ProgrammeOps/FakeProgrammeRepository).
///
/// It reproduces the three guarantees the real repository gets from the
/// database, because the tests above it are about those guarantees:
///   1. every read filters on tenantId;
///   2. AppendAsync rejects a skill key or a previous row from another
///      tenant (the real one queries with tenantId in the WHERE clause,
///      this one compares);
///   3. supersede-then-insert keeps at most one current row per
///      (tenant, staff, skill) — enforced in the real schema by the
///      filtered unique index UX_SkillsEvidence_StaffSkillAssertion_current,
///      and asserted here so a service change that breaks the invariant
///      fails without needing SQL Server.
/// </summary>
public sealed class FakeSkillsEvidenceRepository : ISkillsEvidenceRepository
{
    public readonly List<SkillDefinition> Skills = [];
    public readonly List<StaffSkillAssertion> Assertions = [];

    public Task<IReadOnlyList<SkillDefinition>> GetSkillsAsync(Guid tenantId, bool includeRetired) =>
        Task.FromResult<IReadOnlyList<SkillDefinition>>(
            Skills.Where(s => s.TenantId == tenantId && (includeRetired || s.IsActive))
                  .OrderBy(s => s.Name, StringComparer.Ordinal)
                  .ToList());

    public Task<SkillDefinition?> GetSkillAsync(string skillKey, Guid tenantId) =>
        Task.FromResult(Skills.FirstOrDefault(s => s.TenantId == tenantId && s.SkillKey == skillKey));

    public Task<SkillDefinition> CreateSkillAsync(SkillDefinition skill)
    {
        if (Skills.Any(s => s.TenantId == skill.TenantId && s.SkillKey == skill.SkillKey))
        {
            throw new SkillAssertionValidationException($"'{skill.Name}' already uses the skill key '{skill.SkillKey}'.");
        }

        Skills.Add(skill);
        return Task.FromResult(skill);
    }

    public Task<SkillDefinition> SetSkillActiveAsync(string skillKey, bool isActive, Guid tenantId, DateTime nowUtc)
    {
        var existing = Skills.FirstOrDefault(s => s.TenantId == tenantId && s.SkillKey == skillKey)
            ?? throw new CrossTenantReferenceException("Skill", Guid.Empty);

        var updated = existing with { IsActive = isActive, UpdatedAtUtc = nowUtc };
        Skills[Skills.IndexOf(existing)] = updated;
        return Task.FromResult(updated);
    }

    public Task<IReadOnlyList<StaffSkillAssertion>> GetCurrentForStaffAsync(Guid staffKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<StaffSkillAssertion>>(
            Assertions.Where(a => a.StaffKey == staffKey && a.TenantId == tenantId && a.IsCurrent)
                      .OrderBy(a => a.SkillKey, StringComparer.Ordinal)
                      .ToList());

    public Task<IReadOnlyList<StaffSkillAssertion>> GetHistoryForStaffAsync(Guid staffKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<StaffSkillAssertion>>(
            Assertions.Where(a => a.StaffKey == staffKey && a.TenantId == tenantId)
                      .OrderBy(a => a.SkillKey, StringComparer.Ordinal)
                      .ThenBy(a => a.RecordedAtUtc)
                      .ToList());

    public Task<IReadOnlyList<StaffSkillAssertion>> GetCurrentForTenantAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<StaffSkillAssertion>>(
            Assertions.Where(a => a.TenantId == tenantId && a.IsCurrent).ToList());

    public Task<StaffSkillAssertion?> GetAssertionAsync(Guid assertionKey, Guid tenantId) =>
        Task.FromResult(Assertions.FirstOrDefault(a => a.AssertionKey == assertionKey && a.TenantId == tenantId));

    public Task<StaffSkillAssertion> AppendAsync(StaffSkillAssertion assertion, StaffSkillAssertion? supersedes, DateTime nowUtc)
    {
        var skill = Skills.FirstOrDefault(s => s.SkillKey == assertion.SkillKey && s.TenantId == assertion.TenantId);
        if (skill is null)
        {
            throw new CrossTenantReferenceException("Skill", assertion.AssertionKey);
        }

        if (supersedes is not null)
        {
            var previous = Assertions.FirstOrDefault(a => a.AssertionKey == supersedes.AssertionKey);
            if (previous is null
                || previous.TenantId != assertion.TenantId
                || previous.StaffKey != assertion.StaffKey
                || !previous.IsCurrent)
            {
                throw new CrossTenantReferenceException("SkillAssertion", supersedes.AssertionKey);
            }

            Assertions[Assertions.IndexOf(previous)] = previous with { SupersededAtUtc = nowUtc };
        }

        // Stands in for the filtered unique index. Without it a service bug
        // that forgot to supersede would pass every test here and fail only
        // against SQL Server.
        var clash = Assertions.FirstOrDefault(a =>
            a.TenantId == assertion.TenantId
            && a.StaffKey == assertion.StaffKey
            && a.SkillKey == assertion.SkillKey
            && a.IsCurrent);
        if (clash is not null)
        {
            throw new InvalidOperationException(
                $"UX_SkillsEvidence_StaffSkillAssertion_current would be violated: {assertion.StaffKey} already has a current assertion for '{assertion.SkillKey}'.");
        }

        Assertions.Add(assertion);
        return Task.FromResult(assertion);
    }

    public Task<int> DeleteAllForStaffAsync(Guid staffKey)
    {
        var doomed = Assertions.Where(a => a.StaffKey == staffKey).ToList();
        foreach (var row in doomed)
        {
            Assertions.Remove(row);
        }

        return Task.FromResult(doomed.Count);
    }
}
