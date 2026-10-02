using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// In-memory stand-in for ContinuityRepository. A fake, not a mock.
///
/// It reproduces the two constraints the real schema enforces: one
/// ownership record per (tenant, component), and — the interesting one —
/// at most one *live* processing decision per tenant, which a filtered
/// unique index guarantees in SQL Server and
/// <see cref="SupersedeProcessingDecisionAsync"/> reproduces here. A
/// service bug that left two live decisions would otherwise pass every
/// test and fail only against the database.
/// </summary>
public sealed class FakeContinuityRepository : IContinuityRepository
{
    public readonly List<ComponentOwnership> Components = [];
    public readonly List<ComponentBackup> Backups = [];
    public readonly List<CoverageAction> Actions = [];
    public readonly List<EvidenceProcessingDecision> Decisions = [];

    // ---- Components ----

    public Task<IReadOnlyList<ComponentOwnership>> GetComponentsAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<ComponentOwnership>>(Components.Where(c => c.TenantId == tenantId).ToList());

    public Task<ComponentOwnership?> GetComponentAsync(string componentKey, Guid tenantId) =>
        Task.FromResult(Components.FirstOrDefault(c =>
            c.TenantId == tenantId && string.Equals(c.ComponentKey, componentKey, StringComparison.OrdinalIgnoreCase)));

    public Task<ComponentOwnership> UpsertComponentAsync(ComponentOwnership ownership)
    {
        var existing = Components.FirstOrDefault(c =>
            c.TenantId == ownership.TenantId
            && string.Equals(c.ComponentKey, ownership.ComponentKey, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            var merged = ownership with { OwnershipKey = existing.OwnershipKey, CreatedAtUtc = existing.CreatedAtUtc };
            Components[Components.IndexOf(existing)] = merged;
            return Task.FromResult(merged);
        }

        Components.Add(ownership);
        return Task.FromResult(ownership);
    }

    public Task DeleteComponentAsync(string componentKey, Guid tenantId)
    {
        Backups.RemoveAll(b => b.TenantId == tenantId && string.Equals(b.ComponentKey, componentKey, StringComparison.OrdinalIgnoreCase));
        Components.RemoveAll(c => c.TenantId == tenantId && string.Equals(c.ComponentKey, componentKey, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    // ---- Backups ----

    public Task<IReadOnlyList<ComponentBackup>> GetBackupsAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<ComponentBackup>>(Backups.Where(b => b.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<ComponentBackup>> GetBackupsForStaffAsync(Guid staffKey) =>
        Task.FromResult<IReadOnlyList<ComponentBackup>>(Backups.Where(b => b.StaffKey == staffKey).ToList());

    public Task<ComponentBackup> AddBackupAsync(ComponentBackup backup)
    {
        if (!Components.Any(c => c.TenantId == backup.TenantId
            && string.Equals(c.ComponentKey, backup.ComponentKey, StringComparison.OrdinalIgnoreCase)))
        {
            throw new CrossTenantReferenceException("Component", backup.BackupKey);
        }

        if (Backups.Any(b => b.TenantId == backup.TenantId
            && string.Equals(b.ComponentKey, backup.ComponentKey, StringComparison.OrdinalIgnoreCase)
            && b.StaffKey == backup.StaffKey))
        {
            throw new SkillAssertionValidationException("That person is already approved cover for this component.");
        }

        Backups.Add(backup);
        return Task.FromResult(backup);
    }

    public Task RemoveBackupAsync(Guid backupKey, Guid tenantId)
    {
        Backups.RemoveAll(b => b.BackupKey == backupKey && b.TenantId == tenantId);
        return Task.CompletedTask;
    }

    // ---- Actions ----

    public Task<IReadOnlyList<CoverageAction>> GetActionsAsync(Guid tenantId, bool openOnly) =>
        Task.FromResult<IReadOnlyList<CoverageAction>>(
            Actions.Where(a => a.TenantId == tenantId && (!openOnly || a.IsOpen)).ToList());

    public Task<CoverageAction?> GetActionAsync(Guid actionKey, Guid tenantId) =>
        Task.FromResult(Actions.FirstOrDefault(a => a.ActionKey == actionKey && a.TenantId == tenantId));

    public Task<IReadOnlyList<CoverageAction>> GetActionsForStaffAsync(Guid staffKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<CoverageAction>>(
            Actions.Where(a => a.OwnerStaffKey == staffKey && a.TenantId == tenantId).ToList());

    public Task<CoverageAction> UpsertActionAsync(CoverageAction action)
    {
        if (!Components.Any(c => c.TenantId == action.TenantId
            && string.Equals(c.ComponentKey, action.ComponentKey, StringComparison.OrdinalIgnoreCase)))
        {
            throw new CrossTenantReferenceException("Component", action.ActionKey);
        }

        var existing = Actions.FirstOrDefault(a => a.ActionKey == action.ActionKey && a.TenantId == action.TenantId);
        if (existing is not null)
        {
            Actions[Actions.IndexOf(existing)] = action;
            return Task.FromResult(action);
        }

        Actions.Add(action);
        return Task.FromResult(action);
    }

    // ---- Processing decision ----

    public Task<EvidenceProcessingDecision?> GetLiveProcessingDecisionAsync(Guid tenantId) =>
        Task.FromResult(Decisions.FirstOrDefault(d => d.TenantId == tenantId && d.WithdrawnAtUtc is null));

    public Task<IReadOnlyList<EvidenceProcessingDecision>> GetProcessingDecisionHistoryAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<EvidenceProcessingDecision>>(
            Decisions.Where(d => d.TenantId == tenantId).OrderByDescending(d => d.DecidedAtUtc).ToList());

    public Task<EvidenceProcessingDecision> SupersedeProcessingDecisionAsync(EvidenceProcessingDecision decision, DateTime nowUtc)
    {
        // Stands in for UX_SkillsEvidence_ProcessingDecision_live.
        foreach (var live in Decisions.Where(d => d.TenantId == decision.TenantId && d.WithdrawnAtUtc is null).ToList())
        {
            Decisions[Decisions.IndexOf(live)] = live with { WithdrawnAtUtc = nowUtc };
        }

        Decisions.Add(decision);
        return Task.FromResult(decision);
    }

    public Task<int> WithdrawProcessingDecisionAsync(Guid tenantId, DateTime nowUtc)
    {
        var live = Decisions.Where(d => d.TenantId == tenantId && d.WithdrawnAtUtc is null).ToList();

        foreach (var decision in live)
        {
            Decisions[Decisions.IndexOf(decision)] = decision with { WithdrawnAtUtc = nowUtc };
        }

        return Task.FromResult(live.Count);
    }

    // ---- GDPR ----

    public Task<(int Ownerships, int Backups, int Actions)> DetachStaffAsync(Guid staffKey, DateTime nowUtc)
    {
        var owned = Components.Where(c => c.OwnerStaffKey == staffKey).ToList();
        foreach (var component in owned)
        {
            Components[Components.IndexOf(component)] = component with { OwnerStaffKey = null, UpdatedAtUtc = nowUtc };
        }

        var removedBackups = Backups.RemoveAll(b => b.StaffKey == staffKey);

        // Null, matching the real repository's `SET ownerStaffKey = NULL`
        // — not Guid.Empty, which would read as a real value in a join.
        var owns = Actions.Where(a => a.OwnerStaffKey == staffKey).ToList();
        foreach (var action in owns)
        {
            Actions[Actions.IndexOf(action)] = action with { OwnerStaffKey = null };
        }

        return Task.FromResult((owned.Count, removedBackups, owns.Count));
    }
}
