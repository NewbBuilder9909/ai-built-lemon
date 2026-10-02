using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.SkillsEvidence;
using Umbraco.Cms.Infrastructure.Scoping;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.SkillsEvidence;

public sealed class SkillsEvidenceRepository(IScopeProvider scopeProvider) : ISkillsEvidenceRepository
{
    public async Task<IReadOnlyList<SkillDefinition>> GetSkillsAsync(Guid tenantId, bool includeRetired)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var sql = includeRetired
            ? Sql.Builder.Where("tenantId = @0", tenantId)
            : Sql.Builder.Where("tenantId = @0 AND isActive = 1", tenantId);
        var dtos = await scope.Database.FetchAsync<SkillDefinitionDto>(sql.OrderBy("name"));
        return dtos.Select(Map).ToList();
    }

    public async Task<SkillDefinition?> GetSkillAsync(string skillKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<SkillDefinitionDto>(
            Sql.Builder.Where("skillKey = @0 AND tenantId = @1", skillKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<SkillDefinition> CreateSkillAsync(SkillDefinition skill)
    {
        using var scope = scopeProvider.CreateScope();

        var clash = await scope.Database.FirstOrDefaultAsync<SkillDefinitionDto>(
            Sql.Builder.Where("skillKey = @0 AND tenantId = @1", skill.SkillKey, skill.TenantId));
        if (clash is not null)
        {
            throw new SkillAssertionValidationException($"'{clash.Name}' already uses the skill key '{skill.SkillKey}'.");
        }

        var dto = new SkillDefinitionDto
        {
            SkillDefinitionKey = skill.SkillDefinitionKey,
            TenantId = skill.TenantId,
            SkillKey = skill.SkillKey,
            Name = skill.Name,
            Kind = skill.Kind.ToString(),
            TaxonomyVersion = skill.TaxonomyVersion,
            IsActive = skill.IsActive,
            Description = skill.Description,
            CreatedAtUtc = skill.CreatedAtUtc,
            UpdatedAtUtc = skill.UpdatedAtUtc
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<SkillDefinition> SetSkillActiveAsync(string skillKey, bool isActive, Guid tenantId, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<SkillDefinitionDto>(
            Sql.Builder.Where("skillKey = @0 AND tenantId = @1", skillKey, tenantId))
            ?? throw new CrossTenantReferenceException("Skill", Guid.Empty);

        dto.IsActive = isActive;
        dto.UpdatedAtUtc = nowUtc;

        await scope.Database.UpdateAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<IReadOnlyList<StaffSkillAssertion>> GetCurrentForStaffAsync(Guid staffKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<StaffSkillAssertionDto>(
            Sql.Builder
                .Where("staffKey = @0 AND tenantId = @1 AND supersededAtUtc IS NULL", staffKey, tenantId)
                .OrderBy("skillKey"));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<StaffSkillAssertion>> GetHistoryForStaffAsync(Guid staffKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<StaffSkillAssertionDto>(
            Sql.Builder
                .Where("staffKey = @0 AND tenantId = @1", staffKey, tenantId)
                .OrderBy("skillKey", "recordedAtUtc", "id"));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<StaffSkillAssertion>> GetCurrentForTenantAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<StaffSkillAssertionDto>(
            Sql.Builder
                .Where("tenantId = @0 AND supersededAtUtc IS NULL", tenantId)
                .OrderBy("skillKey", "recordedAtUtc"));
        return dtos.Select(Map).ToList();
    }

    public async Task<StaffSkillAssertion?> GetAssertionAsync(Guid assertionKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<StaffSkillAssertionDto>(
            Sql.Builder.Where("assertionKey = @0 AND tenantId = @1", assertionKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<StaffSkillAssertion> AppendAsync(StaffSkillAssertion assertion, StaffSkillAssertion? supersedes, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var database = scope.Database;

        // The skill has to be one of this tenant's. Looked up by
        // (skillKey, tenantId) together, so a key that exists only in
        // another tenant is indistinguishable from one that exists nowhere
        // — the same rule ContractRepository applies to a customerKey.
        // Whether the skill is *retired* is the service's call, not this
        // one: a reviewer must still be able to close out an assertion on a
        // skill the tenant has since stopped tracking.
        var skill = await database.FirstOrDefaultAsync<SkillDefinitionDto>(
            Sql.Builder.Where("skillKey = @0 AND tenantId = @1", assertion.SkillKey, assertion.TenantId));
        if (skill is null)
        {
            throw new CrossTenantReferenceException("Skill", assertion.AssertionKey);
        }

        if (supersedes is not null)
        {
            var previous = await database.FirstOrDefaultAsync<StaffSkillAssertionDto>(
                Sql.Builder.Where("assertionKey = @0", supersedes.AssertionKey));

            if (previous is null
                || previous.TenantId != assertion.TenantId
                || previous.StaffKey != assertion.StaffKey
                || previous.SupersededAtUtc is not null)
            {
                // A superseded row, another tenant's row, or a row that
                // belongs to a different person: all three mean the caller
                // is working from a stale or forged key.
                throw new CrossTenantReferenceException("SkillAssertion", supersedes.AssertionKey);
            }

            previous.SupersededAtUtc = nowUtc;
            await database.UpdateAsync(previous);
        }

        var dto = ToDto(assertion);
        await database.InsertAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<int> DeleteAllForStaffAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope();
        var deleted = await scope.Database.ExecuteAsync(
            $"DELETE FROM [{StaffSkillAssertionDto.TableName}] WHERE [staffKey] = @0", staffKey);
        scope.Complete();
        return deleted;
    }

    private static StaffSkillAssertionDto ToDto(StaffSkillAssertion assertion) => new()
    {
        AssertionKey = assertion.AssertionKey,
        TenantId = assertion.TenantId,
        StaffKey = assertion.StaffKey,
        SkillKey = assertion.SkillKey,
        Proficiency = assertion.Proficiency.ToString(),
        Origin = assertion.Origin.ToString(),
        Status = assertion.Status.ToString(),
        EvidenceNote = assertion.EvidenceNote,
        ReviewerStaffKey = assertion.ReviewerStaffKey,
        ReviewedAtUtc = assertion.ReviewedAtUtc,
        ReviewNote = assertion.ReviewNote,
        ReviewDueOn = assertion.ReviewDueOn?.ToDateTime(TimeOnly.MinValue),
        RecordedByStaffKey = assertion.RecordedByStaffKey,
        RecordedAtUtc = assertion.RecordedAtUtc,
        SupersedesAssertionKey = assertion.SupersedesAssertionKey,
        SupersededAtUtc = assertion.SupersededAtUtc
    };

    private static SkillDefinition Map(SkillDefinitionDto dto) => new()
    {
        SkillDefinitionKey = dto.SkillDefinitionKey,
        TenantId = dto.TenantId,
        SkillKey = dto.SkillKey,
        Name = dto.Name,
        Kind = Enum.Parse<SkillKind>(dto.Kind),
        TaxonomyVersion = dto.TaxonomyVersion,
        IsActive = dto.IsActive,
        Description = dto.Description,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static StaffSkillAssertion Map(StaffSkillAssertionDto dto) => new()
    {
        AssertionKey = dto.AssertionKey,
        TenantId = dto.TenantId,
        StaffKey = dto.StaffKey,
        SkillKey = dto.SkillKey,
        Proficiency = Enum.Parse<ProficiencyLevel>(dto.Proficiency),
        Origin = Enum.Parse<AssertionOrigin>(dto.Origin),
        Status = Enum.Parse<AssertionStatus>(dto.Status),
        EvidenceNote = dto.EvidenceNote,
        ReviewerStaffKey = dto.ReviewerStaffKey,
        ReviewedAtUtc = dto.ReviewedAtUtc,
        ReviewNote = dto.ReviewNote,
        ReviewDueOn = dto.ReviewDueOn is null ? null : DateOnly.FromDateTime(dto.ReviewDueOn.Value),
        RecordedByStaffKey = dto.RecordedByStaffKey,
        RecordedAtUtc = dto.RecordedAtUtc,
        SupersedesAssertionKey = dto.SupersedesAssertionKey,
        SupersededAtUtc = dto.SupersededAtUtc
    };
}
