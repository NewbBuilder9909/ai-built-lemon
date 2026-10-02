using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// An email match is only a suggestion on the identity queue. Approving it is
/// one click, but only while it still holds: the suggested profile is active,
/// in this tenant, and still has the email the source tool reported.
/// </summary>
public class IdentityQueueServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeSourceConnections : ISourceConnectionRepository
    {
        private readonly List<SourceConnection> connections = [];

        public Task<SourceConnection?> GetActiveForTenantAsync(Guid tenantId, string source) =>
            Task.FromResult(connections.FirstOrDefault(c => c.TenantId == tenantId && c.Source == source));

        public Task<SourceConnection> GetOrCreateActiveAsync(Guid tenantId, string source, string? externalAccountId, DateTime nowUtc)
        {
            var connection = connections.FirstOrDefault(c => c.TenantId == tenantId && c.Source == source);
            if (connection is null)
            {
                connection = new SourceConnection
                {
                    ConnectionKey = Guid.NewGuid(), TenantId = tenantId, Source = source, DisplayName = source,
                    IsActive = true, CreatedAtUtc = nowUtc
                };
                connections.Add(connection);
            }

            return Task.FromResult(connection);
        }

        public Task<SourceConnection> SetCredentialAsync(Guid tenantId, string source, string? protectedCredentialJson, DateTime nowUtc) =>
            throw new NotSupportedException();
    }

    private sealed record Setup(IdentityQueueService Service, FakeIdentityResolutionRepository Identity, FakeStaffRepository Staff, FakeAuditLogRepository Audit, StaffProfile Jamie, UnresolvedIdentity Row);

    private static async Task<Setup> ArrangeAsync(Func<StaffProfile, StaffProfile>? editJamie = null)
    {
        var staff = new FakeStaffRepository();
        var jamie = new StaffProfile
        {
            StaffKey = Guid.NewGuid(), MemberId = 1, FullName = "Jamie Rees", Email = "jamie@example.com", IsActive = true,
            TenantId = Tenant, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime
        };
        staff.Staff.Add(jamie);

        var identity = new FakeIdentityResolutionRepository();
        await new StaffIdentityResolver(identity, staff, new FixedTimeProvider(Now))
            .ResolveAsync("ClickUp", "42", "Jamie@Example.com", "jamie", "task assignee", Tenant);
        var row = Assert.Single(identity.Unresolved);

        if (editJamie is not null)
        {
            staff.Staff.Remove(jamie);
            jamie = editJamie(jamie);
            staff.Staff.Add(jamie);
        }

        var audit = new FakeAuditLogRepository();
        var service = new IdentityQueueService(identity, staff, audit, new FakeSourceConnections(), new FixedTimeProvider(Now));
        return new Setup(service, identity, staff, audit, jamie, row);
    }

    [Fact]
    public async Task The_queue_shows_the_suggested_person_for_an_email_match()
    {
        var setup = await ArrangeAsync();

        var page = await setup.Service.BuildAsync(Tenant, null, new PageRequest(1));

        var row = Assert.Single(page.Unresolved);
        Assert.Equal(setup.Jamie.StaffKey, row.SuggestedStaffKey);
        Assert.Equal("Jamie Rees", row.SuggestedStaffName);
    }

    [Fact]
    public async Task Approving_a_suggestion_creates_the_link_resolves_the_row_and_audits_how()
    {
        var setup = await ArrangeAsync();

        var message = await setup.Service.ApproveSuggestionAsync(Tenant, setup.Row.UnresolvedIdentityKey, actorMemberId: 7);

        Assert.StartsWith("Linked", message);
        var link = Assert.Single(setup.Identity.Links);
        Assert.Equal(setup.Jamie.StaffKey, link.StaffKey);
        Assert.Equal("42", link.ExternalUserId);
        Assert.True(Assert.Single(setup.Identity.Unresolved).IsResolved);
        var entry = Assert.Single(setup.Audit.Entries);
        Assert.Equal("IdentityLinked", entry.Action);
        Assert.Equal(7, entry.ActorMemberId);
        Assert.Contains("ApprovedEmailMatch", entry.DetailJson);

        // The next sync resolves through the approved link.
        var resolver = new StaffIdentityResolver(setup.Identity, setup.Staff, new FixedTimeProvider(Now));
        Assert.Equal(setup.Jamie.StaffKey, await resolver.ResolveAsync("ClickUp", "42", "jamie@example.com", "jamie", "task assignee", Tenant));
    }

    public static TheoryData<string> StaleSuggestions => new() { "email changed", "left", "other tenant" };

    [Theory]
    [MemberData(nameof(StaleSuggestions))]
    public async Task A_suggestion_that_no_longer_holds_is_refused_and_hidden(string change)
    {
        var setup = await ArrangeAsync(jamie => change switch
        {
            "email changed" => jamie with { Email = "jamie.rees@example.com" },
            "left" => jamie with { IsActive = false },
            _ => jamie with { TenantId = Guid.NewGuid() },
        });

        var message = await setup.Service.ApproveSuggestionAsync(Tenant, setup.Row.UnresolvedIdentityKey, actorMemberId: 7);

        Assert.Contains("no longer holds", message);
        Assert.Empty(setup.Identity.Links);
        Assert.Empty(setup.Audit.Entries);
        Assert.Null(Assert.Single((await setup.Service.BuildAsync(Tenant, null, new PageRequest(1))).Unresolved).SuggestedStaffName);
    }

    [Fact]
    public async Task A_row_with_no_suggestion_cannot_be_approved()
    {
        var identity = new FakeIdentityResolutionRepository();
        var row = await identity.RecordSightingAsync("ClickUp", "9", "nobody@example.com", "nobody", "task assignee", Now.UtcDateTime, Tenant);
        var service = new IdentityQueueService(identity, new FakeStaffRepository(), new FakeAuditLogRepository(), new FakeSourceConnections(), new FixedTimeProvider(Now));

        Assert.Contains("no longer holds", await service.ApproveSuggestionAsync(Tenant, row.UnresolvedIdentityKey, actorMemberId: 7));
        Assert.Empty(identity.Links);
    }
}
