using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// Persistence for the Slice 4 aggregates: component ownership, approved
/// backups, coverage actions and the tenant's data-processing decision.
///
/// A third repository in this feature area rather than more methods on
/// ISkillsEvidenceRepository, which already covers the skills matrix and
/// is large. The precedent in CLAUDE.md is one repository per *related*
/// group of aggregates, not one per area at any size — and these four
/// are read together by the continuity view and by nothing else.
///
/// Every method takes <c>tenantId</c> and applies it in the query. The
/// GDPR helpers are keyed on a StaffKey, already an isolation boundary,
/// consistent with the other participants in this codebase.
/// </summary>
public interface IContinuityRepository
{
    // ---- Component ownership ----

    Task<IReadOnlyList<ComponentOwnership>> GetComponentsAsync(Guid tenantId);

    Task<ComponentOwnership?> GetComponentAsync(string componentKey, Guid tenantId);

    Task<ComponentOwnership> UpsertComponentAsync(ComponentOwnership ownership);

    Task DeleteComponentAsync(string componentKey, Guid tenantId);

    // ---- Approved backups ----

    Task<IReadOnlyList<ComponentBackup>> GetBackupsAsync(Guid tenantId);

    Task<IReadOnlyList<ComponentBackup>> GetBackupsForStaffAsync(Guid staffKey);

    /// <summary>
    /// Throws <see cref="Shared.CrossTenantReferenceException"/> if
    /// the component is not this tenant's, and
    /// <see cref="SkillAssertionValidationException"/> if the person is
    /// already approved cover for it.
    /// </summary>
    Task<ComponentBackup> AddBackupAsync(ComponentBackup backup);

    Task RemoveBackupAsync(Guid backupKey, Guid tenantId);

    // ---- Coverage actions (Platinum) ----

    Task<IReadOnlyList<CoverageAction>> GetActionsAsync(Guid tenantId, bool openOnly);

    Task<CoverageAction?> GetActionAsync(Guid actionKey, Guid tenantId);

    Task<IReadOnlyList<CoverageAction>> GetActionsForStaffAsync(Guid staffKey, Guid tenantId);

    Task<CoverageAction> UpsertActionAsync(CoverageAction action);

    // ---- Processing decision ----

    /// <summary>The live decision, or null. At most one per tenant — a filtered unique index enforces it.</summary>
    Task<EvidenceProcessingDecision?> GetLiveProcessingDecisionAsync(Guid tenantId);

    /// <summary>Every decision ever recorded, newest first — the history of what was relied on and when.</summary>
    Task<IReadOnlyList<EvidenceProcessingDecision>> GetProcessingDecisionHistoryAsync(Guid tenantId);

    /// <summary>
    /// Withdraws the current live decision (if any) and inserts the new
    /// one, in one transaction. Superseding rather than editing is what
    /// keeps "what were we relying on in March" answerable.
    /// </summary>
    Task<EvidenceProcessingDecision> SupersedeProcessingDecisionAsync(EvidenceProcessingDecision decision, DateTime nowUtc);

    Task<int> WithdrawProcessingDecisionAsync(Guid tenantId, DateTime nowUtc);

    // ---- GDPR ----

    /// <summary>
    /// Erasure. Clears the subject from ownership and action ownership
    /// rather than deleting the records — see ContinuityDataParticipant
    /// for why a continuity plan outlives the person named on it.
    /// Returns (ownerships cleared, backups removed, actions reassigned).
    /// </summary>
    Task<(int Ownerships, int Backups, int Actions)> DetachStaffAsync(Guid staffKey, DateTime nowUtc);
}
