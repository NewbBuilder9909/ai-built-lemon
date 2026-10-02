using System.Text.Json;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.ViewModels.ContractOps;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.SecurityAssurance;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// Step 2 of docs/delivery-evidence-and-contract-assurance.md: the register
/// of contract obligations with an engineering consequence, dated
/// attestations of the repository controls they need, and the assurance view
/// that joins them through the declared repository → project links.
///
/// Reports; never enforces (decision 1). Blocking stays in the customer's CI.
/// </summary>
public interface IContractAssuranceService
{
    Task<ContractObligationsPageViewModel?> BuildContractPageAsync(Guid tenantId, Guid contractKey, string? message);

    Task<AssurancePortfolioViewModel> BuildPortfolioAsync(Guid tenantId);

    Task<CommandOutcome> AddObligationAsync(Guid tenantId, Guid contractKey, ObligationInput input, Guid? actorStaffKey, int? actorMemberId);

    Task<CommandOutcome> WithdrawObligationAsync(Guid tenantId, Guid obligationKey, Guid? actorStaffKey, int? actorMemberId);

    Task<CommandOutcome> AttestAsync(
        Guid tenantId, string? provider, string? sourceAccountId, string? repositoryKey, RepositoryControl control, ControlState state,
        string? evidenceReference, Guid? actorStaffKey, int? actorMemberId);
}

public sealed class ContractAssuranceService(
    IContractRepository contracts,
    IContractObligationRepository obligations,
    ICodeRepositoryLinkRepository repositoryLinks,
    IProgrammeReadRepository programmes,
    IContractAuditLogRepository audit,
    ISecurityAssuranceQueryService securityAssurance,
    TimeProvider timeProvider) : IContractAssuranceService
{
    public const string ObligationAuditEntity = "ContractObligation";
    public const string AttestationAuditEntity = "RepositoryControlAttestation";

    /// <summary>How far back bypassed or timed-out pull-request checks are shown.</summary>
    public const int CheckRunWindowDays = 90;

    public async Task<ContractObligationsPageViewModel?> BuildContractPageAsync(Guid tenantId, Guid contractKey, string? message)
    {
        var contract = await contracts.GetContractByKeyAsync(contractKey, tenantId);
        if (contract is null)
        {
            return null;
        }

        var scope = await LoadScopeAsync(tenantId);
        var inScope = scope.RepositoriesFor(contract.CustomerKey);
        var attestations = await obligations.GetCurrentAttestationsAsync(tenantId);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var observed = await securityAssurance.GetSnapshotAsync(tenantId, now.AddDays(-CheckRunWindowDays));

        var assessments = (await obligations.GetLiveForContractAsync(contractKey, tenantId))
            .Select(o => ObligationAssurance.Evaluate(o, contract, inScope, attestations, now, observed))
            .ToList();

        return new ContractObligationsPageViewModel(contract, scope.CustomerName(contract.CustomerKey), assessments, inScope, message, observed.Connected, observed.LastSyncSucceededAtUtc, observed.CheckRunsVerified);
    }

    public async Task<AssurancePortfolioViewModel> BuildPortfolioAsync(Guid tenantId)
    {
        var scope = await LoadScopeAsync(tenantId);
        var contractsByKey = (await contracts.GetContractsAsync(tenantId)).ToDictionary(c => c.ContractKey);
        var attestations = await obligations.GetCurrentAttestationsAsync(tenantId);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var observed = await securityAssurance.GetSnapshotAsync(tenantId, now.AddDays(-CheckRunWindowDays));

        var assessed = (await obligations.GetLiveForTenantAsync(tenantId))
            .Where(o => contractsByKey.ContainsKey(o.ContractKey))
            .Select(o =>
            {
                var contract = contractsByKey[o.ContractKey];
                return (Contract: contract, Assessment: ObligationAssurance.Evaluate(o, contract, scope.RepositoriesFor(contract.CustomerKey), attestations, now, observed));
            })
            .ToList();

        var rows = assessed
            .Select(a => new AssurancePortfolioRow(
                a.Contract.ContractKey, a.Contract.Reference, scope.CustomerName(a.Contract.CustomerKey),
                a.Assessment.Obligation, a.Assessment.Status,
                a.Assessment.Repositories.Count(r => r.Status == RepositoryAssuranceStatus.Enforced),
                a.Assessment.Repositories.Count(r => r.IsGap),
                a.Assessment.Repositories.Count(r => r.IsUnknown)))
            // Gaps first, then unknowns: the order a contract owner should act in.
            .OrderBy(r => r.Status switch
            {
                ObligationAssuranceStatus.Gap => 0,
                ObligationAssuranceStatus.Unknown => 1,
                ObligationAssuranceStatus.NoRepositoriesLinked => 2,
                ObligationAssuranceStatus.AllAttestedEnforced => 3,
                ObligationAssuranceStatus.NotAssessedHere => 4,
                _ => 5,
            })
            .ThenBy(r => r.CustomerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ContractReference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // One row per repository and control, naming every contract that needs it:
        // a shared repository carries all of its customers' obligations.
        var attention = assessed
            .SelectMany(a => a.Assessment.Repositories
                .Where(r => r.Status != RepositoryAssuranceStatus.Enforced)
                .Select(r => (Repository: r, Control: a.Assessment.Obligation.RequiredControl!.Value, a.Contract.Reference)))
            .GroupBy(x => (x.Repository.Repository.DisplayName, x.Control))
            .Select(g =>
            {
                // Obligations on one control can differ in deadline; show the worst.
                var worst = g.OrderByDescending(x => x.Repository.IsGap).First().Repository;
                return new RepositoryAttentionRow(
                    worst.Repository, g.Key.Control, worst.Status,
                    g.Select(x => x.Reference).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
                    worst.Attestation?.ExpiresAtUtc, worst.Source);
            })
            .OrderBy(r => r.Status is RepositoryAssuranceStatus.NotEnforced or RepositoryAssuranceStatus.FindingsOverdue ? 0 : 1)
            .ThenBy(r => r.Repository.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AssurancePortfolioViewModel(rows, attention, now, observed.Connected, observed.LastSyncSucceededAtUtc);
    }

    public async Task<CommandOutcome> AddObligationAsync(Guid tenantId, Guid contractKey, ObligationInput input, Guid? actorStaffKey, int? actorMemberId)
    {
        var clause = input.ClauseReference?.Trim();
        var description = input.Description?.Trim();
        if (string.IsNullOrEmpty(clause) || clause.Length > 128)
        {
            return CommandOutcome.Invalid("Give the clause reference as the contract numbers it (up to 128 characters).");
        }

        if (string.IsNullOrEmpty(description) || description.Length > 1000)
        {
            return CommandOutcome.Invalid("Describe what the clause requires (up to 1,000 characters).");
        }

        if (!Enum.IsDefined(input.Kind))
        {
            return CommandOutcome.Invalid("Choose what kind of obligation this is.");
        }

        var isGate = input.Kind == ObligationKind.SecurityFindingGate;
        if (isGate && (input.SeverityThreshold is null || input.Action is null))
        {
            return CommandOutcome.Invalid("A security gate needs a severity threshold and an action.");
        }

        if (isGate && input.Action == GateAction.RemediateWithin && input.RemediationDays is not (>= 1 and <= 365))
        {
            return CommandOutcome.Invalid("Give the remediation deadline in days, between 1 and 365.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var obligation = new ContractObligation
        {
            ObligationKey = Guid.NewGuid(),
            TenantId = tenantId,
            ContractKey = contractKey,
            Kind = input.Kind,
            ClauseReference = clause,
            Description = description,
            SeverityThreshold = isGate ? input.SeverityThreshold : null,
            Action = isGate ? input.Action : null,
            RemediationDays = isGate && input.Action == GateAction.RemediateWithin ? input.RemediationDays : null,
            RecordedByStaffKey = actorStaffKey,
            RecordedAtUtc = now,
        };

        // Another tenant's contract throws CrossTenantReferenceException (a 404).
        await obligations.AddAsync(obligation);
        await LogAsync(ObligationAuditEntity, obligation.ObligationKey, "Recorded", new
        {
            contractKey,
            kind = obligation.Kind.ToString(),
            clause,
            severity = obligation.SeverityThreshold?.ToString(),
            action = obligation.Action?.ToString(),
            obligation.RemediationDays
        }, actorMemberId, tenantId, now);
        return CommandOutcome.Ok;
    }

    public async Task<CommandOutcome> WithdrawObligationAsync(Guid tenantId, Guid obligationKey, Guid? actorStaffKey, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (!await obligations.WithdrawAsync(obligationKey, tenantId, actorStaffKey, now))
        {
            return CommandOutcome.Invalid("That obligation has already been withdrawn.");
        }

        await LogAsync(ObligationAuditEntity, obligationKey, "Withdrawn", null, actorMemberId, tenantId, now);
        return CommandOutcome.Ok;
    }

    public async Task<CommandOutcome> AttestAsync(
        Guid tenantId, string? provider, string? sourceAccountId, string? repositoryKey, RepositoryControl control, ControlState state,
        string? evidenceReference, Guid? actorStaffKey, int? actorMemberId)
    {
        var repository = CodeRepositoryRef.TryCreate(provider, sourceAccountId, repositoryKey, out var error);
        if (repository is null)
        {
            return CommandOutcome.Invalid(error!);
        }

        if (!Enum.IsDefined(control) || !Enum.IsDefined(state))
        {
            return CommandOutcome.Invalid("Choose the control and whether it is in place.");
        }

        var evidence = evidenceReference?.Trim();
        if (string.IsNullOrEmpty(evidence) || evidence.Length > 512)
        {
            return CommandOutcome.Invalid("Say where the evidence is and who confirmed it (up to 512 characters).");
        }

        // An attestation for a repository no project claims would count towards
        // no contract; link it first, so the evidence has somewhere to go.
        if (!(await repositoryLinks.GetLiveAsync(tenantId)).Any(l => l.Repository.SameRepositoryAs(repository)))
        {
            return CommandOutcome.Invalid($"{repository.DisplayName} is not linked to any project. Link it on the code repositories page first.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var attestation = new RepositoryControlAttestation
        {
            AttestationKey = Guid.NewGuid(),
            TenantId = tenantId,
            Repository = repository,
            Control = control,
            State = state,
            EvidenceReference = evidence,
            AttestedByStaffKey = actorStaffKey,
            AttestedAtUtc = now,
            ExpiresAtUtc = now.AddDays(RepositoryControlAttestation.ValidityDays),
        };

        await obligations.RecordAttestationAsync(attestation);
        await LogAsync(AttestationAuditEntity, attestation.AttestationKey, "Attested", new
        {
            repository.Provider,
            repository.SourceAccountId,
            repository.RepositoryKey,
            control = control.ToString(),
            state = state.ToString(),
            evidence,
            attestation.ExpiresAtUtc
        }, actorMemberId, tenantId, now);
        return CommandOutcome.Ok;
    }

    private async Task<TenantScope> LoadScopeAsync(Guid tenantId) => new(
        (await programmes.GetCustomersAsync(tenantId)).ToDictionary(c => c.CustomerKey, c => c.Name),
        await programmes.GetProgrammesAsync(tenantId),
        await programmes.GetProjectsAsync(tenantId),
        await repositoryLinks.GetLiveAsync(tenantId));

    private Task LogAsync(string entityType, Guid entityId, string action, object? detail, int? actorMemberId, Guid tenantId, DateTime nowUtc) =>
        audit.LogAsync(entityType, entityId.ToString(), action, actorMemberId,
            detail is null ? null : JsonSerializer.Serialize(detail), nowUtc, tenantId);

    /// <summary>The tenant's customers, programmes, projects and live repository links, read once per page.</summary>
    private sealed record TenantScope(
        IReadOnlyDictionary<Guid, string> CustomerNames,
        IReadOnlyList<Programme> Programmes,
        IReadOnlyList<Project> Projects,
        IReadOnlyList<CodeRepositoryLink> Links)
    {
        public string CustomerName(Guid customerKey) => CustomerNames.GetValueOrDefault(customerKey) ?? "(unknown customer)";

        /// <summary>Repositories linked to any project of any programme of this customer, with those projects named.</summary>
        public IReadOnlyList<InScopeRepository> RepositoriesFor(Guid customerKey)
        {
            var programmesByKey = Programmes.Where(p => p.CustomerKey == customerKey).ToDictionary(p => p.ProgrammeKey);
            var projectLabels = Projects
                .Where(p => programmesByKey.ContainsKey(p.ProgrammeKey))
                .ToDictionary(p => p.ProjectKey, p => $"{programmesByKey[p.ProgrammeKey].Name} › {p.Name}");

            return Links
                .Where(l => projectLabels.ContainsKey(l.ProjectKey))
                .GroupBy(l => l.Repository.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(g => new InScopeRepository(g.First().Repository,
                    g.Select(l => projectLabels[l.ProjectKey]).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList()))
                .OrderBy(r => r.Repository.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
