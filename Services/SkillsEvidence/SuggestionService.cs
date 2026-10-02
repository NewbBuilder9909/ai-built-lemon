using System.Text.Json;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>What one generation pass proposed.</summary>
public sealed record SuggestionRunResult(int SkillTags, int CoverageGaps, string Summary);

/// <summary>
/// Generates proposals from evidence already collected, and applies a
/// person's decision about them.
///
/// **Nothing here creates a fact.** The design document's fifth phase
/// asks for suggestions with confidence and citations that require human
/// acceptance before they become assertions or causal links, and the
/// ceiling on what acceptance can produce is set here:
///
/// - accepting a skill tag creates a **self-declared, Submitted**
///   assertion at <see cref="ProficiencyLevel.Awareness"/> — the weakest
///   thing the domain can express — which then goes through the same
///   review a hand-typed declaration does. It cannot create a validated
///   skill, and it refuses outright if the person already has an
///   assertion for that skill, so it can never raise an existing level;
/// - accepting a coverage gap creates an open
///   <see cref="CoverageAction"/> with an owner the accepting manager
///   chooses.
///
/// There is deliberately no path from a suggestion to a
/// <see cref="ServiceOps.SupportLinkMethod.ConfirmedRootCause"/> or to a
/// validated proficiency. The rules are threshold counts over evidence,
/// stated in <see cref="SuggestionThresholds"/>, so "why did it suggest
/// that?" has an answer a customer can check.
/// </summary>
public interface ISuggestionService
{
    /// <summary>
    /// Re-runs the rules for a tenant. Idempotent: a proposal already
    /// raised — or already decided — is left alone, so the queue does
    /// not nag.
    /// </summary>
    Task<SuggestionRunResult> GenerateAsync(Guid tenantId, int? actorMemberId);

    /// <summary>
    /// Accepts a suggestion and creates the thing it proposed. Returns
    /// what was created, described for the confirmation message.
    /// </summary>
    Task<string> AcceptAsync(Guid suggestionKey, Guid deciderStaffKey, Guid? actionOwnerStaffKey, Guid tenantId, int? actorMemberId);

    Task DismissAsync(Guid suggestionKey, Guid deciderStaffKey, string? note, Guid tenantId, int? actorMemberId);
}

public sealed class SuggestionService(
    ISuggestionRepository suggestions,
    IEngineeringEvidenceRepository evidence,
    ISkillsEvidenceRepository skills,
    IContinuityRepository continuity,
    ISkillAssertionService assertions,
    IContinuityService continuityService,
    ISkillsEvidenceAuditLogRepository auditLog,
    IStaffRepository staffRepository,
    TimeProvider timeProvider) : ISuggestionService
{
    public async Task<SuggestionRunResult> GenerateAsync(Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var from = today.AddDays(-SuggestionThresholds.ObservationWindowDays);

        var skillTags = await GenerateSkillTagsAsync(tenantId, from, today, now);
        var gaps = await GenerateCoverageGapsAsync(tenantId, from, today, now);

        await auditLog.LogAsync(
            SkillsEvidenceAuditAction.EntityTypeSuggestion, tenantId.ToString(),
            SkillsEvidenceAuditAction.SuggestionsGenerated, actorMemberId,
            JsonSerializer.Serialize(new { skillTags, coverageGaps = gaps }), now, tenantId);

        return new SuggestionRunResult(skillTags, gaps,
            $"{skillTags} skill suggestions and {gaps} coverage suggestions proposed. "
            + "Each needs a person to accept it before anything is created.");
    }

    /// <summary>
    /// "This person touched files classified as X repeatedly." Counted
    /// over distinct artefacts, not commits, so one busy day does not
    /// look like six months of experience.
    /// </summary>
    private async Task<int> GenerateSkillTagsAsync(Guid tenantId, DateOnly from, DateOnly to, DateTime now)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var activeStaff = (await staffRepository.GetByTenantAsync(tenantId))
            .Where(s => s.IsActive)
            .Select(s => s.StaffKey)
            .ToHashSet();

        // Only skills the tenant actually tracks. Suggesting a skill
        // that is not in their taxonomy would propose something the
        // accept path could not create.
        var trackedSkills = (await skills.GetSkillsAsync(tenantId, includeRetired: false))
            .Select(s => s.SkillKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (trackedSkills.Count == 0 || activeStaff.Count == 0)
        {
            return 0;
        }

        var raised = 0;

        foreach (var staffKey in activeStaff)
        {
            var attributed = (await evidence.GetEvidenceForStaffAsync(staffKey, tenantId))
                .Where(e => e.IsAttributableToAPerson && e.OccurredAtUtc >= fromUtc)
                .ToList();

            if (attributed.Count == 0)
            {
                continue;
            }

            var existing = (await skills.GetCurrentForStaffAsync(staffKey, tenantId))
                .Select(a => a.SkillKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var byLanguage = attributed
                .SelectMany(e => e.LanguageHints.Select(hint => (Hint: hint, Evidence: e)))
                .Where(x => trackedSkills.Contains(x.Hint))
                // Never propose something they have already said
                // something about — accepting would be refused anyway,
                // and a suggestion that cannot be accepted is noise.
                .Where(x => !existing.Contains(x.Hint))
                .GroupBy(x => x.Hint, StringComparer.OrdinalIgnoreCase);

            foreach (var group in byLanguage)
            {
                var artefacts = group.Select(x => x.Evidence).DistinctBy(e => e.ExternalId).ToList();
                if (artefacts.Count < SuggestionThresholds.MinimumArtefactsForSkillTag)
                {
                    continue;
                }

                var repositories = artefacts.Select(e => e.RepositoryKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();

                var created = await suggestions.RaiseIfNewAsync(new Suggestion
                {
                    SuggestionKey = Guid.NewGuid(),
                    TenantId = tenantId,
                    Kind = SuggestionKind.SkillTag,
                    SubjectStaffKey = staffKey,
                    SubjectKey = group.Key,
                    Confidence = SuggestionThresholds.BandFor(artefacts.Count),
                    Rationale =
                        $"{artefacts.Count} artefacts touching files classified as '{group.Key}' "
                        + $"across {repositories} {(repositories == 1 ? "repository" : "repositories")} "
                        + $"since {from:yyyy-MM-dd}. This is participation, not proficiency.",
                    Citations = [.. artefacts
                        .OrderByDescending(e => e.OccurredAtUtc)
                        .Select(e => e.SourceUrl)
                        .Where(url => url is not null)
                        .Take(SuggestionThresholds.MaxCitations)
                        .Select(url => url!)],
                    EvidenceCount = artefacts.Count,
                    ObservedFrom = from,
                    ObservedTo = to,
                    Outcome = SuggestionOutcome.Open,
                    RaisedAtUtc = now
                });

                if (created is not null)
                {
                    raised++;
                }
            }
        }

        return raised;
    }

    /// <summary>
    /// "This component has an owner and nobody approved to cover them."
    /// Derived from the manager's own map, not from activity — so it
    /// restates a gap they can already see, with a proposed action
    /// attached.
    /// </summary>
    private async Task<int> GenerateCoverageGapsAsync(Guid tenantId, DateOnly from, DateOnly to, DateTime now)
    {
        var components = await continuity.GetComponentsAsync(tenantId);
        var backups = (await continuity.GetBackupsAsync(tenantId)).ToLookup(b => b.ComponentKey, StringComparer.OrdinalIgnoreCase);
        var openActions = (await continuity.GetActionsAsync(tenantId, openOnly: true))
            .Select(a => a.ComponentKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var raised = 0;

        foreach (var component in components)
        {
            // An owner but nobody approved to cover them, and nothing
            // already being done about it.
            if (component.OwnerStaffKey is null
                || backups[component.ComponentKey].Any()
                || openActions.Contains(component.ComponentKey))
            {
                continue;
            }

            var created = await suggestions.RaiseIfNewAsync(new Suggestion
            {
                SuggestionKey = Guid.NewGuid(),
                TenantId = tenantId,
                Kind = SuggestionKind.CoverageGap,
                SubjectStaffKey = null,
                SubjectKey = component.ComponentKey,
                // The map either says this or it does not; there is no
                // threshold to be more or less sure about.
                Confidence = SuggestionConfidence.Strong,
                Rationale =
                    $"'{component.DisplayName}' has one accountable owner and nobody approved as cover. "
                    + "This is a continuity risk to the organisation, not a judgement about the owner.",
                EvidenceCount = 1,
                ObservedFrom = from,
                ObservedTo = to,
                Outcome = SuggestionOutcome.Open,
                RaisedAtUtc = now
            });

            if (created is not null)
            {
                raised++;
            }
        }

        return raised;
    }

    public async Task<string> AcceptAsync(
        Guid suggestionKey, Guid deciderStaffKey, Guid? actionOwnerStaffKey, Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var suggestion = await Require(suggestionKey, tenantId);

        var created = suggestion.Kind switch
        {
            SuggestionKind.SkillTag => await AcceptSkillTagAsync(suggestion, tenantId, actorMemberId),
            SuggestionKind.CoverageGap => await AcceptCoverageGapAsync(suggestion, deciderStaffKey, actionOwnerStaffKey, tenantId, actorMemberId),
            _ => throw new SkillAssertionValidationException(
                "This kind of suggestion cannot be accepted automatically. Record the relationship yourself.")
        };

        await suggestions.DecideAsync(suggestion with
        {
            Outcome = SuggestionOutcome.Accepted,
            DecidedByStaffKey = deciderStaffKey,
            DecidedAtUtc = now
        });

        await auditLog.LogAsync(
            SkillsEvidenceAuditAction.EntityTypeSuggestion, suggestionKey.ToString(),
            SkillsEvidenceAuditAction.SuggestionAccepted, actorMemberId,
            JsonSerializer.Serialize(new { kind = suggestion.Kind.ToString(), suggestion.SubjectKey, deciderStaffKey }),
            now, tenantId);

        return created;
    }

    private async Task<string> AcceptSkillTagAsync(Suggestion suggestion, Guid tenantId, int? actorMemberId)
    {
        var subject = suggestion.SubjectStaffKey
            ?? throw new SkillAssertionValidationException("This suggestion names no person.");

        // The ceiling: a self-declared Submitted assertion at Awareness,
        // which still has to be reviewed. DeclareAsync refuses if they
        // already have a current assertion for the skill, so accepting
        // can never raise an existing level.
        var existing = (await skills.GetCurrentForStaffAsync(subject, tenantId))
            .Any(a => string.Equals(a.SkillKey, suggestion.SubjectKey, StringComparison.OrdinalIgnoreCase));

        if (existing)
        {
            throw new SkillAssertionValidationException(
                "There is already a record for this skill. A suggestion can start a conversation, never change a level.");
        }

        await assertions.DeclareAsync(
            subject, suggestion.SubjectKey, ProficiencyLevel.Awareness,
            $"Suggested from observed activity: {suggestion.Rationale}",
            tenantId, actorMemberId);

        return "Recorded as an unreviewed, Awareness-level claim. It still needs a reviewer, like any other declaration.";
    }

    private async Task<string> AcceptCoverageGapAsync(
        Suggestion suggestion, Guid deciderStaffKey, Guid? actionOwnerStaffKey, Guid tenantId, int? actorMemberId)
    {
        var owner = actionOwnerStaffKey
            ?? throw new SkillAssertionValidationException(
                "Choose who will own the action. An action with no owner is a wish, not a plan.");

        await continuityService.RaiseActionAsync(
            suggestion.SubjectKey, CoverageActionType.NominateBackup, owner,
            suggestion.Rationale,
            $"Suggested {suggestion.RaisedAtUtc:yyyy-MM-dd}: {suggestion.EvidenceCount} finding, confidence {suggestion.Confidence}",
            dueOn: null, deciderStaffKey, tenantId, actorMemberId);

        return "A coverage action has been raised, with the owner you chose.";
    }

    public async Task DismissAsync(Guid suggestionKey, Guid deciderStaffKey, string? note, Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var suggestion = await Require(suggestionKey, tenantId);

        await suggestions.DecideAsync(suggestion with
        {
            Outcome = SuggestionOutcome.Dismissed,
            DecidedByStaffKey = deciderStaffKey,
            DecidedAtUtc = now,
            DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        });

        await auditLog.LogAsync(
            SkillsEvidenceAuditAction.EntityTypeSuggestion, suggestionKey.ToString(),
            SkillsEvidenceAuditAction.SuggestionDismissed, actorMemberId,
            JsonSerializer.Serialize(new { kind = suggestion.Kind.ToString(), suggestion.SubjectKey, deciderStaffKey }),
            now, tenantId);
    }

    private async Task<Suggestion> Require(Guid suggestionKey, Guid tenantId)
    {
        var suggestion = await suggestions.GetAsync(suggestionKey, tenantId)
            ?? throw new CrossTenantReferenceException("Suggestion", suggestionKey);

        return suggestion.IsOpen
            ? suggestion
            : throw new SkillAssertionValidationException("This suggestion has already been decided.");
    }
}
