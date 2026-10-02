using System.Text.Json;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// The continuity-plan lifecycle: declaring a component and its owner,
/// approving cover, and raising and closing the actions that follow.
///
/// Every rule here exists to keep the plan a *decision* rather than a
/// derived number. Nothing infers an owner from activity, nothing
/// auto-raises an action, and closing one requires saying what happened.
/// </summary>
public interface IContinuityService
{
    Task<ComponentOwnership> DeclareComponentAsync(
        string rawKeyOrName, string displayName, string? description, Guid? ownerStaffKey,
        Guid reviewerStaffKey, Guid tenantId, int? actorMemberId);

    /// <summary>
    /// Re-confirms the map without changing it — the "yes, still true"
    /// action. Moves the review date, which is the whole point.
    /// </summary>
    Task<ComponentOwnership> ConfirmComponentAsync(string componentKey, Guid reviewerStaffKey, Guid tenantId, int? actorMemberId);

    Task RetireComponentAsync(string componentKey, Guid tenantId, int? actorMemberId);

    Task<ComponentBackup> ApproveBackupAsync(
        string componentKey, Guid staffKey, string? note, Guid approverStaffKey, Guid tenantId, int? actorMemberId);

    Task RemoveBackupAsync(Guid backupKey, Guid tenantId, int? actorMemberId);

    Task<CoverageAction> RaiseActionAsync(
        string componentKey, CoverageActionType type, Guid ownerStaffKey, string rationale,
        string? evidenceSnapshot, DateOnly? dueOn, Guid raisedByStaffKey, Guid tenantId, int? actorMemberId);

    /// <summary>
    /// Closes an action. <paramref name="outcomeNote"/> is required for
    /// both outcomes: "done" must say what was done, and "abandoned"
    /// must say why — a decision that quietly evaporates is worse than
    /// one that was never made.
    /// </summary>
    Task<CoverageAction> CloseActionAsync(
        Guid actionKey, CoverageActionOutcome outcome, string outcomeNote,
        Guid closedByStaffKey, Guid tenantId, int? actorMemberId);

    // ---- Processing decision ----

    Task<EvidenceProcessingDecision> RecordProcessingDecisionAsync(
        EvidenceLawfulBasis lawfulBasis, bool workerNoticeGiven, string? workerNoticeReference,
        bool dpiaCompleted, string? dpiaReference, DateTime? dpiaCompletedAtUtc, string purpose,
        DateOnly reviewDueOn, Guid decidedByStaffKey, Guid tenantId, int? actorMemberId);

    Task WithdrawProcessingDecisionAsync(Guid tenantId, int? actorMemberId);
}

public sealed class ContinuityService(
    IContinuityRepository repository,
    ISkillsEvidenceAuditLogRepository auditLog,
    TimeProvider timeProvider) : IContinuityService
{
    private const int MaxNoteLength = 1000;

    public async Task<ComponentOwnership> DeclareComponentAsync(
        string rawKeyOrName, string displayName, string? description, Guid? ownerStaffKey,
        Guid reviewerStaffKey, Guid tenantId, int? actorMemberId)
    {
        if (!SkillTaxonomy.IsValidName(displayName))
        {
            throw new SkillAssertionValidationException($"A component needs a name of 1 to {SkillTaxonomy.MaxNameLength} characters.");
        }

        var source = string.IsNullOrWhiteSpace(rawKeyOrName) ? displayName : rawKeyOrName;
        var componentKey = SkillTaxonomy.NormalizeKey(source);
        if (!SkillTaxonomy.IsValidKey(componentKey))
        {
            throw new SkillAssertionValidationException(
                $"'{source}' does not reduce to a usable component key. Use letters and digits.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var existing = await repository.GetComponentAsync(componentKey, tenantId);

        var saved = await repository.UpsertComponentAsync(new ComponentOwnership
        {
            OwnershipKey = existing?.OwnershipKey ?? Guid.NewGuid(),
            TenantId = tenantId,
            ComponentKey = componentKey,
            DisplayName = displayName.Trim(),
            Description = Clean(description),
            OwnerStaffKey = ownerStaffKey,
            // Declaring it *is* reviewing it — the manager is saying this
            // is true today, which is what the review date records.
            ReviewedByStaffKey = reviewerStaffKey,
            LastReviewedAtUtc = now,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now
        });

        await LogAsync(SkillsEvidenceAuditAction.EntityTypeComponent, saved.ComponentKey,
            existing is null ? SkillsEvidenceAuditAction.ComponentDeclared : SkillsEvidenceAuditAction.ComponentUpdated,
            JsonSerializer.Serialize(new { saved.DisplayName, owner = saved.OwnerStaffKey }), now, tenantId, actorMemberId);

        return saved;
    }

    public async Task<ComponentOwnership> ConfirmComponentAsync(string componentKey, Guid reviewerStaffKey, Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var existing = await repository.GetComponentAsync(SkillTaxonomy.NormalizeKey(componentKey), tenantId)
            ?? throw new CrossTenantReferenceException("Component", Guid.Empty);

        var saved = await repository.UpsertComponentAsync(existing with
        {
            ReviewedByStaffKey = reviewerStaffKey,
            LastReviewedAtUtc = now,
            UpdatedAtUtc = now
        });

        await LogAsync(SkillsEvidenceAuditAction.EntityTypeComponent, saved.ComponentKey,
            SkillsEvidenceAuditAction.ComponentReviewed, null, now, tenantId, actorMemberId);

        return saved;
    }

    public async Task RetireComponentAsync(string componentKey, Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var normalized = SkillTaxonomy.NormalizeKey(componentKey);

        _ = await repository.GetComponentAsync(normalized, tenantId)
            ?? throw new CrossTenantReferenceException("Component", Guid.Empty);

        await repository.DeleteComponentAsync(normalized, tenantId);

        await LogAsync(SkillsEvidenceAuditAction.EntityTypeComponent, normalized,
            SkillsEvidenceAuditAction.ComponentRetired, null, now, tenantId, actorMemberId);
    }

    public async Task<ComponentBackup> ApproveBackupAsync(
        string componentKey, Guid staffKey, string? note, Guid approverStaffKey, Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var normalized = SkillTaxonomy.NormalizeKey(componentKey);

        var component = await repository.GetComponentAsync(normalized, tenantId)
            ?? throw new CrossTenantReferenceException("Component", Guid.Empty);

        if (component.OwnerStaffKey == staffKey)
        {
            throw new SkillAssertionValidationException(
                "The owner cannot be their own backup. The point of cover is that somebody else can step in.");
        }

        var backup = await repository.AddBackupAsync(new ComponentBackup
        {
            BackupKey = Guid.NewGuid(),
            TenantId = tenantId,
            ComponentKey = normalized,
            StaffKey = staffKey,
            ApprovedByStaffKey = approverStaffKey,
            ApprovedAtUtc = now,
            Note = Clean(note)
        });

        await LogAsync(SkillsEvidenceAuditAction.EntityTypeComponent, normalized,
            SkillsEvidenceAuditAction.BackupApproved,
            JsonSerializer.Serialize(new { staffKey, approverStaffKey }), now, tenantId, actorMemberId);

        return backup;
    }

    public async Task RemoveBackupAsync(Guid backupKey, Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await repository.RemoveBackupAsync(backupKey, tenantId);

        await LogAsync(SkillsEvidenceAuditAction.EntityTypeComponent, backupKey.ToString(),
            SkillsEvidenceAuditAction.BackupRemoved, null, now, tenantId, actorMemberId);
    }

    public async Task<CoverageAction> RaiseActionAsync(
        string componentKey, CoverageActionType type, Guid ownerStaffKey, string rationale,
        string? evidenceSnapshot, DateOnly? dueOn, Guid raisedByStaffKey, Guid tenantId, int? actorMemberId)
    {
        if (!Enum.IsDefined(type))
        {
            throw new SkillAssertionValidationException("Choose one of the defined action types.");
        }

        // Raising without an owner is refused; losing one later to an
        // erasure is not, and shows as NeedsReassignment instead.
        if (ownerStaffKey == Guid.Empty)
        {
            throw new SkillAssertionValidationException("An action needs an owner. Without one it is a wish, not a plan.");
        }

        var reason = Clean(rationale)
            ?? throw new SkillAssertionValidationException("Say what exposure this action addresses.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var normalized = SkillTaxonomy.NormalizeKey(componentKey);

        var action = await repository.UpsertActionAsync(new CoverageAction
        {
            ActionKey = Guid.NewGuid(),
            TenantId = tenantId,
            ComponentKey = normalized,
            Type = type,
            OwnerStaffKey = ownerStaffKey,
            Rationale = reason,
            EvidenceSnapshot = Clean(evidenceSnapshot),
            DueOn = dueOn,
            Outcome = CoverageActionOutcome.Open,
            RaisedByStaffKey = raisedByStaffKey,
            RaisedAtUtc = now
        });

        await LogAsync(SkillsEvidenceAuditAction.EntityTypeCoverageAction, action.ActionKey.ToString(),
            SkillsEvidenceAuditAction.CoverageActionRaised,
            JsonSerializer.Serialize(new { component = normalized, type = type.ToString(), ownerStaffKey }),
            now, tenantId, actorMemberId);

        return action;
    }

    public async Task<CoverageAction> CloseActionAsync(
        Guid actionKey, CoverageActionOutcome outcome, string outcomeNote,
        Guid closedByStaffKey, Guid tenantId, int? actorMemberId)
    {
        if (outcome == CoverageActionOutcome.Open)
        {
            throw new SkillAssertionValidationException("Closing an action needs an outcome of completed or abandoned.");
        }

        var note = Clean(outcomeNote)
            ?? throw new SkillAssertionValidationException(
                outcome == CoverageActionOutcome.Completed
                    ? "Say what was actually done. \"Done\" with no detail is not a follow-up."
                    : "Say why this was dropped, so the exposure is not quietly forgotten.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var existing = await repository.GetActionAsync(actionKey, tenantId)
            ?? throw new CrossTenantReferenceException("CoverageAction", actionKey);

        if (!existing.IsOpen)
        {
            throw new SkillAssertionValidationException("This action has already been closed.");
        }

        var closed = await repository.UpsertActionAsync(existing with
        {
            Outcome = outcome,
            OutcomeNote = note,
            ClosedByStaffKey = closedByStaffKey,
            ClosedAtUtc = now
        });

        await LogAsync(SkillsEvidenceAuditAction.EntityTypeCoverageAction, actionKey.ToString(),
            SkillsEvidenceAuditAction.CoverageActionClosed,
            JsonSerializer.Serialize(new { outcome = outcome.ToString(), closedByStaffKey }),
            now, tenantId, actorMemberId);

        return closed;
    }

    // ---- Processing decision ----

    public async Task<EvidenceProcessingDecision> RecordProcessingDecisionAsync(
        EvidenceLawfulBasis lawfulBasis, bool workerNoticeGiven, string? workerNoticeReference,
        bool dpiaCompleted, string? dpiaReference, DateTime? dpiaCompletedAtUtc, string purpose,
        DateOnly reviewDueOn, Guid decidedByStaffKey, Guid tenantId, int? actorMemberId)
    {
        if (!Enum.IsDefined(lawfulBasis))
        {
            throw new SkillAssertionValidationException("Choose one of the defined lawful bases.");
        }

        var statedPurpose = Clean(purpose)
            ?? throw new SkillAssertionValidationException(
                "State what the evidence is collected for. A purpose nobody wrote down cannot be shown to be proportionate.");

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (reviewDueOn <= DateOnly.FromDateTime(now))
        {
            throw new SkillAssertionValidationException("The review date must be in the future.");
        }

        var decision = await repository.SupersedeProcessingDecisionAsync(new EvidenceProcessingDecision
        {
            DecisionKey = Guid.NewGuid(),
            TenantId = tenantId,
            LawfulBasis = lawfulBasis,
            WorkerNoticeGiven = workerNoticeGiven,
            WorkerNoticeReference = Clean(workerNoticeReference),
            DpiaCompleted = dpiaCompleted,
            DpiaReference = Clean(dpiaReference),
            DpiaCompletedAtUtc = dpiaCompletedAtUtc,
            Purpose = statedPurpose,
            DecidedByStaffKey = decidedByStaffKey,
            DecidedAtUtc = now,
            ReviewDueOn = reviewDueOn
        }, now);

        // The references are the customer's own artefacts, not free text
        // about a person, so they are safe in the audit detail — and an
        // auditor asking "what were we relying on" needs them there.
        await LogAsync(SkillsEvidenceAuditAction.EntityTypeProcessingDecision, decision.DecisionKey.ToString(),
            SkillsEvidenceAuditAction.ProcessingDecisionRecorded,
            JsonSerializer.Serialize(new
            {
                lawfulBasis = lawfulBasis.ToString(),
                workerNoticeGiven,
                dpiaCompleted,
                decision.DpiaReference,
                reviewDueOn = reviewDueOn.ToString("yyyy-MM-dd"),
                decidedByStaffKey
            }),
            now, tenantId, actorMemberId);

        return decision;
    }

    public async Task WithdrawProcessingDecisionAsync(Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var withdrawn = await repository.WithdrawProcessingDecisionAsync(tenantId, now);

        if (withdrawn > 0)
        {
            await LogAsync(SkillsEvidenceAuditAction.EntityTypeProcessingDecision, tenantId.ToString(),
                SkillsEvidenceAuditAction.ProcessingDecisionWithdrawn, null, now, tenantId, actorMemberId);
        }
    }

    private Task LogAsync(string entityType, string entityId, string action, string? detailJson, DateTime now, Guid tenantId, int? actorMemberId) =>
        auditLog.LogAsync(entityType, entityId, action, actorMemberId, detailJson, now, tenantId);

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxNoteLength ? trimmed : trimmed[..MaxNoteLength];
    }
}
