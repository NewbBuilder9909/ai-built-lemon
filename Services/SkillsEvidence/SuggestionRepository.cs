using System.Text.Json;
using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.SkillsEvidence;
using Umbraco.Cms.Infrastructure.Scoping;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.SkillsEvidence;

public interface ISuggestionRepository
{
    Task<IReadOnlyList<Suggestion>> GetAsync(Guid tenantId, bool openOnly);

    Task<Suggestion?> GetAsync(Guid suggestionKey, Guid tenantId);

    Task<IReadOnlyList<Suggestion>> GetForStaffAsync(Guid staffKey, Guid tenantId);

    /// <summary>
    /// Inserts a suggestion, or returns the existing one untouched.
    /// **Never overwrites a decided suggestion** — that is what stops the
    /// engine re-raising something somebody dismissed. Returns null when
    /// nothing was created.
    /// </summary>
    Task<Suggestion?> RaiseIfNewAsync(Suggestion suggestion);

    Task<Suggestion> DecideAsync(Suggestion suggestion);

    /// <summary>GDPR: proposals about a person are claims about them, and go with them.</summary>
    Task<int> DeleteForStaffAsync(Guid staffKey);
}

public sealed class SuggestionRepository(IScopeProvider scopeProvider) : ISuggestionRepository
{
    public async Task<IReadOnlyList<Suggestion>> GetAsync(Guid tenantId, bool openOnly)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var sql = openOnly
            ? Sql.Builder.Where("tenantId = @0 AND outcome = @1", tenantId, nameof(SuggestionOutcome.Open))
            : Sql.Builder.Where("tenantId = @0", tenantId);
        var dtos = await scope.Database.FetchAsync<SuggestionDto>(sql.OrderBy("confidence DESC", "raisedAtUtc DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<Suggestion?> GetAsync(Guid suggestionKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<SuggestionDto>(
            Sql.Builder.Where("suggestionKey = @0 AND tenantId = @1", suggestionKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyList<Suggestion>> GetForStaffAsync(Guid staffKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<SuggestionDto>(
            Sql.Builder.Where("subjectStaffKey = @0 AND tenantId = @1", staffKey, tenantId));
        return dtos.Select(Map).ToList();
    }

    public async Task<Suggestion?> RaiseIfNewAsync(Suggestion suggestion)
    {
        using var scope = scopeProvider.CreateScope();

        var existing = await scope.Database.FirstOrDefaultAsync<SuggestionDto>(
            Sql.Builder.Where(
                "tenantId = @0 AND kind = @1 AND subjectKey = @2", suggestion.TenantId,
                suggestion.Kind.ToString(), suggestion.SubjectKey)
                .Where(suggestion.SubjectStaffKey is null ? "subjectStaffKey IS NULL" : "subjectStaffKey = @0",
                    suggestion.SubjectStaffKey ?? Guid.Empty)
                .Where(suggestion.RelatedKey is null ? "relatedKey IS NULL" : "relatedKey = @0",
                    suggestion.RelatedKey ?? string.Empty));

        if (existing is not null)
        {
            // Open or decided, it stays as it is. Refreshing an open
            // suggestion's counts would be defensible; silently
            // resurrecting a dismissed one would not, and one rule is
            // easier to reason about than two.
            scope.Complete();
            return null;
        }

        var dto = ToDto(suggestion);
        await scope.Database.InsertAsync(dto);
        scope.Complete();
        return Map(dto);
    }

    public async Task<Suggestion> DecideAsync(Suggestion suggestion)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<SuggestionDto>(
            Sql.Builder.Where("suggestionKey = @0 AND tenantId = @1", suggestion.SuggestionKey, suggestion.TenantId))
            ?? throw new CrossTenantReferenceException("Suggestion", suggestion.SuggestionKey);

        dto.Outcome = suggestion.Outcome.ToString();
        dto.DecidedByStaffKey = suggestion.DecidedByStaffKey;
        dto.DecidedAtUtc = suggestion.DecidedAtUtc;
        dto.DecisionNote = Truncate(suggestion.DecisionNote, 1000);

        await scope.Database.UpdateAsync(dto);
        scope.Complete();
        return Map(dto);
    }

    public async Task<int> DeleteForStaffAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope();
        var deleted = await scope.Database.ExecuteAsync(
            $"DELETE FROM [{SuggestionDto.TableName}] WHERE [subjectStaffKey] = @0", staffKey);
        scope.Complete();
        return deleted;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static SuggestionDto ToDto(Suggestion suggestion) => new()
    {
        SuggestionKey = suggestion.SuggestionKey,
        TenantId = suggestion.TenantId,
        Kind = suggestion.Kind.ToString(),
        SubjectStaffKey = suggestion.SubjectStaffKey,
        SubjectKey = suggestion.SubjectKey,
        RelatedKey = suggestion.RelatedKey,
        Confidence = suggestion.Confidence.ToString(),
        Rationale = Truncate(suggestion.Rationale, 1000)!,
        CitationsJson = suggestion.Citations.Count == 0
            ? null
            : JsonSerializer.Serialize(suggestion.Citations.Take(SuggestionThresholds.MaxCitations)),
        EvidenceCount = suggestion.EvidenceCount,
        ObservedFrom = suggestion.ObservedFrom.ToDateTime(TimeOnly.MinValue),
        ObservedTo = suggestion.ObservedTo.ToDateTime(TimeOnly.MinValue),
        Outcome = suggestion.Outcome.ToString(),
        DecidedByStaffKey = suggestion.DecidedByStaffKey,
        DecidedAtUtc = suggestion.DecidedAtUtc,
        DecisionNote = Truncate(suggestion.DecisionNote, 1000),
        RaisedAtUtc = suggestion.RaisedAtUtc
    };

    private static Suggestion Map(SuggestionDto dto) => new()
    {
        SuggestionKey = dto.SuggestionKey,
        TenantId = dto.TenantId,
        Kind = Enum.Parse<SuggestionKind>(dto.Kind),
        SubjectStaffKey = dto.SubjectStaffKey,
        SubjectKey = dto.SubjectKey,
        RelatedKey = dto.RelatedKey,
        Confidence = Enum.Parse<SuggestionConfidence>(dto.Confidence),
        Rationale = dto.Rationale,
        Citations = string.IsNullOrWhiteSpace(dto.CitationsJson)
            ? []
            : JsonSerializer.Deserialize<List<string>>(dto.CitationsJson) ?? [],
        EvidenceCount = dto.EvidenceCount,
        ObservedFrom = DateOnly.FromDateTime(dto.ObservedFrom),
        ObservedTo = DateOnly.FromDateTime(dto.ObservedTo),
        Outcome = Enum.Parse<SuggestionOutcome>(dto.Outcome),
        DecidedByStaffKey = dto.DecidedByStaffKey,
        DecidedAtUtc = dto.DecidedAtUtc,
        DecisionNote = dto.DecisionNote,
        RaisedAtUtc = dto.RaisedAtUtc
    };
}
