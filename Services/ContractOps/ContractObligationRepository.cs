using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Shared;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// The obligations register and repository control attestations
/// (ContractOps_Obligation, ContractOps_RepositoryControlAttestation). One
/// repository for both, following the ProgrammeRepository precedent: they are
/// always read together. Every method takes the tenant and applies it in SQL.
/// </summary>
public interface IContractObligationRepository
{
    Task<IReadOnlyList<ContractObligation>> GetLiveForContractAsync(Guid contractKey, Guid tenantId);

    Task<IReadOnlyList<ContractObligation>> GetLiveForTenantAsync(Guid tenantId);

    /// <summary>Throws CrossTenantReferenceException if the contract is not the tenant's.</summary>
    Task AddAsync(ContractObligation obligation);

    /// <summary>Ends a live obligation, keeping it on record; false if there was none.</summary>
    Task<bool> WithdrawAsync(Guid obligationKey, Guid tenantId, Guid? withdrawnByStaffKey, DateTime nowUtc);

    /// <summary>The current (not superseded) attestation per repository and control, expired or not.</summary>
    Task<IReadOnlyList<RepositoryControlAttestation>> GetCurrentAttestationsAsync(Guid tenantId);

    /// <summary>Supersedes any current attestation for the same repository and control, then records this one.</summary>
    Task RecordAttestationAsync(RepositoryControlAttestation attestation);
}

public sealed class ContractObligationRepository(IScopeProvider scopeProvider) : IContractObligationRepository
{
    public async Task<IReadOnlyList<ContractObligation>> GetLiveForContractAsync(Guid contractKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ContractObligationDto>(
            Sql.Builder.Where("tenantId = @0 AND contractKey = @1 AND withdrawnAtUtc IS NULL", tenantId, contractKey).OrderBy("recordedAtUtc", "id"));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ContractObligation>> GetLiveForTenantAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ContractObligationDto>(
            Sql.Builder.Where("tenantId = @0 AND withdrawnAtUtc IS NULL", tenantId).OrderBy("contractKey", "recordedAtUtc", "id"));
        return dtos.Select(Map).ToList();
    }

    public async Task AddAsync(ContractObligation obligation)
    {
        using var scope = scopeProvider.CreateScope();
        var owned = await scope.Database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {ContractDto.TableName} WHERE contractKey = @0 AND tenantId = @1",
            obligation.ContractKey, obligation.TenantId);
        if (owned == 0)
        {
            throw new CrossTenantReferenceException("Contract", obligation.ContractKey);
        }

        await scope.Database.InsertAsync(new ContractObligationDto
        {
            ObligationKey = obligation.ObligationKey,
            TenantId = obligation.TenantId,
            ContractKey = obligation.ContractKey,
            Kind = obligation.Kind.ToString(),
            ClauseReference = obligation.ClauseReference,
            Description = obligation.Description,
            SeverityThreshold = obligation.SeverityThreshold?.ToString(),
            GateAction = obligation.Action?.ToString(),
            RemediationDays = obligation.RemediationDays,
            RecordedByStaffKey = obligation.RecordedByStaffKey,
            RecordedAtUtc = obligation.RecordedAtUtc,
        });
        scope.Complete();
    }

    public async Task<bool> WithdrawAsync(Guid obligationKey, Guid tenantId, Guid? withdrawnByStaffKey, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var ended = await scope.Database.ExecuteAsync(
            $"UPDATE {ContractObligationDto.TableName} SET withdrawnAtUtc = @0, withdrawnByStaffKey = @1 WHERE obligationKey = @2 AND tenantId = @3 AND withdrawnAtUtc IS NULL",
            new object[] { nowUtc, withdrawnByStaffKey!, obligationKey, tenantId });
        scope.Complete();
        return ended == 1;
    }

    public async Task<IReadOnlyList<RepositoryControlAttestation>> GetCurrentAttestationsAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<RepositoryControlAttestationDto>(
            Sql.Builder.Where("tenantId = @0 AND supersededAtUtc IS NULL", tenantId));
        return dtos.Select(Map).ToList();
    }

    public async Task RecordAttestationAsync(RepositoryControlAttestation attestation)
    {
        var repository = attestation.Repository;
        using var scope = scopeProvider.CreateScope();

        // Supersede and insert in one transaction; the filtered unique index
        // guarantees a single current attestation even if two admins race.
        await scope.Database.ExecuteAsync(
            $"""
            UPDATE {RepositoryControlAttestationDto.TableName} WITH (UPDLOCK, HOLDLOCK)
            SET supersededAtUtc = @0
            WHERE tenantId = @1 AND provider = @2 AND sourceAccountId = @3 AND repositoryKey = @4 AND control = @5 AND supersededAtUtc IS NULL
            """,
            new object[] { attestation.AttestedAtUtc, attestation.TenantId, repository.Provider, repository.SourceAccountId, repository.RepositoryKey, attestation.Control.ToString() });

        await scope.Database.InsertAsync(new RepositoryControlAttestationDto
        {
            AttestationKey = attestation.AttestationKey,
            TenantId = attestation.TenantId,
            Provider = repository.Provider,
            SourceAccountId = repository.SourceAccountId,
            RepositoryKey = repository.RepositoryKey,
            Control = attestation.Control.ToString(),
            State = attestation.State.ToString(),
            EvidenceReference = attestation.EvidenceReference,
            AttestedByStaffKey = attestation.AttestedByStaffKey,
            AttestedAtUtc = attestation.AttestedAtUtc,
            ExpiresAtUtc = attestation.ExpiresAtUtc,
        });
        scope.Complete();
    }

    private static ContractObligation Map(ContractObligationDto dto) => new()
    {
        ObligationKey = dto.ObligationKey,
        TenantId = dto.TenantId,
        ContractKey = dto.ContractKey,
        Kind = Enum.Parse<ObligationKind>(dto.Kind),
        ClauseReference = dto.ClauseReference,
        Description = dto.Description,
        SeverityThreshold = dto.SeverityThreshold is null ? null : Enum.Parse<FindingSeverity>(dto.SeverityThreshold),
        Action = dto.GateAction is null ? null : Enum.Parse<GateAction>(dto.GateAction),
        RemediationDays = dto.RemediationDays,
        RecordedByStaffKey = dto.RecordedByStaffKey,
        RecordedAtUtc = dto.RecordedAtUtc,
        WithdrawnAtUtc = dto.WithdrawnAtUtc,
        WithdrawnByStaffKey = dto.WithdrawnByStaffKey,
    };

    private static RepositoryControlAttestation Map(RepositoryControlAttestationDto dto) => new()
    {
        AttestationKey = dto.AttestationKey,
        TenantId = dto.TenantId,
        Repository = new CodeRepositoryRef(dto.Provider, dto.SourceAccountId, dto.RepositoryKey),
        Control = Enum.Parse<RepositoryControl>(dto.Control),
        State = Enum.Parse<ControlState>(dto.State),
        EvidenceReference = dto.EvidenceReference,
        AttestedByStaffKey = dto.AttestedByStaffKey,
        AttestedAtUtc = dto.AttestedAtUtc,
        ExpiresAtUtc = dto.ExpiresAtUtc,
        SupersededAtUtc = dto.SupersededAtUtc,
    };
}
