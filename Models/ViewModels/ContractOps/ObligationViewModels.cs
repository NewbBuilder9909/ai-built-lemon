using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ContractOps;

namespace ProgrammePulse.Models.ViewModels.ContractOps;

/// <summary>Form input for recording one obligation from a contract clause.</summary>
public sealed class ObligationInput
{
    public ObligationKind Kind { get; set; }

    public string? ClauseReference { get; set; }

    public string? Description { get; set; }

    public FindingSeverity? SeverityThreshold { get; set; }

    public GateAction? Action { get; set; }

    public int? RemediationDays { get; set; }
}

/// <summary>/staffops/contracts/{key}/obligations: one contract's obligations and how each stands.</summary>
public sealed record ContractObligationsPageViewModel(
    Contract Contract,
    string CustomerName,
    IReadOnlyList<ObligationAssessment> Assessments,
    IReadOnlyList<InScopeRepository> RepositoriesInScope,
    string? Message,
    bool SecurityToolConnected = false,
    DateTime? SecurityToolSyncedAtUtc = null,
    bool CheckRunsVerified = false);

/// <summary>/staffops/contracts/assurance: every active obligation across the portfolio, gaps first.</summary>
public sealed record AssurancePortfolioViewModel(
    IReadOnlyList<AssurancePortfolioRow> Rows,
    IReadOnlyList<RepositoryAttentionRow> RepositoriesNeedingAttention,
    DateTime AsOfUtc,
    bool SecurityToolConnected = false,
    DateTime? SecurityToolSyncedAtUtc = null);

public sealed record AssurancePortfolioRow(
    Guid ContractKey,
    string ContractReference,
    string CustomerName,
    ContractObligation Obligation,
    ObligationAssuranceStatus Status,
    int Enforced,
    int NotEnforced,
    int UnknownOrExpired);

/// <summary>A repository whose required control is not shown to be in place, with every contract that depends on it.</summary>
public sealed record RepositoryAttentionRow(
    CodeRepositoryRef Repository,
    RepositoryControl Control,
    RepositoryAssuranceStatus Status,
    IReadOnlyList<string> Contracts,
    DateTime? AttestationExpiresAtUtc,
    AssuranceSource Source = AssuranceSource.Attestation);
