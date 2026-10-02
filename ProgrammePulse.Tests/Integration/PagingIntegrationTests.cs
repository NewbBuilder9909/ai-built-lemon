using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.SkillsEvidence;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Finding A7: list views loaded every row. Each paged SQL read here is
/// walked page by page at a small page size and the pages, stitched
/// together, must equal the full list in the same order: nothing repeated,
/// nothing skipped, the last page saying there is no next. Each SQL count
/// must equal the definition it replaced, and another tenant's rows never
/// appear.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PagingIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "paged SQL reads were not compared with the full lists they page.";
    private const int PageSize = 3;
    private static readonly DateTime Start = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private T Resolve<T>() where T : notnull => factory.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    [Fact]
    public async Task Open_alerts_page_in_order_and_count_without_reading()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Resolve<IProgrammeRepository>();
        var tenant = Guid.NewGuid();
        for (var i = 0; i < 8; i++)
        {
            await repository.RaiseAlertAsync(new Alert
            {
                AlertKey = Guid.NewGuid(), Type = AlertType.WorkstreamBlocked, EntityKey = Guid.NewGuid(),
                Message = $"Alert {i}", RaisedAtUtc = Start.AddMinutes(i)
            }, tenant);
        }

        await repository.RaiseAlertAsync(new Alert
        {
            AlertKey = Guid.NewGuid(), Type = AlertType.WorkstreamBlocked, EntityKey = Guid.NewGuid(),
            Message = "Another tenant", RaisedAtUtc = Start
        }, Guid.NewGuid());

        var full = await repository.GetOpenAlertsAsync(tenant);
        var paged = await WalkAsync(page => repository.GetOpenAlertsPageAsync(tenant, page));

        Assert.Equal(full.Select(a => a.AlertKey), paged.Select(a => a.AlertKey));
        Assert.Equal(8, await repository.CountOpenAlertsAsync(tenant));
    }

    [Fact]
    public async Task Identity_queue_pages_in_order_and_counts_without_reading()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Resolve<IIdentityResolutionRepository>();
        var tenant = Guid.NewGuid();
        for (var person = 0; person < 7; person++)
        {
            // Distinct occurrence counts, so the expected order is unambiguous.
            for (var sighting = 0; sighting <= person; sighting++)
            {
                await repository.RecordSightingAsync("PagingTest", $"user-{person}", null, $"Person {person}", "paging", Start.AddMinutes(sighting), tenant);
            }
        }

        await repository.RecordSightingAsync("PagingTest", "elsewhere", null, "Other tenant", "paging", Start, Guid.NewGuid());

        var full = await repository.GetUnresolvedAsync(tenant);
        var paged = await WalkAsync(page => repository.GetUnresolvedPageAsync(tenant, page));

        Assert.Equal(full.Select(u => u.UnresolvedIdentityKey), paged.Select(u => u.UnresolvedIdentityKey));
        Assert.Equal(7, await repository.CountUnresolvedAsync(tenant));
    }

    [Fact]
    public async Task Unmapped_actor_queue_pages_in_order_and_counts_people_and_bots_as_before()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Resolve<IEngineeringEvidenceRepository>();
        var tenant = Guid.NewGuid();
        await EvidenceSqlSetup.PermitAsync(factory, tenant);
        var connection = Guid.NewGuid();
        for (var i = 0; i < 8; i++)
        {
            await repository.RecordUnmappedSightingAsync(Actor(tenant, connection, $"actor-{i}", isBot: i % 3 == 0, lastSeen: Start.AddMinutes(i)));
        }

        var otherTenant = Guid.NewGuid();
        await EvidenceSqlSetup.PermitAsync(factory, otherTenant);
        await repository.RecordUnmappedSightingAsync(Actor(otherTenant, Guid.NewGuid(), "elsewhere", isBot: false, lastSeen: Start));

        var full = await repository.GetUnmappedActorsAsync(tenant, openOnly: true);
        var paged = await WalkAsync(page => repository.GetUnmappedActorsPageAsync(tenant, page));

        Assert.Equal(full.Select(a => a.UnmappedActorKey), paged.Select(a => a.UnmappedActorKey));
        Assert.Equal(full.Count(a => !a.IsBot), await repository.CountOpenUnmappedActorsAsync(tenant, includeBots: false));
        Assert.Equal(full.Count, await repository.CountOpenUnmappedActorsAsync(tenant, includeBots: true));
        Assert.Equal(5, await repository.CountOpenUnmappedActorsAsync(tenant, includeBots: false));
    }

    [Fact]
    public async Task A_persons_evidence_pages_in_order_and_the_summary_matches_the_whole_record()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Resolve<IEngineeringEvidenceRepository>();
        var tenant = Guid.NewGuid();
        var connection = Guid.NewGuid();
        var person = Guid.NewGuid();
        await SeedEvidenceAuthorizationAsync(repository, tenant, connection, person);
        EvidenceRole[] roles = [EvidenceRole.CommitAuthor, EvidenceRole.PullRequestAuthor, EvidenceRole.Reviewer];
        string[][] hints = [["C#"], ["C#", "SQL"], [], ["TypeScript"]];
        for (var i = 0; i < 10; i++)
        {
            await repository.UpsertEvidenceAsync(Evidence(tenant, connection, person, $"artefact-{i}", roles[i % roles.Length], hints[i % hints.Length], Start.AddHours(i)));
        }

        // Same person key under another tenant: must count nowhere here.
        var otherTenant = Guid.NewGuid();
        var otherConnection = Guid.NewGuid();
        await SeedEvidenceAuthorizationAsync(repository, otherTenant, otherConnection, person);
        await repository.UpsertEvidenceAsync(Evidence(otherTenant, otherConnection, person, "elsewhere", EvidenceRole.Reviewer, ["Go"], Start.AddDays(30)));

        var full = await repository.GetEvidenceForStaffAsync(person, tenant);
        var paged = await WalkAsync(page => repository.GetEvidencePageForStaffAsync(person, tenant, page));
        Assert.Equal(full.Select(e => e.ExternalId), paged.Select(e => e.ExternalId));

        var expected = EvidenceSummary.Of(full);
        var actual = await repository.GetEvidenceSummaryForStaffAsync(person, tenant);
        Assert.Equal(10, actual.Count);
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.CountsByRole.OrderBy(p => p.Key), actual.CountsByRole.OrderBy(p => p.Key));
        Assert.Equal(expected.LanguageHints, actual.LanguageHints);
        Assert.Equal(["C#", "SQL", "TypeScript"], actual.LanguageHints);
        Assert.Equal(expected.EarliestUtc, actual.EarliestUtc);
        Assert.Equal(expected.LatestUtc, actual.LatestUtc);

        var nobody = await repository.GetEvidenceSummaryForStaffAsync(Guid.NewGuid(), tenant);
        Assert.Equal(0, nobody.Count);
        Assert.Null(nobody.EarliestUtc);
        Assert.Null(nobody.LatestUtc);
        Assert.Empty(nobody.CountsByRole);
        Assert.Empty(nobody.LanguageHints);
    }

    private async Task SeedEvidenceAuthorizationAsync(IEngineeringEvidenceRepository repository, Guid tenant, Guid connection, Guid person)
    {
        await EvidenceSqlSetup.PermitAsync(factory, tenant);
        await repository.UpsertConnectionAsync(new()
        {
            ConnectionKey = connection, TenantId = tenant, Provider = "PagingTest", SourceAccountId = "account",
            DisplayName = "Paging fixture", ApiBaseUrl = "https://api.github.com", SelectedRepositories = ["repo"],
            Status = EvidenceConnectionStatus.Active, CreatedAtUtc = Start, UpdatedAtUtc = Start
        });
        await repository.CreateActorLinkAsync(new()
        {
            LinkKey = Guid.NewGuid(), TenantId = tenant, ConnectionKey = connection, Provider = "PagingTest",
            ExternalActorId = "actor", StaffKey = person, ApprovedAtUtc = Start
        });
    }

    // Reads every page in turn and checks the paging contract as it goes.
    private static async Task<List<T>> WalkAsync<T>(Func<PageRequest, Task<ResultPage<T>>> read)
    {
        var all = new List<T>();
        for (var number = 1; ; number++)
        {
            var page = await read(new PageRequest(number, PageSize));
            Assert.Equal(number, page.Number);
            Assert.True(page.Items.Count <= PageSize);
            all.AddRange(page.Items);
            if (!page.HasNext)
            {
                return all;
            }

            Assert.Equal(PageSize, page.Items.Count);
        }
    }

    private static UnmappedEvidenceActor Actor(Guid tenant, Guid connection, string id, bool isBot, DateTime lastSeen) => new()
    {
        UnmappedActorKey = Guid.NewGuid(), TenantId = tenant, ConnectionKey = connection, Provider = "PagingTest",
        ExternalActorId = id, IsBot = isBot, Reason = UnmappedActorReason.NoCandidate, OccurrenceCount = 1,
        FirstSeenUtc = lastSeen, LastSeenUtc = lastSeen
    };

    private static EngineeringEvidence Evidence(Guid tenant, Guid connection, Guid person, string id, EvidenceRole role, string[] hints, DateTime occurred) => new()
    {
        EvidenceKey = Guid.NewGuid(), TenantId = tenant, ConnectionKey = connection, Provider = "PagingTest",
        SourceAccountId = "account", SourceType = EvidenceSourceType.PullRequest, ExternalId = id, Role = role,
        ActorExternalId = "actor", ActorIsBot = false, StaffKey = person, AttributionStatus = EvidenceAttributionStatus.Mapped,
        RepositoryKey = "repo", OccurredAtUtc = occurred, LanguageHints = hints, SchemaVersion = 1,
        FirstIngestedAtUtc = occurred, UpdatedAtUtc = occurred
    };
}
