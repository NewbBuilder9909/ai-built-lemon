using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.SecurityAssurance;
using ProgrammePulse.Services.ContractOps;
using ProgrammePulse.Services.SecurityAssurance;
using static ProgrammePulse.Tests.SecurityAssurance.SecurityRecords;

namespace ProgrammePulse.Tests.SecurityAssurance;

/// <summary>
/// Step 3: what the scanning tool observes, joined to contract obligations.
/// The rules under test are the ones a contract owner relies on: an
/// observation beats an attestation; no gate is "not in place", never
/// unknown; a gate that only fails on Critical does not meet "block High";
/// ignored and snoozed findings still count; a repository the tool doesn't
/// scan falls back to its attestation.
/// </summary>
public sealed class ObservedAssuranceTests
{
    private static readonly Guid Tenant = Guid.Parse("c0a70000-0000-0000-0000-000000000003");
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly CodeRepositoryRef Api = new("GitHub", "acme", "acme/api");
    private static readonly CodeRepositoryRef Web = new("GitHub", "acme", "acme/web");

    // ---- The gate policy ----

    [Theory]
    [InlineData(true, SecuritySeverity.High, true, SecuritySeverity.High, true)]
    [InlineData(true, SecuritySeverity.Medium, true, SecuritySeverity.High, true)]
    [InlineData(true, SecuritySeverity.Critical, true, SecuritySeverity.High, false)]
    [InlineData(true, SecuritySeverity.Critical, true, SecuritySeverity.Critical, true)]
    [InlineData(true, SecuritySeverity.High, false, SecuritySeverity.High, false)]
    [InlineData(false, null, true, SecuritySeverity.High, false)]
    public void A_gate_blocks_only_when_configured_at_or_below_the_contracted_severity_on_every_kind(
        bool configured, SecuritySeverity? threshold, bool allKinds, SecuritySeverity contracted, bool expected)
    {
        Assert.Equal(expected, SecurityGatePolicy.BlocksAt(Observation(Tenant, Api, configured, threshold, allKinds), contracted));
    }

    [Fact]
    public void A_configured_gate_that_never_fails_does_not_block()
    {
        Assert.False(SecurityGatePolicy.BlocksAt(Observation(Tenant, Api, configured: true, threshold: null), SecuritySeverity.Critical));
    }

    // ---- Block before merge ----

    [Fact]
    public void An_observation_supersedes_an_attestation_for_the_same_repository()
    {
        var attestedEnforced = Attest(Api, RepositoryControl.SecurityCheckRequired, ControlState.Enforced);
        var result = ObligationAssurance.Evaluate(Gate(GateAction.Block), ActiveContract(), [InScope(Api)], [attestedEnforced], Now,
            Snapshot([Observation(Tenant, Api, configured: false)]));

        var repository = Assert.Single(result.Repositories);
        Assert.Equal((RepositoryAssuranceStatus.NotEnforced, AssuranceSource.Observation), (repository.Status, repository.Source));
        Assert.Null(repository.Attestation);
        Assert.Equal(ObligationAssuranceStatus.Gap, result.Status);
    }

    [Fact]
    public void A_repository_the_tool_does_not_scan_falls_back_to_its_attestation()
    {
        var result = ObligationAssurance.Evaluate(Gate(GateAction.Block), ActiveContract(), [InScope(Api), InScope(Web)],
            [Attest(Web, RepositoryControl.SecurityCheckRequired, ControlState.Enforced)], Now,
            Snapshot([Observation(Tenant, Api)]));

        Assert.Equal(
            [(RepositoryAssuranceStatus.Enforced, AssuranceSource.Observation), (RepositoryAssuranceStatus.Enforced, AssuranceSource.Attestation)],
            result.Repositories.Select(r => (r.Status, r.Source)));
        Assert.Equal(ObligationAssuranceStatus.AllAttestedEnforced, result.Status);
    }

    [Fact]
    public void A_disconnected_snapshot_is_ignored_entirely()
    {
        var stale = SecurityAssuranceSnapshot.None with { Observations = [Observation(Tenant, Api, configured: false)] };
        var result = ObligationAssurance.Evaluate(Gate(GateAction.Block), ActiveContract(), [InScope(Api)], [], Now, stale);

        Assert.Equal((RepositoryAssuranceStatus.Unknown, AssuranceSource.Attestation), (result.Repositories[0].Status, result.Repositories[0].Source));
    }

    [Fact]
    public void A_gate_failing_only_on_Critical_does_not_meet_block_High_but_meets_block_Critical()
    {
        var snapshot = Snapshot([Observation(Tenant, Api, threshold: SecuritySeverity.Critical)]);

        Assert.Equal(RepositoryAssuranceStatus.NotEnforced,
            ObligationAssurance.Evaluate(Gate(GateAction.Block), ActiveContract(), [InScope(Api)], [], Now, snapshot).Repositories[0].Status);
        Assert.Equal(RepositoryAssuranceStatus.Enforced,
            ObligationAssurance.Evaluate(Gate(GateAction.Block) with { SeverityThreshold = FindingSeverity.Critical }, ActiveContract(), [InScope(Api)], [], Now, snapshot).Repositories[0].Status);
    }

    [Fact]
    public void Bypassed_and_timed_out_checks_since_the_contract_started_are_carried_but_do_not_change_the_status()
    {
        var contract = ActiveContract();
        var start = contract.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var result = ObligationAssurance.Evaluate(Gate(GateAction.Block), contract, [InScope(Api)], [], Now,
            Snapshot([Observation(Tenant, Api)], runs:
            [
                Run(Tenant, Api, CheckRunOutcome.Bypassed, start.AddDays(2), "1"),
                Run(Tenant, Api, CheckRunOutcome.TimedOut, start.AddDays(3), "2"),
                Run(Tenant, Api, CheckRunOutcome.Failed, start.AddDays(4), "3"),
                Run(Tenant, Api, CheckRunOutcome.Bypassed, start.AddDays(-1), "4"),
                Run(Tenant, Web, CheckRunOutcome.Bypassed, start.AddDays(2), "5"),
            ]));

        var repository = Assert.Single(result.Repositories);
        Assert.Equal(RepositoryAssuranceStatus.Enforced, repository.Status);
        Assert.Equal(["2", "1"], repository.UnheldChecks!.Select(r => r.ExternalId));
    }

    // ---- Fix within a deadline ----

    [Fact]
    public void Ignored_and_snoozed_findings_past_the_deadline_are_a_gap()
    {
        var old = Now.AddDays(-45);
        var result = ObligationAssurance.Evaluate(Gate(GateAction.RemediateWithin), ActiveContract(), [InScope(Api)], [], Now,
            Snapshot([Observation(Tenant, Api)],
            [
                Finding(Tenant, Api, SecuritySeverity.High, SecurityFindingStatus.Ignored, old, "1"),
                Finding(Tenant, Api, SecuritySeverity.Critical, SecurityFindingStatus.Snoozed, old, "2"),
                Finding(Tenant, Api, SecuritySeverity.High, SecurityFindingStatus.Open, Now.AddDays(-5), "3"),
            ]));

        var repository = Assert.Single(result.Repositories);
        Assert.Equal((RepositoryAssuranceStatus.FindingsOverdue, 2, 3), (repository.Status, repository.OverdueFindings, repository.OutstandingFindings!.Count));
        Assert.Equal(ObligationAssuranceStatus.Gap, result.Status);
    }

    [Fact]
    public void Findings_within_the_deadline_or_below_the_threshold_or_closed_are_not_overdue()
    {
        var old = Now.AddDays(-45);
        var result = ObligationAssurance.Evaluate(Gate(GateAction.RemediateWithin) with { SeverityThreshold = FindingSeverity.Critical },
            ActiveContract(), [InScope(Api)], [], Now,
            Snapshot([Observation(Tenant, Api)],
            [
                Finding(Tenant, Api, SecuritySeverity.High, SecurityFindingStatus.Open, old, "1"),
                Finding(Tenant, Api, SecuritySeverity.Critical, SecurityFindingStatus.Closed, old, "2"),
                Finding(Tenant, Api, SecuritySeverity.Critical, SecurityFindingStatus.Open, Now.AddDays(-29), "3"),
            ]));

        var repository = Assert.Single(result.Repositories);
        Assert.Equal((RepositoryAssuranceStatus.Enforced, 0, 1), (repository.Status, repository.OverdueFindings, repository.OutstandingFindings!.Count));
    }

    [Fact]
    public void A_repository_never_scanned_does_not_meet_scanning_enabled()
    {
        var neverScanned = Observation(Tenant, Api) with { LastScannedAtUtc = null };
        var result = ObligationAssurance.Evaluate(Gate(GateAction.RemediateWithin), ActiveContract(), [InScope(Api)], [], Now,
            Snapshot([neverScanned]));

        Assert.Equal(RepositoryAssuranceStatus.NotEnforced, Assert.Single(result.Repositories).Status);
    }

    // ---- The snapshot query ----

    [Fact]
    public async Task The_snapshot_is_empty_until_a_clean_sync_and_leaves_out_findings_of_repositories_no_longer_listed()
    {
        var repository = new FakeSecurityAssuranceRepository();
        var gate = new AllowAllFeatures();
        var query = new SecurityAssuranceQueryService(repository, gate);
        repository.Connections.Add(new SecurityToolConnection
        {
            ConnectionKey = Guid.NewGuid(), TenantId = Tenant, Tool = SecurityTools.Aikido, Region = "eu", ClientId = "id",
            Status = SecurityConnectionStatus.Active, CreatedAtUtc = Now, UpdatedAtUtc = Now,
        });
        repository.Observations.Add(Observation(Tenant, Api));
        repository.Findings.Add(Finding(Tenant, Api, SecuritySeverity.High, SecurityFindingStatus.Open, Now, "1"));
        repository.Findings.Add(Finding(Tenant, Web, SecuritySeverity.High, SecurityFindingStatus.Open, Now, "2"));

        Assert.False((await query.GetSnapshotAsync(Tenant, Now.AddDays(-90))).Connected);

        await repository.RecordSyncFinishedAsync(Tenant, SecurityTools.Aikido, null, Now);
        var snapshot = await query.GetSnapshotAsync(Tenant, Now.AddDays(-90));

        Assert.True(snapshot.Connected);
        Assert.Equal(["1"], snapshot.OutstandingFindings.Select(f => f.ExternalId));
        Assert.False(snapshot.CheckRunsVerified);

        gate.Enabled = false;
        Assert.False((await query.GetSnapshotAsync(Tenant, Now.AddDays(-90))).Connected);
    }

    private sealed class AllowAllFeatures : ProgrammePulse.Services.Tenancy.IFeatureGate
    {
        public bool Enabled { get; set; } = true;

        public Task<bool> IsEnabledAsync(string feature) => Task.FromResult(Enabled);

        public Task<ProgrammePulse.Services.Tenancy.FeatureGateDecision> EvaluateAsync(string feature) =>
            Task.FromResult(new ProgrammePulse.Services.Tenancy.FeatureGateDecision(
                Enabled, Enabled ? ProgrammePulse.Services.Tenancy.FeatureGateOutcome.Enabled : ProgrammePulse.Services.Tenancy.FeatureGateOutcome.NotInPlan, "Enterprise"));
    }

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

    private static RepositoryControlAttestation Attest(CodeRepositoryRef repository, RepositoryControl control, ControlState state) => new()
    {
        AttestationKey = Guid.NewGuid(), TenantId = Tenant, Repository = repository, Control = control, State = state,
        EvidenceReference = "evidence", AttestedAtUtc = Now.AddDays(-1), ExpiresAtUtc = Now.AddDays(89)
    };
}
