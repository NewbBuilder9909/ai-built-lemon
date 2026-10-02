using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Real SQL Server. An email match is stored as a suggestion on the queue row
/// (ProgrammeOps step 27) rather than attributing the person, and the latest
/// sighting decides it: a later sighting with no match clears it.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class IdentitySuggestionIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the identity queue's suggested-match column was not exercised against SQL Server.";
    private static readonly DateTime Start = new(2026, 9, 27, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_suggestion_is_stored_read_back_and_replaced_by_the_latest_sighting()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IIdentityResolutionRepository>();
        var tenant = Guid.NewGuid();
        var suggested = Guid.NewGuid();

        var first = await repository.RecordSightingAsync("ClickUp", "42", "jamie@example.com", "jamie", "task assignee", Start, tenant, suggested);
        Assert.Equal(suggested, first.SuggestedStaffKey);
        Assert.Equal(suggested, (await repository.GetUnresolvedByKeyAsync(first.UnresolvedIdentityKey, tenant))!.SuggestedStaffKey);
        Assert.Equal(suggested, Assert.Single((await repository.GetUnresolvedPageAsync(tenant, new PageRequest(1))).Items).SuggestedStaffKey);

        // The profile's email changed, so the next sync finds no match.
        var second = await repository.RecordSightingAsync("ClickUp", "42", "jamie@example.com", "jamie", "task assignee", Start.AddHours(1), tenant);
        Assert.Equal(first.UnresolvedIdentityKey, second.UnresolvedIdentityKey);
        Assert.Null((await repository.GetUnresolvedByKeyAsync(first.UnresolvedIdentityKey, tenant))!.SuggestedStaffKey);
    }
}
