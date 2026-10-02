using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Services.ServiceOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Real LocalDB, real ServiceOpsRepository, real composite unique
/// indexes.
///
/// The claim these prove is the one the in-memory fake can only assert
/// about itself: that UX_ServiceOps_SupportCaseFact_identity really is
/// unique on (tenantId, connectionKey, externalTicketId). That is what
/// makes the deliberate <c>updated_since</c> overlap safe — without it
/// every run would duplicate the cases in the overlap window and inflate
/// every demand figure in the product.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ServiceOpsRepositoryIntegrationTests(ProgrammePulseWebApplicationFactory factory)
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Sept1 = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private IServiceOpsRepository Repository() =>
        factory.Services.CreateScope().ServiceProvider.GetRequiredService<IServiceOpsRepository>();

    private static string NewAccount() => "acct" + Guid.NewGuid().ToString("N")[..12];

    private async Task<DeskConnection> ConnectAsync(IServiceOpsRepository repository, Guid tenantId, string account)
    {
        var now = DateTime.UtcNow;
        return await repository.UpsertConnectionAsync(new DeskConnection
        {
            ConnectionKey = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = DeskHostPolicy.FreshdeskProvider,
            SourceAccountId = account,
            DisplayName = $"{account}.freshdesk.com",
            ApiBaseUrl = DeskHostPolicy.CanonicalizeFreshdesk(account)!,
            ApprovedComponents = ["billing", "sync"],
            Status = DeskConnectionStatus.Active,
            ProtectedCredentialJson = "ciphertext",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
    }

    private static SupportCaseFact Case(
        DeskConnection connection, string ticketId, DateTime? resolvedAtUtc = null,
        bool withdrawn = false, string? component = "billing") => new()
        {
            CaseKey = Guid.NewGuid(),
            TenantId = connection.TenantId,
            ConnectionKey = connection.ConnectionKey,
            Provider = DeskHostPolicy.FreshdeskProvider,
            SourceAccountId = connection.SourceAccountId,
            ExternalTicketId = ticketId,
            CreatedAtUtc = Sept1,
            UpdatedAtUtc = Sept1,
            ResolvedAtUtc = resolvedAtUtc,
            State = withdrawn ? SupportCaseState.Withdrawn
                : resolvedAtUtc is null ? SupportCaseState.Active : SupportCaseState.Resolved,
            ProviderStatus = "2",
            Priority = SupportCasePriority.High,
            ComponentKey = component,
            RawComponentTag = component,
            SourceUrl = "https://acme.freshdesk.com/a/tickets/1",
            IsReopened = false,
            IsWithdrawn = withdrawn,
            SchemaVersion = SupportCaseSchema.CurrentVersion,
            FirstIngestedAtUtc = DateTime.UtcNow,
            IngestedAtUtc = DateTime.UtcNow
        };

    [Fact]
    public async Task Replaying_the_same_ticket_updates_one_row_and_keeps_its_first_seen_stamp()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Service evidence SQL persistence, isolation and review constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var ticketId = Guid.NewGuid().ToString("N");

        var first = await repository.UpsertCaseAsync(Case(connection, ticketId));
        var second = await repository.UpsertCaseAsync(Case(connection, ticketId, resolvedAtUtc: Sept1.AddHours(3)));

        var stored = await repository.GetCaseAsync(connection.ConnectionKey, ticketId, TenantA);
        Assert.NotNull(stored);
        Assert.Equal(SupportCaseState.Resolved, stored.State);
        Assert.Equal(first.CaseKey, second.CaseKey);

        // Same ~3.33ms datetime rounding as the evidence tables; see
        // EngineeringEvidenceRepositoryIntegrationTests.
        Assert.True((second.FirstIngestedAtUtc - first.FirstIngestedAtUtc).Duration() < TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public async Task Two_tenants_may_hold_the_same_ticket_id_without_colliding()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Service evidence SQL persistence, isolation and review constraints were not verified.")) return;

        var repository = Repository();
        var ticketId = Guid.NewGuid().ToString("N");
        var a = await ConnectAsync(repository, TenantA, NewAccount());
        var b = await ConnectAsync(repository, TenantB, NewAccount());

        await repository.UpsertCaseAsync(Case(a, ticketId));
        await repository.UpsertCaseAsync(Case(b, ticketId));

        Assert.NotNull(await repository.GetCaseAsync(a.ConnectionKey, ticketId, TenantA));
        Assert.NotNull(await repository.GetCaseAsync(b.ConnectionKey, ticketId, TenantB));
        Assert.Null(await repository.GetCaseAsync(a.ConnectionKey, ticketId, TenantB));
    }

    [Fact]
    public async Task One_tenant_may_connect_two_desks_and_reconnecting_updates_in_place()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Service evidence SQL persistence, isolation and review constraints were not verified.")) return;

        var repository = Repository();
        var account = NewAccount();
        var first = await ConnectAsync(repository, TenantA, account);
        var again = await ConnectAsync(repository, TenantA, account);
        var second = await ConnectAsync(repository, TenantA, NewAccount());

        Assert.Equal(first.ConnectionKey, again.ConnectionKey);
        Assert.NotEqual(first.ConnectionKey, second.ConnectionKey);
    }

    [Fact]
    public async Task A_link_against_a_case_that_is_not_this_tenants_is_refused()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Service evidence SQL persistence, isolation and review constraints were not verified.")) return;

        var repository = Repository();
        var theirs = await ConnectAsync(repository, TenantB, NewAccount());
        var ticketId = Guid.NewGuid().ToString("N");
        await repository.UpsertCaseAsync(Case(theirs, ticketId));

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => repository.UpsertLinkAsync(new SupportCodeLink
        {
            LinkKey = Guid.NewGuid(),
            TenantId = TenantA,
            ConnectionKey = theirs.ConnectionKey,
            ExternalTicketId = ticketId,
            ArtifactType = LinkedArtifactType.Issue,
            ArtifactExternalId = "PROJ-1",
            Method = SupportLinkMethod.IssueKeyMatch,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        }));
    }

    [Fact]
    public async Task A_reviewed_verdict_round_trips_through_the_real_columns()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Service evidence SQL persistence, isolation and review constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var ticketId = Guid.NewGuid().ToString("N");
        await repository.UpsertCaseAsync(Case(connection, ticketId));
        var reviewer = Guid.NewGuid();

        var saved = await repository.UpsertLinkAsync(new SupportCodeLink
        {
            LinkKey = Guid.NewGuid(),
            TenantId = TenantA,
            ConnectionKey = connection.ConnectionKey,
            ExternalTicketId = ticketId,
            ArtifactType = LinkedArtifactType.PullRequest,
            ArtifactExternalId = "pr-42",
            ArtifactSource = "acme/web",
            ArtifactUrl = "https://github.com/acme/web/pull/42",
            Method = SupportLinkMethod.ConfirmedRootCause,
            ReviewedByStaffKey = reviewer,
            ReviewedAtUtc = Sept1,
            ReviewNote = "the null check was removed — see the incident review",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var read = await repository.GetLinkAsync(saved.LinkKey, TenantA);

        Assert.NotNull(read);
        Assert.Equal(SupportLinkMethod.ConfirmedRootCause, read.Method);
        Assert.True(read.IsCausalClaim);
        Assert.Equal(LinkedArtifactType.PullRequest, read.ArtifactType);
        Assert.Equal(reviewer, read.ReviewedByStaffKey);
        Assert.Equal("the null check was removed — see the incident review", read.ReviewNote);
    }

    [Fact]
    public async Task The_same_person_can_hold_two_roles_on_one_case()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Service evidence SQL persistence, isolation and review constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var ticketId = Guid.NewGuid().ToString("N");
        await repository.UpsertCaseAsync(Case(connection, ticketId));

        // Role is part of the identity key: resolving and reviewing are
        // two different claims about the same person.
        await repository.UpsertParticipantAsync(Participant(connection, ticketId, "77", SupportCaseRole.Resolver));
        await repository.UpsertParticipantAsync(Participant(connection, ticketId, "77", SupportCaseRole.Reviewer));

        var participants = await repository.GetParticipantsForCaseAsync(connection.ConnectionKey, ticketId, TenantA);
        Assert.Equal(2, participants.Count);
    }

    [Fact]
    public async Task One_agent_account_cannot_map_to_two_people_on_one_desk()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Service evidence SQL persistence, isolation and review constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var agentId = Guid.NewGuid().ToString("N");

        await repository.CreateAgentLinkAsync(AgentLink(connection, agentId, Guid.NewGuid()));

        await Assert.ThrowsAsync<ServiceOpsValidationException>(() =>
            repository.CreateAgentLinkAsync(AgentLink(connection, agentId, Guid.NewGuid())));
    }

    [Fact]
    public async Task Erasure_detaches_participation_without_deleting_the_case_history()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Service evidence SQL persistence, isolation and review constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var ticketId = Guid.NewGuid().ToString("N");
        var staffKey = Guid.NewGuid();
        await repository.UpsertCaseAsync(Case(connection, ticketId, resolvedAtUtc: Sept1.AddHours(2)));
        await repository.UpsertParticipantAsync(
            Participant(connection, ticketId, "77", SupportCaseRole.Resolver) with { StaffKey = staffKey });
        await repository.CreateAgentLinkAsync(AgentLink(connection, "77-" + ticketId, staffKey));

        Assert.Equal(1, await repository.DeleteAgentLinksForStaffAsync(staffKey));
        Assert.Equal(1, await repository.DetachParticipationForStaffAsync(staffKey, DateTime.UtcNow));

        // The resolution still happened; nobody is named for it.
        var participants = await repository.GetParticipantsForCaseAsync(connection.ConnectionKey, ticketId, TenantA);
        Assert.Single(participants);
        Assert.Null(participants[0].StaffKey);
        Assert.Null(participants[0].AgentDisplayName);
        Assert.NotNull(await repository.GetCaseAsync(connection.ConnectionKey, ticketId, TenantA));
    }

    [Fact]
    public async Task Unmapped_agents_are_grouped_by_account_with_a_distinct_case_count()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Service evidence SQL persistence, isolation and review constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var agentId = "agent-" + Guid.NewGuid().ToString("N")[..8];

        foreach (var suffix in new[] { "a", "b" })
        {
            var ticketId = Guid.NewGuid().ToString("N");
            await repository.UpsertCaseAsync(Case(connection, ticketId));
            await repository.UpsertParticipantAsync(Participant(connection, ticketId, agentId, SupportCaseRole.Resolver));
        }

        var unmapped = await repository.GetUnmappedAgentsAsync(TenantA);
        var row = Assert.Single(unmapped, a => a.ExternalAgentId == agentId);
        Assert.Equal(2, row.Cases);
    }

    [Fact]
    public async Task A_withdrawn_case_is_kept_and_excluded_by_the_caller_rather_than_deleted()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Service evidence SQL persistence, isolation and review constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var ticketId = Guid.NewGuid().ToString("N");
        await repository.UpsertCaseAsync(Case(connection, ticketId));

        Assert.Equal(1, await repository.MarkCaseWithdrawnAsync(connection.ConnectionKey, ticketId, TenantA, DateTime.UtcNow));

        var stored = await repository.GetCaseAsync(connection.ConnectionKey, ticketId, TenantA);
        Assert.NotNull(stored);
        Assert.True(stored.IsWithdrawn);
        Assert.False(stored.CountsTowardsTrends);
    }

    [Fact]
    public async Task Coverage_is_one_row_per_stream_and_observed_from_never_moves_forwards()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Service evidence SQL persistence, isolation and review constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var early = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await repository.UpsertCoverageAsync(Coverage(connection, DeskStream.Tickets, early));
        await repository.UpsertCoverageAsync(Coverage(connection, DeskStream.Tickets, early.AddMonths(6)));
        await repository.UpsertCoverageAsync(Coverage(connection, DeskStream.WithdrawnTickets, early));

        var rows = await repository.GetCoverageAsync(TenantA);
        var mine = rows.Where(r => r.ConnectionKey == connection.ConnectionKey).ToList();
        Assert.Equal(2, mine.Count);
        Assert.Equal(early, mine.Single(r => r.Stream == DeskStream.Tickets).ObservedFromUtc);
    }

    private static SupportCaseParticipant Participant(
        DeskConnection connection, string ticketId, string agentId, SupportCaseRole role) => new()
        {
            ParticipantKey = Guid.NewGuid(),
            TenantId = connection.TenantId,
            ConnectionKey = connection.ConnectionKey,
            ExternalTicketId = ticketId,
            ExternalAgentId = agentId,
            AgentDisplayName = "A. Agent",
            Role = role,
            OccurredAtUtc = Sept1,
            IngestedAtUtc = DateTime.UtcNow
        };

    private static DeskAgentLink AgentLink(DeskConnection connection, string agentId, Guid staffKey) => new()
    {
        LinkKey = Guid.NewGuid(),
        TenantId = connection.TenantId,
        ConnectionKey = connection.ConnectionKey,
        Provider = DeskHostPolicy.FreshdeskProvider,
        ExternalAgentId = agentId,
        ExternalAgentName = "A. Agent",
        StaffKey = staffKey,
        ApprovedAtUtc = DateTime.UtcNow
    };

    private static DeskCoverage Coverage(DeskConnection connection, DeskStream stream, DateTime observedFrom) => new()
    {
        CoverageKey = Guid.NewGuid(),
        TenantId = connection.TenantId,
        ConnectionKey = connection.ConnectionKey,
        Stream = stream,
        Cursor = Sept1,
        ObservedFromUtc = observedFrom,
        CompleteThroughUtc = DateTime.UtcNow,
        Status = DeskCoverageStatus.Complete,
        UpdatedAtUtc = DateTime.UtcNow
    };
}
