using Microsoft.Extensions.DependencyInjection;
using NPoco;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ContractOps;
using ProgrammePulse.Services.Shared;
using Umbraco.Cms.Infrastructure.Scoping;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// The obligations register and control attestations against real SQL
/// Server: tenant isolation both ways, a contract from another tenant refused
/// before writing, withdrawal kept on record, and exactly one current
/// attestation per repository and control with every superseded one kept.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ContractObligationIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the contract obligations register was not exercised against real SQL Server.";
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    private T Resolve<T>() where T : notnull => factory.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    [Fact]
    public async Task Obligations_are_tenant_scoped_refuse_foreign_contracts_and_are_kept_when_withdrawn()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Resolve<IContractObligationRepository>();
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var contract = await SeedContractAsync(tenant);
        var obligation = Obligation(tenant, contract);

        await repository.AddAsync(obligation);
        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => repository.AddAsync(Obligation(otherTenant, contract)));

        var stored = Assert.Single(await repository.GetLiveForContractAsync(contract, tenant));
        Assert.Equal((FindingSeverity.High, GateAction.RemediateWithin, 30), (stored.SeverityThreshold!.Value, stored.Action!.Value, stored.RemediationDays!.Value));
        Assert.Empty(await repository.GetLiveForTenantAsync(otherTenant));
        Assert.False(await repository.WithdrawAsync(obligation.ObligationKey, otherTenant, null, Now));

        Assert.True(await repository.WithdrawAsync(obligation.ObligationKey, tenant, Guid.NewGuid(), Now));
        Assert.Empty(await repository.GetLiveForContractAsync(contract, tenant));
        Assert.Equal(1, await CountAsync("ContractOps_Obligation", tenant));
    }

    [Fact]
    public async Task One_current_attestation_per_repository_and_control_with_history_kept()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Resolve<IContractObligationRepository>();
        var tenant = Guid.NewGuid();
        var repo = new CodeRepositoryRef("GitHub", "acme", $"acme/{Guid.NewGuid():N}");

        await repository.RecordAttestationAsync(Attestation(tenant, repo, RepositoryControl.SecurityCheckRequired, ControlState.NotEnforced, Now.AddDays(-5)));
        await repository.RecordAttestationAsync(Attestation(tenant, repo, RepositoryControl.SecurityCheckRequired, ControlState.Enforced, Now));
        await repository.RecordAttestationAsync(Attestation(tenant, repo, RepositoryControl.SecurityScanningEnabled, ControlState.Enforced, Now));

        var current = await repository.GetCurrentAttestationsAsync(tenant);
        Assert.Equal(2, current.Count);
        Assert.Equal(ControlState.Enforced, Assert.Single(current, a => a.Control == RepositoryControl.SecurityCheckRequired).State);
        Assert.Empty(await repository.GetCurrentAttestationsAsync(Guid.NewGuid()));
        Assert.Equal(3, await CountAsync("ContractOps_RepositoryControlAttestation", tenant));

        using var scope = factory.Services.GetRequiredService<IScopeProvider>().CreateScope(autoComplete: true);
        var filter = await scope.Database.ExecuteScalarAsync<string>(
            "SELECT filter_definition FROM sys.indexes WHERE name = @0 AND is_unique = 1",
            ProgrammePulse.Migrations.ContractOps.AddObligationTables.CurrentAttestationIndexName);
        Assert.Equal("([supersededAtUtc] IS NULL)", filter);
    }

    private async Task<int> CountAsync(string table, Guid tenant)
    {
        using var scope = factory.Services.GetRequiredService<IScopeProvider>().CreateScope(autoComplete: true);
        return await scope.Database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE tenantId = @0", tenant);
    }

    private async Task<Guid> SeedContractAsync(Guid tenant)
    {
        var customer = await Resolve<ProgrammePulse.Services.ProgrammeOps.IProgrammeRepository>().UpsertCustomerAsync(new Customer
        {
            CustomerKey = Guid.NewGuid(), Name = "Obligations customer", CreatedAtUtc = Now, UpdatedAtUtc = Now
        }, tenant);
        var contract = await Resolve<IContractRepository>().CreateContractAsync(new Contract
        {
            ContractKey = Guid.NewGuid(), CustomerKey = customer.CustomerKey, Reference = $"OB-{Guid.NewGuid():N}"[..20],
            CommercialModel = CommercialModel.TimeAndMaterials, Currency = "GBP",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = ContractStatus.Active,
            CreatedAtUtc = Now, UpdatedAtUtc = Now
        }, tenant);
        return contract.ContractKey;
    }

    private static ContractObligation Obligation(Guid tenant, Guid contract) => new()
    {
        ObligationKey = Guid.NewGuid(), TenantId = tenant, ContractKey = contract, Kind = ObligationKind.SecurityFindingGate,
        ClauseReference = "Schedule 3, 4.2", Description = "Fix High and Critical findings within 30 days",
        SeverityThreshold = FindingSeverity.High, Action = GateAction.RemediateWithin, RemediationDays = 30, RecordedAtUtc = Now
    };

    private static RepositoryControlAttestation Attestation(Guid tenant, CodeRepositoryRef repo, RepositoryControl control, ControlState state, DateTime at) => new()
    {
        AttestationKey = Guid.NewGuid(), TenantId = tenant, Repository = repo, Control = control, State = state,
        EvidenceReference = "Branch rule screenshot", AttestedAtUtc = at, ExpiresAtUtc = at.AddDays(RepositoryControlAttestation.ValidityDays)
    };
}
