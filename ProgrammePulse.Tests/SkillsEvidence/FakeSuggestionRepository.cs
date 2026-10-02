using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// In-memory stand-in for SuggestionRepository.
///
/// Reproduces the identity constraint the real schema enforces on
/// (tenantId, kind, subjectStaffKey, subjectKey, relatedKey), including
/// SQL Server's treatment of NULLs as equal within a unique index —
/// which is the behaviour the idempotent regeneration relies on.
/// </summary>
public sealed class FakeSuggestionRepository : ISuggestionRepository
{
    public readonly List<Suggestion> Suggestions = [];

    public Task<IReadOnlyList<Suggestion>> GetAsync(Guid tenantId, bool openOnly) =>
        Task.FromResult<IReadOnlyList<Suggestion>>(
            Suggestions.Where(s => s.TenantId == tenantId && (!openOnly || s.IsOpen)).ToList());

    public Task<Suggestion?> GetAsync(Guid suggestionKey, Guid tenantId) =>
        Task.FromResult(Suggestions.FirstOrDefault(s => s.SuggestionKey == suggestionKey && s.TenantId == tenantId));

    public Task<IReadOnlyList<Suggestion>> GetForStaffAsync(Guid staffKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<Suggestion>>(
            Suggestions.Where(s => s.SubjectStaffKey == staffKey && s.TenantId == tenantId).ToList());

    public Task<Suggestion?> RaiseIfNewAsync(Suggestion suggestion)
    {
        var existing = Suggestions.FirstOrDefault(s =>
            s.TenantId == suggestion.TenantId
            && s.Kind == suggestion.Kind
            && s.SubjectStaffKey == suggestion.SubjectStaffKey
            && string.Equals(s.SubjectKey, suggestion.SubjectKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.RelatedKey, suggestion.RelatedKey, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            // Open or decided, it stays. Resurrecting a dismissal is the
            // behaviour this exists to prevent.
            return Task.FromResult<Suggestion?>(null);
        }

        Suggestions.Add(suggestion);
        return Task.FromResult<Suggestion?>(suggestion);
    }

    public Task<Suggestion> DecideAsync(Suggestion suggestion)
    {
        var existing = Suggestions.FirstOrDefault(s =>
            s.SuggestionKey == suggestion.SuggestionKey && s.TenantId == suggestion.TenantId)
            ?? throw new CrossTenantReferenceException("Suggestion", suggestion.SuggestionKey);

        Suggestions[Suggestions.IndexOf(existing)] = suggestion;
        return Task.FromResult(suggestion);
    }

    public Task<int> DeleteForStaffAsync(Guid staffKey) =>
        Task.FromResult(Suggestions.RemoveAll(s => s.SubjectStaffKey == staffKey));
}
