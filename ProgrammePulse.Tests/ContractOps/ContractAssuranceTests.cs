using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.ViewModels.ContractOps;
using ProgrammePulse.Services.ContractOps;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Tests.ProgrammeOps;
using ProgrammePulse.Tests.SecurityAssurance;

namespace ProgrammePulse.Tests.ContractOps;

/// <summary>
/// Step 2 of docs/delivery-evidence-and-contract-assurance.md. The rules that
/// matter most are negative: an expired or missing attestation is never read
/// as in place; a contract with no linked repository is not assessable, not
/// clean; a shared repository carries every customer's obligation.
/// </summary>
public sealed class ContractAssuranceTests
{
    private static readonly Guid Tenant = Guid.Parse("c0a70000-0000-0000-0000-000000000001");
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly CodeRepositoryRef Api = new("GitHub", "acme", "acme/api");
    private static readonly CodeRepositoryRef Web = new("GitHub", "acme", "acme/web");

    // ---- The pure assessment ----

    [Fact]
    public void Missing_and_expired_attestations_are_unknown_never_in_place()
    {
        var result = ObligationAssurance.Evaluate(Gate(GateAction.Block), ActiveContract(), [InScope(Api), InScope(Web)],
            [Attest(Api, RepositoryControl.SecurityCheckRequired, ControlState.Enforced, attestedDaysAgo: 91)], Now);

        Assert.Equal(ObligationAssuranceStatus.Unknown, result.Status);
        Assert.Equal([RepositoryAssuranceStatus.Expired, RepositoryAssuranceStatus.Unknown], result.Repositories.Select(r => r.Status));
    }

    [Fact]
    public void One_attested_gap_outranks_unknowns_and_all_enforced_is_the_only_clean_result()
    {
        var gap = ObligationAssurance.Evaluate(Gate(GateAction.Block), ActiveContract(), [InScope(Api), InScope(Web)],
            [Attest(Web, RepositoryControl.SecurityCheckRequired, ControlState.NotEnforced)], Now);
        var clean = ObligationAssurance.Evaluate(Gate(GateAction.Block), ActiveContract(), [InScope(Api), InScope(Web)],
            [Attest(Api, RepositoryControl.SecurityCheckRequired, ControlState.Enforced), Attest(Web, RepositoryControl.SecurityCheckRequired, ControlState.Enforced)], Now);

        Assert.Equal(ObligationAssuranceStatus.Gap, gap.Status);
        Assert.Equal(ObligationAssuranceStatus.AllAttestedEnforced, clean.Status);
    }

    [Fact]
    public void An_attestation_of_the_wrong_control_or_a_superseded_one_does_not_count()
    {
        var result = ObligationAssurance.Evaluate(Gate(GateAction.Block), ActiveContract(), [InScope(Api)],
        [
            Attest(Api, RepositoryControl.SecurityScanningEnabled, ControlState.Enforced),
            Attest(Api, RepositoryControl.SecurityCheckRequired, ControlState.Enforced) with { SupersededAtUtc = Now.AddDays(-1) },
        ], Now);

        Assert.Equal(RepositoryAssuranceStatus.Unknown, Assert.Single(result.Repositories).Status);
    }

    [Fact]
    public void Each_action_needs_its_own_control()
    {
        Assert.Equal(RepositoryControl.SecurityCheckRequired, Gate(GateAction.Block).RequiredControl);
        Assert.Equal(RepositoryControl.SecurityScanningEnabled, Gate(GateAction.RemediateWithin).RequiredControl);
        Assert.Null((Gate(GateAction.Block) with { Kind = ObligationKind.DataResidency }).RequiredControl);
    }

    [Fact]
    public void No_linked_repository_is_not_assessable_and_non_repository_obligations_are_not_assessed_here()
    {
        Assert.Equal(ObligationAssuranceStatus.NoRepositoriesLinked,
            ObligationAssurance.Evaluate(Gate(GateAction.Block), ActiveContract(), [], [], Now).Status);
        Assert.Equal(ObligationAssuranceStatus.NotAssessedHere,
            ObligationAssurance.Evaluate(Gate(GateAction.Block) with { Kind = ObligationKind.Encryption }, ActiveContract(), [InScope(Api)], [], Now).Status);
    }

    [Theory]
    [InlineData(ContractStatus.Draft, 0)]
    [InlineData(ContractStatus.Expired, 0)]
    [InlineData(ContractStatus.Active, 400)]
    public void Obligations_outside_an_active_term_are_not_in_effect(ContractStatus status, int daysAfterStart)
    {
        var contract = ActiveContract() with { Status = status, StartDate = DateOnly.FromDateTime(Now).AddDays(-daysAfterStart), EndDate = DateOnly.FromDateTime(Now).AddDays(-daysAfterStart + 365) };
        Assert.Equal(ObligationAssuranceStatus.NotInEffect,
            ObligationAssurance.Evaluate(Gate(GateAction.Block), contract, [InScope(Api)], [], Now).Status);
    }

    // ---- The service ----

    private readonly FakeContractRepository _contracts = new();
    private readonly FakeContractObligationRepository _obligations = new();
    private readonly FakeCodeRepositoryLinkRepository _links = new();
    private readonly FakeProgrammeRepository _programmes = new();
    private readonly RecordingContractAudit _audit = new();

    private readonly FakeSecurityAssuranceQueryService _security = new();

    private ContractAssuranceService Service() => new(_contracts, _obligations, _links, _programmes, _audit, _security, new FixedTime(Now));

    [Theory]
    [InlineData(ObligationKind.SecurityFindingGate, "4.2", "Block High", null, GateAction.Block, null, "severity threshold")]
    [InlineData(ObligationKind.SecurityFindingGate, "4.2", "Fix High", FindingSeverity.High, GateAction.RemediateWithin, null, "between 1 and 365")]
    [InlineData(ObligationKind.SecurityFindingGate, "4.2", "Fix High", FindingSeverity.High, GateAction.RemediateWithin, 400, "between 1 and 365")]
    [InlineData(ObligationKind.DataResidency, "", "UK only", null, null, null, "clause reference")]
    [InlineData(ObligationKind.DataResidency, "7.1", " ", null, null, null, "Describe")]
    public async Task Incomplete_obligations_are_refused_with_a_reason(
        ObligationKind kind, string clause, string description, FindingSeverity? severity, GateAction? action, int? days, string expected)
    {
        var contract = SeedContract();
        var outcome = await Service().AddObligationAsync(Tenant, contract, new ObligationInput
        {
            Kind = kind, ClauseReference = clause, Description = description, SeverityThreshold = severity, Action = action, RemediationDays = days
        }, null, null);

        Assert.False(outcome.Succeeded);
        Assert.Contains(expected, outcome.Error);
        Assert.Empty(_obligations.Obligations);
    }

    [Fact]
    public async Task A_recorded_obligation_drops_gate_fields_that_do_not_apply_and_is_audited()
    {
        var contract = SeedContract();
        var outcome = await Service().AddObligationAsync(Tenant, contract, new ObligationInput
        {
            Kind = ObligationKind.DataResidency, ClauseReference = " 7.1 ", Description = " UK regions only ",
            SeverityThreshold = FindingSeverity.High, Action = GateAction.Block, RemediationDays = 5
        }, null, 3);

        Assert.True(outcome.Succeeded);
        var stored = Assert.Single(_obligations.Obligations);
        Assert.Equal(("7.1", "UK regions only", null, null, null), (stored.ClauseReference, stored.Description, stored.SeverityThreshold, stored.Action, stored.RemediationDays));
        Assert.Equal((ContractAssuranceService.ObligationAuditEntity, "Recorded"), (_audit.Entries[0].EntityType, _audit.Entries[0].Action));
    }

    [Fact]
    public async Task Attestations_need_evidence_and_a_linked_repository()
    {
        var noEvidence = await Service().AttestAsync(Tenant, "GitHub", "acme", "acme/api", RepositoryControl.SecurityCheckRequired, ControlState.Enforced, " ", null, null);
        var unlinked = await Service().AttestAsync(Tenant, "GitHub", "acme", "acme/api", RepositoryControl.SecurityCheckRequired, ControlState.Enforced, "Screenshot in SharePoint", null, null);

        Assert.Contains("evidence", noEvidence.Error);
        Assert.Contains("not linked", unlinked.Error);
        Assert.Empty(_obligations.Attestations);
    }

    [Fact]
    public async Task A_shared_repository_appears_once_needing_attention_for_every_contract_it_serves()
    {
        var first = SeedContract("C-001", out var firstProject);
        var second = SeedContract("C-002", out var secondProject);
        _links.Rows.Add(Link(Api, firstProject));
        _links.Rows.Add(Link(Api, secondProject));
        foreach (var contract in new[] { first, second })
        {
            await Service().AddObligationAsync(Tenant, contract, new ObligationInput
            {
                Kind = ObligationKind.SecurityFindingGate, ClauseReference = "4.2", Description = "Block High",
                SeverityThreshold = FindingSeverity.High, Action = GateAction.Block
            }, null, null);
        }

        var portfolio = await Service().BuildPortfolioAsync(Tenant);

        var attention = Assert.Single(portfolio.RepositoriesNeedingAttention);
        Assert.Equal(["C-001", "C-002"], attention.Contracts);
        Assert.All(portfolio.Rows, r => Assert.Equal(ObligationAssuranceStatus.Unknown, r.Status));

        Assert.True((await Service().AttestAsync(Tenant, "GitHub", "acme", "acme/api", RepositoryControl.SecurityCheckRequired, ControlState.Enforced, "Branch rule screenshot, confirmed by the lead", null, null)).Succeeded);
        var after = await Service().BuildPortfolioAsync(Tenant);
        Assert.Empty(after.RepositoriesNeedingAttention);
        Assert.All(after.Rows, r => Assert.Equal(ObligationAssuranceStatus.AllAttestedEnforced, r.Status));
    }

    private Guid SeedContract() => SeedContract("C-001", out _);

    private Guid SeedContract(string reference, out Guid projectKey)
    {
        var customer = new Customer { CustomerKey = Guid.NewGuid(), TenantId = Tenant, Name = $"Customer {reference}", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var programme = new Programme { ProgrammeKey = Guid.NewGuid(), TenantId = Tenant, Name = $"Programme {reference}", CustomerKey = customer.CustomerKey, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        projectKey = Guid.NewGuid();
        _programmes.Customers.Add(customer);
        _programmes.Programmes.Add(programme);
        _programmes.Projects.Add(new Project { ProjectKey = projectKey, TenantId = Tenant, ProgrammeKey = programme.ProgrammeKey, Name = "Web", CreatedAtUtc = Now, UpdatedAtUtc = Now });
        var contract = ActiveContract() with { ContractKey = Guid.NewGuid(), CustomerKey = customer.CustomerKey, Reference = reference, TenantId = Tenant };
        _contracts.Contracts.Add(contract);
        return contract.ContractKey;
    }

    private static CodeRepositoryLink Link(CodeRepositoryRef repository, Guid project) => new()
    {
        LinkKey = Guid.NewGuid(), TenantId = Tenant, Repository = repository, ProjectKey = project, LinkedAtUtc = Now
    };

    private static Contract ActiveContract() => new()
    {
        ContractKey = Guid.NewGuid(), CustomerKey = Guid.NewGuid(), Reference = "C-001", CommercialModel = CommercialModel.TimeAndMaterials,
        Currency = "GBP", StartDate = DateOnly.FromDateTime(Now).AddDays(-30), EndDate = DateOnly.FromDateTime(Now).AddDays(335),
        Status = ContractStatus.Active, CreatedAtUtc = Now, UpdatedAtUtc = Now
    };

    private static ContractObligation Gate(GateAction action) => new()
    {
        ObligationKey = Guid.NewGuid(), TenantId = Tenant, ContractKey = Guid.NewGuid(), Kind = ObligationKind.SecurityFindingGate,
        ClauseReference = "4.2", Description = "Security gate", SeverityThreshold = FindingSeverity.High, Action = action,
        RemediationDays = action == GateAction.RemediateWithin ? 30 : null, RecordedAtUtc = Now
    };

    private static InScopeRepository InScope(CodeRepositoryRef repository) => new(repository, ["Programme › Web"]);

    private static RepositoryControlAttestation Attest(CodeRepositoryRef repository, RepositoryControl control, ControlState state, int attestedDaysAgo = 1) => new()
    {
        AttestationKey = Guid.NewGuid(), TenantId = Tenant, Repository = repository, Control = control, State = state,
        EvidenceReference = "evidence", AttestedAtUtc = Now.AddDays(-attestedDaysAgo),
        ExpiresAtUtc = Now.AddDays(-attestedDaysAgo + RepositoryControlAttestation.ValidityDays)
    };

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}

/// <summary>In-memory IContractObligationRepository with the same tenant, withdrawal and supersede rules as the SQL one.</summary>
public sealed class FakeContractObligationRepository : IContractObligationRepository
{
    public List<ContractObligation> Obligations { get; } = [];

    public List<RepositoryControlAttestation> Attestations { get; } = [];

    public Task<IReadOnlyList<ContractObligation>> GetLiveForContractAsync(Guid contractKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<ContractObligation>>(Obligations.Where(o => o.TenantId == tenantId && o.ContractKey == contractKey && o.IsLive).ToList());

    public Task<IReadOnlyList<ContractObligation>> GetLiveForTenantAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<ContractObligation>>(Obligations.Where(o => o.TenantId == tenantId && o.IsLive).ToList());

    public Task AddAsync(ContractObligation obligation)
    {
        Obligations.Add(obligation);
        return Task.CompletedTask;
    }

    public Task<bool> WithdrawAsync(Guid obligationKey, Guid tenantId, Guid? withdrawnByStaffKey, DateTime nowUtc)
    {
        var index = Obligations.FindIndex(o => o.ObligationKey == obligationKey && o.TenantId == tenantId && o.IsLive);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Obligations[index] = Obligations[index] with { WithdrawnAtUtc = nowUtc, WithdrawnByStaffKey = withdrawnByStaffKey };
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<RepositoryControlAttestation>> GetCurrentAttestationsAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<RepositoryControlAttestation>>(Attestations.Where(a => a.TenantId == tenantId && a.SupersededAtUtc is null).ToList());

    public Task RecordAttestationAsync(RepositoryControlAttestation attestation)
    {
        for (var i = 0; i < Attestations.Count; i++)
        {
            var existing = Attestations[i];
            if (existing.TenantId == attestation.TenantId && existing.Control == attestation.Control
                && existing.SupersededAtUtc is null && existing.Repository.SameRepositoryAs(attestation.Repository))
            {
                Attestations[i] = existing with { SupersededAtUtc = attestation.AttestedAtUtc };
            }
        }

        Attestations.Add(attestation);
        return Task.CompletedTask;
    }
}

/// <summary>Records Contract Ops audit entries so a test can assert what was audited.</summary>
public sealed class RecordingContractAudit : IContractAuditLogRepository
{
    public List<(string EntityType, string EntityId, string Action, string? DetailJson)> Entries { get; } = [];

    public Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
    {
        Entries.Add((entityType, entityId, action, detailJson));
        return Task.CompletedTask;
    }
}
