using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Finding B3: detection runs after every sync and on demand, so two runs can
/// overlap. Before AddOpenAlertUniqueness each checked for an open alert and
/// then inserted, and both inserted. These race real inserts against real
/// SQL Server; an in-memory fake cannot reproduce the interleaving.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class OpenAlertUniquenessIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "concurrent alert raising was not exercised against real SQL Server.";

    [Fact]
    public async Task Concurrent_raises_for_one_entity_leave_exactly_one_open_alert()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var tenant = Guid.NewGuid();
        var entity = Guid.NewGuid();

        var raised = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            Repository().RaiseAlertAsync(NewAlert(entity), tenant))));

        var open = Assert.Single(await Repository().GetOpenAlertsAsync(tenant));
        Assert.All(raised, alert => Assert.Equal(open.AlertKey, alert.AlertKey));
    }

    [Fact]
    public async Task An_acknowledged_alert_does_not_block_a_new_one_and_other_tenants_are_independent()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Repository();
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var entity = Guid.NewGuid();

        var first = await repository.RaiseAlertAsync(NewAlert(entity), tenant);
        var elsewhere = await repository.RaiseAlertAsync(NewAlert(entity), otherTenant);
        Assert.NotEqual(first.AlertKey, elsewhere.AlertKey);

        await repository.AcknowledgeAlertAsync(first.AlertKey, null, DateTime.UtcNow, tenant);
        var second = await repository.RaiseAlertAsync(NewAlert(entity), tenant);

        Assert.NotEqual(first.AlertKey, second.AlertKey);
        Assert.Equal(second.AlertKey, Assert.Single(await repository.GetOpenAlertsAsync(tenant)).AlertKey);
    }

    private IProgrammeRepository Repository() =>
        factory.Services.CreateScope().ServiceProvider.GetRequiredService<IProgrammeRepository>();

    private static Alert NewAlert(Guid entity) => new()
    {
        AlertKey = Guid.NewGuid(),
        Type = AlertType.WorkstreamBlocked,
        EntityKey = entity,
        Message = "Integration test alert",
        RaisedAtUtc = DateTime.UtcNow
    };
}
