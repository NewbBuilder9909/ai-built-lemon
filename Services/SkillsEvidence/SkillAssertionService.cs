using System.Text.Json;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.SkillsEvidence;

public sealed class SkillAssertionService(
    ISkillsEvidenceRepository repository,
    ISkillsEvidenceAuditLogRepository auditLog,
    TimeProvider timeProvider) : ISkillAssertionService
{
    private const int MaxNoteLength = 1000;

    public async Task<StaffSkillAssertion> DeclareAsync(
        Guid staffKey, string skillKey, ProficiencyLevel proficiency, string? evidenceNote,
        Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var skill = await RequireActiveSkillAsync(skillKey, tenantId);
        var current = await CurrentForAsync(staffKey, skill.SkillKey, tenantId);

        var assertion = new StaffSkillAssertion
        {
            AssertionKey = Guid.NewGuid(),
            TenantId = tenantId,
            StaffKey = staffKey,
            SkillKey = skill.SkillKey,
            Proficiency = Require(proficiency),
            Origin = AssertionOrigin.SelfDeclared,
            Status = AssertionStatus.Submitted,
            EvidenceNote = Clean(evidenceNote),
            RecordedByStaffKey = staffKey,
            RecordedAtUtc = now,
            SupersedesAssertionKey = current?.AssertionKey
        };

        return await AppendAndAuditAsync(assertion, current, now, SkillsEvidenceAuditAction.AssertionDeclared, actorMemberId);
    }

    public async Task<StaffSkillAssertion> RecordForStaffAsync(
        Guid subjectStaffKey, Guid reviewerStaffKey, string skillKey, ProficiencyLevel proficiency,
        string reviewNote, DateOnly? reviewDueOn, Guid tenantId, int? actorMemberId)
    {
        if (subjectStaffKey == reviewerStaffKey)
        {
            throw new SkillAssertionValidationException(
                "Record your own skills through the self-declaration form — a reviewer cannot validate themselves.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var skill = await RequireActiveSkillAsync(skillKey, tenantId);
        var current = await CurrentForAsync(subjectStaffKey, skill.SkillKey, tenantId);

        var assertion = new StaffSkillAssertion
        {
            AssertionKey = Guid.NewGuid(),
            TenantId = tenantId,
            StaffKey = subjectStaffKey,
            SkillKey = skill.SkillKey,
            Proficiency = Require(proficiency),
            Origin = AssertionOrigin.ReviewerRecorded,
            Status = AssertionStatus.Validated,
            ReviewerStaffKey = reviewerStaffKey,
            ReviewedAtUtc = now,
            ReviewNote = RequireNote(reviewNote, "Say why you are recording this skill for someone else."),
            ReviewDueOn = reviewDueOn ?? ProficiencyRubric.DefaultReviewDue(now),
            RecordedByStaffKey = reviewerStaffKey,
            RecordedAtUtc = now,
            SupersedesAssertionKey = current?.AssertionKey
        };

        return await AppendAndAuditAsync(assertion, current, now, SkillsEvidenceAuditAction.AssertionValidated, actorMemberId);
    }

    public async Task<StaffSkillAssertion> ValidateAsync(
        Guid assertionKey, Guid reviewerStaffKey, ProficiencyLevel proficiency, string reviewNote,
        DateOnly? reviewDueOn, Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var current = await RequireCurrentAsync(assertionKey, tenantId);

        if (current.StaffKey == reviewerStaffKey)
        {
            throw new SkillAssertionValidationException("You cannot validate your own assertion.");
        }

        if (current.Status == AssertionStatus.Withdrawn)
        {
            throw new SkillAssertionValidationException(
                "This assertion has been withdrawn. There is nothing to validate until it is declared again.");
        }

        var assertion = new StaffSkillAssertion
        {
            AssertionKey = Guid.NewGuid(),
            TenantId = tenantId,
            StaffKey = current.StaffKey,
            SkillKey = current.SkillKey,
            Proficiency = Require(proficiency),
            // The employee's own claim is what this grew from, so the
            // origin carries forward — "validated" describes the review,
            // not who first said it.
            Origin = current.Origin,
            Status = AssertionStatus.Validated,
            EvidenceNote = current.EvidenceNote,
            ReviewerStaffKey = reviewerStaffKey,
            ReviewedAtUtc = now,
            ReviewNote = RequireNote(reviewNote, "Say why you are validating at this level — an unexplained level is what gets challenged."),
            ReviewDueOn = reviewDueOn ?? ProficiencyRubric.DefaultReviewDue(now),
            RecordedByStaffKey = reviewerStaffKey,
            RecordedAtUtc = now,
            SupersedesAssertionKey = current.AssertionKey
        };

        return await AppendAndAuditAsync(assertion, current, now, SkillsEvidenceAuditAction.AssertionValidated, actorMemberId);
    }

    public async Task<StaffSkillAssertion> RejectAsync(
        Guid assertionKey, Guid reviewerStaffKey, string reviewNote, Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var current = await RequireCurrentAsync(assertionKey, tenantId);

        if (current.StaffKey == reviewerStaffKey)
        {
            throw new SkillAssertionValidationException("You cannot reject your own assertion.");
        }

        var assertion = new StaffSkillAssertion
        {
            AssertionKey = Guid.NewGuid(),
            TenantId = tenantId,
            StaffKey = current.StaffKey,
            SkillKey = current.SkillKey,
            Proficiency = current.Proficiency,
            Origin = current.Origin,
            Status = AssertionStatus.Rejected,
            EvidenceNote = current.EvidenceNote,
            ReviewerStaffKey = reviewerStaffKey,
            ReviewedAtUtc = now,
            ReviewNote = RequireNote(reviewNote, "A rejection needs a reason the person can read and respond to."),
            // No review-due date: there is nothing standing that could go
            // stale. The person's route back is to challenge or re-declare.
            RecordedByStaffKey = reviewerStaffKey,
            RecordedAtUtc = now,
            SupersedesAssertionKey = current.AssertionKey
        };

        return await AppendAndAuditAsync(assertion, current, now, SkillsEvidenceAuditAction.AssertionRejected, actorMemberId);
    }

    public async Task<StaffSkillAssertion> ChallengeAsync(
        Guid assertionKey, Guid staffKey, ProficiencyLevel claimedProficiency, string reason,
        Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var current = await RequireCurrentAsync(assertionKey, tenantId);

        if (current.StaffKey != staffKey)
        {
            // Not "you are not allowed to" — the caller is not the subject,
            // so as far as they are concerned this record is not theirs and
            // does not exist. Same reasoning as CrossTenantReferenceException.
            throw new CrossTenantReferenceException("SkillAssertion", assertionKey);
        }

        if (current.Status is AssertionStatus.Submitted or AssertionStatus.ChallengeRaised)
        {
            throw new SkillAssertionValidationException(
                "This is already waiting for a reviewer. Withdraw and re-declare it if you want to change what you claimed.");
        }

        var assertion = new StaffSkillAssertion
        {
            AssertionKey = Guid.NewGuid(),
            TenantId = tenantId,
            StaffKey = staffKey,
            SkillKey = current.SkillKey,
            Proficiency = Require(claimedProficiency),
            Origin = AssertionOrigin.SelfDeclared,
            Status = AssertionStatus.ChallengeRaised,
            EvidenceNote = RequireNote(reason, "Say what you think the record should be and why."),
            // The reviewer's decision is carried forward, not cleared: the
            // whole point of a challenge is to show what is being disputed
            // next to the dispute.
            ReviewerStaffKey = current.ReviewerStaffKey,
            ReviewedAtUtc = current.ReviewedAtUtc,
            ReviewNote = current.ReviewNote,
            RecordedByStaffKey = staffKey,
            RecordedAtUtc = now,
            SupersedesAssertionKey = current.AssertionKey
        };

        return await AppendAndAuditAsync(assertion, current, now, SkillsEvidenceAuditAction.AssertionChallenged, actorMemberId);
    }

    public async Task<StaffSkillAssertion> WithdrawAsync(Guid assertionKey, Guid staffKey, Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var current = await RequireCurrentAsync(assertionKey, tenantId);

        if (current.StaffKey != staffKey)
        {
            throw new CrossTenantReferenceException("SkillAssertion", assertionKey);
        }

        if (current.Status == AssertionStatus.Withdrawn)
        {
            throw new SkillAssertionValidationException("This skill has already been withdrawn.");
        }

        var assertion = new StaffSkillAssertion
        {
            AssertionKey = Guid.NewGuid(),
            TenantId = tenantId,
            StaffKey = staffKey,
            SkillKey = current.SkillKey,
            Proficiency = current.Proficiency,
            Origin = current.Origin,
            Status = AssertionStatus.Withdrawn,
            RecordedByStaffKey = staffKey,
            RecordedAtUtc = now,
            SupersedesAssertionKey = current.AssertionKey
        };

        return await AppendAndAuditAsync(assertion, current, now, SkillsEvidenceAuditAction.AssertionWithdrawn, actorMemberId);
    }

    public async Task<SkillDefinition> CreateSkillAsync(
        string rawKeyOrName, string name, SkillKind kind, string? description, Guid tenantId, int? actorMemberId)
    {
        if (!SkillTaxonomy.IsValidName(name))
        {
            throw new SkillAssertionValidationException($"A skill needs a name of 1 to {SkillTaxonomy.MaxNameLength} characters.");
        }

        // Fall back to the display name when no key was typed, so the
        // common case is one field, not two.
        var source = string.IsNullOrWhiteSpace(rawKeyOrName) ? name : rawKeyOrName;
        var skillKey = SkillTaxonomy.NormalizeKey(source);
        if (!SkillTaxonomy.IsValidKey(skillKey))
        {
            throw new SkillAssertionValidationException(
                $"'{source}' does not reduce to a usable skill key. Use letters and digits, e.g. \"C#\" becomes \"c\" — type a key explicitly if the name is mostly punctuation.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new SkillAssertionValidationException("Choose one of the defined skill kinds.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var skill = await repository.CreateSkillAsync(new SkillDefinition
        {
            SkillDefinitionKey = Guid.NewGuid(),
            TenantId = tenantId,
            SkillKey = skillKey,
            Name = name.Trim(),
            Kind = kind,
            TaxonomyVersion = SkillTaxonomy.CurrentVersion,
            IsActive = true,
            Description = Clean(description),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await auditLog.LogAsync(
            SkillsEvidenceAuditAction.EntityTypeSkill, skill.SkillKey, SkillsEvidenceAuditAction.SkillCreated,
            actorMemberId,
            JsonSerializer.Serialize(new { skill.Name, kind = skill.Kind.ToString(), skill.TaxonomyVersion }),
            now, tenantId);

        return skill;
    }

    public async Task<SkillDefinition> SetSkillActiveAsync(string skillKey, bool isActive, Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var skill = await repository.SetSkillActiveAsync(SkillTaxonomy.NormalizeKey(skillKey), isActive, tenantId, now);

        await auditLog.LogAsync(
            SkillsEvidenceAuditAction.EntityTypeSkill, skill.SkillKey,
            isActive ? SkillsEvidenceAuditAction.SkillReinstated : SkillsEvidenceAuditAction.SkillRetired,
            actorMemberId, null, now, tenantId);

        return skill;
    }

    // ---- helpers ----

    private async Task<StaffSkillAssertion> AppendAndAuditAsync(
        StaffSkillAssertion assertion, StaffSkillAssertion? supersedes, DateTime now, string action, int? actorMemberId)
    {
        var saved = await repository.AppendAsync(assertion, supersedes, now);

        // Deliberately *not* the free-text notes. The audit log outlives
        // the subject's erasure; the notes are the revealing part of the
        // record and go with them. See SkillsEvidenceAuditLogDto and
        // docs/gdpr.md.
        await auditLog.LogAsync(
            SkillsEvidenceAuditAction.EntityTypeAssertion,
            saved.AssertionKey.ToString(),
            action,
            actorMemberId,
            JsonSerializer.Serialize(new
            {
                staffKey = saved.StaffKey,
                skillKey = saved.SkillKey,
                proficiency = saved.Proficiency.ToString(),
                status = saved.Status.ToString(),
                origin = saved.Origin.ToString(),
                reviewerStaffKey = saved.ReviewerStaffKey,
                supersedes = saved.SupersedesAssertionKey
            }),
            now,
            saved.TenantId);

        return saved;
    }

    private async Task<SkillDefinition> RequireActiveSkillAsync(string skillKey, Guid tenantId)
    {
        var normalized = SkillTaxonomy.NormalizeKey(skillKey);
        var skill = await repository.GetSkillAsync(normalized, tenantId)
            ?? throw new CrossTenantReferenceException("Skill", Guid.Empty);

        if (!skill.IsActive)
        {
            throw new SkillAssertionValidationException($"'{skill.Name}' has been retired and cannot take new assertions.");
        }

        return skill;
    }

    private async Task<StaffSkillAssertion> RequireCurrentAsync(Guid assertionKey, Guid tenantId)
    {
        var assertion = await repository.GetAssertionAsync(assertionKey, tenantId)
            ?? throw new CrossTenantReferenceException("SkillAssertion", assertionKey);

        if (!assertion.IsCurrent)
        {
            throw new SkillAssertionValidationException(
                "This is an older version of the record. Reload the page and act on the current one.");
        }

        return assertion;
    }

    private async Task<StaffSkillAssertion?> CurrentForAsync(Guid staffKey, string skillKey, Guid tenantId)
    {
        var current = await repository.GetCurrentForStaffAsync(staffKey, tenantId);
        return current.FirstOrDefault(a => string.Equals(a.SkillKey, skillKey, StringComparison.Ordinal));
    }

    private static ProficiencyLevel Require(ProficiencyLevel level) =>
        Enum.IsDefined(level)
            ? level
            : throw new SkillAssertionValidationException("Choose one of the four proficiency levels.");

    private static string RequireNote(string? note, string message)
    {
        var cleaned = Clean(note);
        return cleaned is null ? throw new SkillAssertionValidationException(message) : cleaned;
    }

    private static string? Clean(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        return trimmed.Length <= MaxNoteLength ? trimmed : trimmed[..MaxNoteLength];
    }
}
