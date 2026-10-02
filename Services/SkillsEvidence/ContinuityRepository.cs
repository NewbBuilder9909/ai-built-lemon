using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.SkillsEvidence;
using Umbraco.Cms.Infrastructure.Scoping;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.SkillsEvidence;

public sealed class ContinuityRepository(IScopeProvider scopeProvider) : IContinuityRepository
{
    // ---- Component ownership ----

    public async Task<IReadOnlyList<ComponentOwnership>> GetComponentsAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ComponentOwnershipDto>(
            Sql.Builder.Where("tenantId = @0", tenantId).OrderBy("displayName"));
        return dtos.Select(Map).ToList();
    }

    public async Task<ComponentOwnership?> GetComponentAsync(string componentKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<ComponentOwnershipDto>(
            Sql.Builder.Where("componentKey = @0 AND tenantId = @1", componentKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<ComponentOwnership> UpsertComponentAsync(ComponentOwnership ownership)
    {
        using var scope = scopeProvider.CreateScope();
        var existing = await scope.Database.FirstOrDefaultAsync<ComponentOwnershipDto>(
            Sql.Builder.Where("tenantId = @0 AND componentKey = @1", ownership.TenantId, ownership.ComponentKey));

        var dto = existing ?? new ComponentOwnershipDto
        {
            OwnershipKey = ownership.OwnershipKey,
            TenantId = ownership.TenantId,
            ComponentKey = ownership.ComponentKey,
            CreatedAtUtc = ownership.CreatedAtUtc
        };

        dto.DisplayName = ownership.DisplayName;
        dto.Description = Truncate(ownership.Description, 1000);
        dto.OwnerStaffKey = ownership.OwnerStaffKey;
        dto.ReviewedByStaffKey = ownership.ReviewedByStaffKey;
        dto.LastReviewedAtUtc = ownership.LastReviewedAtUtc;
        dto.UpdatedAtUtc = ownership.UpdatedAtUtc;

        if (existing is null)
        {
            await scope.Database.InsertAsync(dto);
        }
        else
        {
            await scope.Database.UpdateAsync(dto);
        }

        scope.Complete();
        return Map(dto);
    }

    public async Task DeleteComponentAsync(string componentKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();

        // Backups and actions go with it — they are meaningless attached
        // to a component the tenant no longer tracks.
        await scope.Database.ExecuteAsync(
            $"DELETE FROM [{ComponentBackupDto.TableName}] WHERE [tenantId] = @0 AND [componentKey] = @1", tenantId, componentKey);
        await scope.Database.ExecuteAsync(
            $"DELETE FROM [{ComponentOwnershipDto.TableName}] WHERE [tenantId] = @0 AND [componentKey] = @1", tenantId, componentKey);

        scope.Complete();
    }

    // ---- Backups ----

    public async Task<IReadOnlyList<ComponentBackup>> GetBackupsAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ComponentBackupDto>(Sql.Builder.Where("tenantId = @0", tenantId));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ComponentBackup>> GetBackupsForStaffAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ComponentBackupDto>(Sql.Builder.Where("staffKey = @0", staffKey));
        return dtos.Select(Map).ToList();
    }

    public async Task<ComponentBackup> AddBackupAsync(ComponentBackup backup)
    {
        using var scope = scopeProvider.CreateScope();

        var component = await scope.Database.FirstOrDefaultAsync<ComponentOwnershipDto>(
            Sql.Builder.Where("tenantId = @0 AND componentKey = @1", backup.TenantId, backup.ComponentKey));
        if (component is null)
        {
            throw new CrossTenantReferenceException("Component", backup.BackupKey);
        }

        var clash = await scope.Database.FirstOrDefaultAsync<ComponentBackupDto>(
            Sql.Builder.Where("tenantId = @0 AND componentKey = @1 AND staffKey = @2",
                backup.TenantId, backup.ComponentKey, backup.StaffKey));
        if (clash is not null)
        {
            throw new SkillAssertionValidationException("That person is already approved cover for this component.");
        }

        var dto = new ComponentBackupDto
        {
            BackupKey = backup.BackupKey,
            TenantId = backup.TenantId,
            ComponentKey = backup.ComponentKey,
            StaffKey = backup.StaffKey,
            ApprovedByStaffKey = backup.ApprovedByStaffKey,
            ApprovedAtUtc = backup.ApprovedAtUtc,
            Note = Truncate(backup.Note, 1000)
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();
        return Map(dto);
    }

    public async Task RemoveBackupAsync(Guid backupKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"DELETE FROM [{ComponentBackupDto.TableName}] WHERE [backupKey] = @0 AND [tenantId] = @1", backupKey, tenantId);
        scope.Complete();
    }

    // ---- Actions ----

    public async Task<IReadOnlyList<CoverageAction>> GetActionsAsync(Guid tenantId, bool openOnly)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var sql = openOnly
            ? Sql.Builder.Where("tenantId = @0 AND outcome = @1", tenantId, nameof(CoverageActionOutcome.Open))
            : Sql.Builder.Where("tenantId = @0", tenantId);
        var dtos = await scope.Database.FetchAsync<CoverageActionDto>(sql.OrderBy("raisedAtUtc DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<CoverageAction?> GetActionAsync(Guid actionKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<CoverageActionDto>(
            Sql.Builder.Where("actionKey = @0 AND tenantId = @1", actionKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyList<CoverageAction>> GetActionsForStaffAsync(Guid staffKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<CoverageActionDto>(
            Sql.Builder.Where("ownerStaffKey = @0 AND tenantId = @1", staffKey, tenantId));
        return dtos.Select(Map).ToList();
    }

    public async Task<CoverageAction> UpsertActionAsync(CoverageAction action)
    {
        using var scope = scopeProvider.CreateScope();

        var component = await scope.Database.FirstOrDefaultAsync<ComponentOwnershipDto>(
            Sql.Builder.Where("tenantId = @0 AND componentKey = @1", action.TenantId, action.ComponentKey));
        if (component is null)
        {
            throw new CrossTenantReferenceException("Component", action.ActionKey);
        }

        var existing = await scope.Database.FirstOrDefaultAsync<CoverageActionDto>(
            Sql.Builder.Where("actionKey = @0 AND tenantId = @1", action.ActionKey, action.TenantId));

        var dto = existing ?? new CoverageActionDto
        {
            ActionKey = action.ActionKey,
            TenantId = action.TenantId,
            ComponentKey = action.ComponentKey,
            RaisedByStaffKey = action.RaisedByStaffKey,
            RaisedAtUtc = action.RaisedAtUtc
        };

        dto.ActionType = action.Type.ToString();
        dto.OwnerStaffKey = action.OwnerStaffKey;
        dto.Rationale = Truncate(action.Rationale, 1000)!;
        dto.EvidenceSnapshot = Truncate(action.EvidenceSnapshot, 1000);
        dto.DueOn = action.DueOn?.ToDateTime(TimeOnly.MinValue);
        dto.Outcome = action.Outcome.ToString();
        dto.OutcomeNote = Truncate(action.OutcomeNote, 1000);
        dto.ClosedByStaffKey = action.ClosedByStaffKey;
        dto.ClosedAtUtc = action.ClosedAtUtc;

        if (existing is null)
        {
            await scope.Database.InsertAsync(dto);
        }
        else
        {
            await scope.Database.UpdateAsync(dto);
        }

        scope.Complete();
        return Map(dto);
    }

    // ---- Processing decision ----

    public async Task<EvidenceProcessingDecision?> GetLiveProcessingDecisionAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<EvidenceProcessingDecisionDto>(
            Sql.Builder.Where("tenantId = @0 AND withdrawnAtUtc IS NULL", tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyList<EvidenceProcessingDecision>> GetProcessingDecisionHistoryAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<EvidenceProcessingDecisionDto>(
            Sql.Builder.Where("tenantId = @0", tenantId).OrderBy("decidedAtUtc DESC", "id DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<EvidenceProcessingDecision> SupersedeProcessingDecisionAsync(EvidenceProcessingDecision decision, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();

        // Withdraw then insert, in one transaction, so the filtered
        // unique index never sees two live decisions.
        await scope.Database.ExecuteAsync(
            $"UPDATE [{EvidenceProcessingDecisionDto.TableName}] SET [withdrawnAtUtc] = @0 " +
            "WHERE [tenantId] = @1 AND [withdrawnAtUtc] IS NULL",
            nowUtc, decision.TenantId);

        var dto = new EvidenceProcessingDecisionDto
        {
            DecisionKey = decision.DecisionKey,
            TenantId = decision.TenantId,
            LawfulBasis = decision.LawfulBasis.ToString(),
            WorkerNoticeGiven = decision.WorkerNoticeGiven,
            WorkerNoticeReference = Truncate(decision.WorkerNoticeReference, 512),
            DpiaCompleted = decision.DpiaCompleted,
            DpiaReference = Truncate(decision.DpiaReference, 512),
            DpiaCompletedAtUtc = decision.DpiaCompletedAtUtc,
            Purpose = Truncate(decision.Purpose, 1000)!,
            DecidedByStaffKey = decision.DecidedByStaffKey,
            DecidedAtUtc = decision.DecidedAtUtc,
            ReviewDueOn = decision.ReviewDueOn.ToDateTime(TimeOnly.MinValue)
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();
        return Map(dto);
    }

    public async Task<int> WithdrawProcessingDecisionAsync(Guid tenantId, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var updated = await scope.Database.ExecuteAsync(
            $"UPDATE [{EvidenceProcessingDecisionDto.TableName}] SET [withdrawnAtUtc] = @0 " +
            "WHERE [tenantId] = @1 AND [withdrawnAtUtc] IS NULL",
            nowUtc, tenantId);
        scope.Complete();
        return updated;
    }

    // ---- GDPR ----

    public async Task<(int Ownerships, int Backups, int Actions)> DetachStaffAsync(Guid staffKey, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();

        // The component stays and becomes unowned — which is a finding
        // the continuity view should shout about, not hide.
        var ownerships = await scope.Database.ExecuteAsync(
            $"UPDATE [{ComponentOwnershipDto.TableName}] SET [ownerStaffKey] = NULL, [updatedAtUtc] = @0 WHERE [ownerStaffKey] = @1",
            nowUtc, staffKey);

        // An approved backup who has left is not cover, so the row goes.
        var backups = await scope.Database.ExecuteAsync(
            $"DELETE FROM [{ComponentBackupDto.TableName}] WHERE [staffKey] = @0", staffKey);

        // An open action keeps its rationale and outcome but loses its
        // owner, so it surfaces as needing reassignment rather than
        // quietly disappearing from the plan.
        var actions = await scope.Database.ExecuteAsync(
            $"UPDATE [{CoverageActionDto.TableName}] SET [ownerStaffKey] = NULL WHERE [ownerStaffKey] = @0",
            staffKey);

        scope.Complete();
        return (ownerships, backups, actions);
    }

    // ---- mapping ----

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static ComponentOwnership Map(ComponentOwnershipDto dto) => new()
    {
        OwnershipKey = dto.OwnershipKey,
        TenantId = dto.TenantId,
        ComponentKey = dto.ComponentKey,
        DisplayName = dto.DisplayName,
        Description = dto.Description,
        OwnerStaffKey = dto.OwnerStaffKey,
        ReviewedByStaffKey = dto.ReviewedByStaffKey,
        LastReviewedAtUtc = dto.LastReviewedAtUtc,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static ComponentBackup Map(ComponentBackupDto dto) => new()
    {
        BackupKey = dto.BackupKey,
        TenantId = dto.TenantId,
        ComponentKey = dto.ComponentKey,
        StaffKey = dto.StaffKey,
        ApprovedByStaffKey = dto.ApprovedByStaffKey,
        ApprovedAtUtc = dto.ApprovedAtUtc,
        Note = dto.Note
    };

    private static CoverageAction Map(CoverageActionDto dto) => new()
    {
        ActionKey = dto.ActionKey,
        TenantId = dto.TenantId,
        ComponentKey = dto.ComponentKey,
        Type = Enum.Parse<CoverageActionType>(dto.ActionType),
        OwnerStaffKey = dto.OwnerStaffKey,
        Rationale = dto.Rationale,
        EvidenceSnapshot = dto.EvidenceSnapshot,
        DueOn = dto.DueOn is null ? null : DateOnly.FromDateTime(dto.DueOn.Value),
        Outcome = Enum.Parse<CoverageActionOutcome>(dto.Outcome),
        OutcomeNote = dto.OutcomeNote,
        RaisedByStaffKey = dto.RaisedByStaffKey,
        RaisedAtUtc = dto.RaisedAtUtc,
        ClosedByStaffKey = dto.ClosedByStaffKey,
        ClosedAtUtc = dto.ClosedAtUtc
    };

    private static EvidenceProcessingDecision Map(EvidenceProcessingDecisionDto dto) => new()
    {
        DecisionKey = dto.DecisionKey,
        TenantId = dto.TenantId,
        LawfulBasis = Enum.Parse<EvidenceLawfulBasis>(dto.LawfulBasis),
        WorkerNoticeGiven = dto.WorkerNoticeGiven,
        WorkerNoticeReference = dto.WorkerNoticeReference,
        DpiaCompleted = dto.DpiaCompleted,
        DpiaReference = dto.DpiaReference,
        DpiaCompletedAtUtc = dto.DpiaCompletedAtUtc,
        Purpose = dto.Purpose,
        DecidedByStaffKey = dto.DecidedByStaffKey,
        DecidedAtUtc = dto.DecidedAtUtc,
        ReviewDueOn = DateOnly.FromDateTime(dto.ReviewDueOn),
        WithdrawnAtUtc = dto.WithdrawnAtUtc
    };
}
