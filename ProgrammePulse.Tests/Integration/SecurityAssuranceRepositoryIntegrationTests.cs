using Microsoft.Extensions.DependencyInjection;
using NPoco;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.SecurityAssurance;
using ProgrammePulse.Services.SecurityAssurance;
using Umbraco.Cms.Infrastructure.Scoping;
using Xunit.Abstractions;
using static ProgrammePulse.Tests.SecurityAssurance.SecurityRecords;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// The security assurance tables against real SQL Server: the migration ran;
/// replaying a sync is idempotent (the identity indexes); a status change in
/// the tool updates the row rather than adding one; observations are replaced
/// so a repository the tool drops stops counting; and nothing crosses tenants.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SecurityAssuranceRepositoryIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the security assurance repository was not exercised against real SQL Server.";
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    private ISecurityAssuranceRepository Repository() =>
        factory.Services.CreateScope().ServiceProvider.GetRequiredService<ISecurityAssuranceRepository>();

    [Fact]
    public async Task Replaying_a_sync_is_idempotent_and_a_status_change_updates_in_place()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Repository();
        var tenant = Guid.NewGuid();
        var api = Repo();
        var findings = new[]
        {
            Finding(tenant, api, SecuritySeverity.High, SecurityFindingStatus.Open, Now.AddDays(-40), "1"),
            Finding(tenant, api, SecuritySeverity.Critical, SecurityFindingStatus.Ignored, Now.AddDays(-10), "2"),
            Finding(tenant, api, SecuritySeverity.Medium, SecurityFindingStatus.Open, Now.AddDays(-10), "3"),
        };

        await repository.UpsertFindingsAsync(tenant, SecurityTools.Aikido, findings);
        await repository.UpsertFindingsAsync(tenant, SecurityTools.Aikido, findings);
        Assert.Equal(3, await CountAsync("SecurityAssurance_Finding", tenant));

        var outstanding = await repository.GetOutstandingFindingsAsync(tenant, SecuritySeverity.High);
        Assert.Equal(["1", "2"], outstanding.Select(f => f.ExternalId).Order());
        var ignored = Assert.Single(outstanding, f => f.ExternalId == "2");
        Assert.Equal((SecurityFindingStatus.Ignored, api.RepositoryKey, "CVE-2026-0001"), (ignored.Status, ignored.Repository.RepositoryKey, ignored.CveId));

        await repository.UpsertFindingsAsync(tenant, SecurityTools.Aikido,
            [findings[0] with { Status = SecurityFindingStatus.Closed, ClosedAtUtc = Now }]);
        Assert.Equal(3, await CountAsync("SecurityAssurance_Finding", tenant));
        Assert.Equal(["2"], (await repository.GetOutstandingFindingsAsync(tenant, SecuritySeverity.High)).Select(f => f.ExternalId));

        var runs = new[] { Run(tenant, api, CheckRunOutcome.Bypassed, Now.AddDays(-1), "10"), Run(tenant, api, CheckRunOutcome.Passed, Now.AddDays(-100), "11") };
        await repository.UpsertCheckRunsAsync(tenant, SecurityTools.Aikido, runs);
        await repository.UpsertCheckRunsAsync(tenant, SecurityTools.Aikido, runs);
        Assert.Equal(2, await CountAsync("SecurityAssurance_CheckRun", tenant));
        Assert.Equal(["10"], (await repository.GetCheckRunsSinceAsync(tenant, Now.AddDays(-90))).Select(r => r.ExternalId));
    }

    [Fact]
    public async Task Observations_are_replaced_so_a_dropped_repository_stops_counting()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Repository();
        var tenant = Guid.NewGuid();
        var api = Repo();
        var web = Repo();

        await repository.ReplaceObservationsAsync(tenant, SecurityTools.Aikido,
            [Observation(tenant, api, configured: false, toolRepositoryId: "1"), Observation(tenant, web, toolRepositoryId: "2")]);
        await repository.ReplaceObservationsAsync(tenant, SecurityTools.Aikido,
            [Observation(tenant, api, threshold: SecuritySeverity.Critical, toolRepositoryId: "1")]);

        var observation = Assert.Single(await repository.GetObservationsAsync(tenant));
        Assert.Equal((api.RepositoryKey, true, SecuritySeverity.Critical),
            (observation.Repository.RepositoryKey, observation.GateConfigured, observation.GateMinimumSeverity!.Value));
        Assert.Equal(1, await CountAsync("SecurityAssurance_RepositoryObservation", tenant));
    }

    [Fact]
    public async Task Nothing_crosses_tenants_and_the_connection_lifecycle_round_trips()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Repository();
        var tenant = Guid.NewGuid();
        var other = Guid.NewGuid();
        var api = Repo();

        await repository.ReplaceObservationsAsync(tenant, SecurityTools.Aikido, [Observation(tenant, api, toolRepositoryId: "1")]);
        await repository.UpsertFindingsAsync(tenant, SecurityTools.Aikido, [Finding(tenant, api, SecuritySeverity.High, SecurityFindingStatus.Open, Now, "1")]);

        // The same tool ids in another tenant are other rows, and replacing that
        // tenant's observations leaves the first tenant's alone.
        await repository.UpsertFindingsAsync(other, SecurityTools.Aikido, [Finding(other, api, SecuritySeverity.High, SecurityFindingStatus.Closed, Now, "1")]);
        await repository.ReplaceObservationsAsync(other, SecurityTools.Aikido, []);

        Assert.Single(await repository.GetObservationsAsync(tenant));
        Assert.Single(await repository.GetOutstandingFindingsAsync(tenant, SecuritySeverity.High));
        Assert.Empty(await repository.GetOutstandingFindingsAsync(other, SecuritySeverity.High));
        Assert.Null(await repository.GetConnectionAsync(other, SecurityTools.Aikido));

        await repository.UpsertConnectionAsync(new SecurityToolConnection
        {
            ConnectionKey = Guid.NewGuid(), TenantId = tenant, Tool = SecurityTools.Aikido, Region = "eu", ClientId = "client",
            ProtectedCredentialJson = "cipher", Status = SecurityConnectionStatus.Active, CreatedAtUtc = Now, UpdatedAtUtc = Now,
        });
        await repository.RecordSyncStartedAsync(tenant, SecurityTools.Aikido, Now);
        await repository.RecordSyncFinishedAsync(tenant, SecurityTools.Aikido, null, Now.AddMinutes(1));
        await repository.RecordSyncFinishedAsync(tenant, SecurityTools.Aikido, "rate limited", Now.AddMinutes(5));
        await repository.SetConnectionStatusAsync(tenant, SecurityTools.Aikido, SecurityConnectionStatus.Disconnected, clearCredential: true, Now.AddMinutes(6));

        var connection = await repository.GetConnectionAsync(tenant, SecurityTools.Aikido);
        Assert.NotNull(connection);
        Assert.Equal((SecurityConnectionStatus.Disconnected, (string?)null, Now.AddMinutes(1), "rate limited"),
            (connection!.Status, connection.ProtectedCredentialJson, connection.LastSyncSucceededAtUtc, connection.LastSyncError));
    }

    private static CodeRepositoryRef Repo() => new("GitHub", "acme", $"acme/{Guid.NewGuid():N}");

    private async Task<int> CountAsync(string table, Guid tenant)
    {
        using var scope = factory.Services.GetRequiredService<IScopeProvider>().CreateScope(autoComplete: true);
        return await scope.Database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE tenantId = @0", tenant);
    }
}
