using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Real LocalDB, real EngineeringEvidenceRepository, real composite unique
/// indexes.
///
/// The claim these exist to prove is the one the in-memory fake can only
/// assert about itself: that
/// UX_SkillsEvidence_EngineeringEvidence_identity really is unique on
/// (tenantId, connectionKey, sourceType, externalId, role), which is what
/// makes a replayed page idempotent rather than duplicating a person's
/// work. Same reasoning as SkillsEvidenceRepositoryIntegrationTests.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EngineeringEvidenceRepositoryIntegrationTests(ProgrammePulseWebApplicationFactory factory)
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private IEngineeringEvidenceRepository Repository() =>
        factory.Services.CreateScope().ServiceProvider.GetRequiredService<IEngineeringEvidenceRepository>();

    private static string NewAccount() => "acct-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task Revocation_detaches_existing_rows_and_rejects_stale_sync_attribution()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Revocation persistence was not verified.")) return;
        var repository = Repository();
        var connection = await ConnectAsync(repository, Guid.NewGuid(), NewAccount());
        var staff = Guid.NewGuid();
        var link = await repository.CreateActorLinkAsync(Link(connection, "actor", staff));
        var stale = Row(connection, "existing", EvidenceRole.CommitAuthor, "actor", staff);
        Assert.Equal(staff, (await repository.UpsertEvidenceAsync(stale)).StaffKey);
        await repository.DeleteActorLinkAsync(link.LinkKey, connection.TenantId);
        Assert.Empty(await repository.GetEvidenceForStaffAsync(staff, connection.TenantId));
        Assert.Null((await repository.UpsertEvidenceAsync(stale)).StaffKey);
        Assert.Null((await repository.UpsertEvidenceAsync(stale with { EvidenceKey = Guid.NewGuid(), ExternalId = "late" })).StaffKey);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.AttributeEvidenceToStaffAsync(
            connection.ConnectionKey, "actor", staff, connection.TenantId, DateTime.UtcNow));
    }

    [Fact]
    public async Task Withdrawn_processing_decision_blocks_all_collection_writes()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Processing withdrawal persistence was not verified.")) return;
        var repository = Repository();
        var connection = await ConnectAsync(repository, Guid.NewGuid(), NewAccount());
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IContinuityRepository>().WithdrawProcessingDecisionAsync(connection.TenantId, DateTime.UtcNow);
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => repository.UpsertEvidenceAsync(Row(connection, "late", EvidenceRole.CommitAuthor)));
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => repository.SaveRawAsync(connection.TenantId, connection.ConnectionKey, "GitHub", connection.SourceAccountId, "commit", "late", "{}", DateTime.UtcNow));
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => repository.RecordUnmappedSightingAsync(Sighting(connection, "late")));
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => repository.UpsertCoverageAsync(Coverage(connection, "repo", EvidenceStream.Commits, DateTime.UtcNow)));
        Assert.Empty(await repository.GetEvidenceForConnectionAsync(connection.ConnectionKey, connection.TenantId));
    }

    private async Task<EvidenceConnection> ConnectAsync(IEngineeringEvidenceRepository repository, Guid tenantId, string account)
    {
        await EvidenceSqlSetup.PermitAsync(factory, tenantId);
        var now = DateTime.UtcNow;
        return await repository.UpsertConnectionAsync(new EvidenceConnection
        {
            ConnectionKey = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = "GitHub",
            SourceAccountId = account,
            DisplayName = $"github.com/{account}",
            ApiBaseUrl = EvidenceHostPolicy.GitHubDotComApi,
            SelectedRepositories = [$"{account}/web"],
            Status = EvidenceConnectionStatus.Active,
            ProtectedCredentialJson = "ciphertext",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
    }

    private static EngineeringEvidence Row(
        EvidenceConnection connection, string externalId, EvidenceRole role,
        string actorId = "1", Guid? staffKey = null, params string[] hints) => new()
        {
            EvidenceKey = Guid.NewGuid(),
            TenantId = connection.TenantId,
            ConnectionKey = connection.ConnectionKey,
            Provider = "GitHub",
            SourceAccountId = connection.SourceAccountId,
            SourceType = EvidenceSourceType.Commit,
            ExternalId = externalId,
            Role = role,
            ActorExternalId = actorId,
            ActorLogin = "alex",
            ActorIsBot = false,
            StaffKey = staffKey,
            AttributionStatus = staffKey is null ? EvidenceAttributionStatus.Unmapped : EvidenceAttributionStatus.Mapped,
            RepositoryKey = $"{connection.SourceAccountId}/web",
            Title = "A change",
            SourceUrl = "https://github.com/x/y/commit/abc",
            OccurredAtUtc = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
            LanguageHints = hints,
            SchemaVersion = EngineeringEvidenceSchema.CurrentVersion,
            FirstIngestedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    [Fact]
    public async Task Replaying_the_same_artefact_updates_one_row_and_keeps_its_first_seen_stamp()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Engineering evidence SQL persistence, isolation and source-key constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var sha = Guid.NewGuid().ToString("N");

        var first = await repository.UpsertEvidenceAsync(Row(connection, sha, EvidenceRole.CommitAuthor, hints: "csharp"));
        await Task.Delay(10);
        var second = await repository.UpsertEvidenceAsync(
            Row(connection, sha, EvidenceRole.CommitAuthor, hints: "csharp", staffKey: null) with { Title = "Retitled" });

        var all = await repository.GetEvidenceForConnectionAsync(connection.ConnectionKey, TenantA);
        Assert.Single(all);
        Assert.Equal("Retitled", all[0].Title);
        Assert.Equal(["csharp"], all[0].LanguageHints);

        // First-seen survives the replay. Compared with a tolerance
        // because SQL Server's `datetime` rounds to ~3.33ms increments:
        // the value returned by the insert still has in-memory precision,
        // while the one returned by the update has been through the
        // column. Only a real-database test shows that, which is why this
        // one exists — nothing depends on sub-millisecond precision here,
        // but a caller comparing an exact round-trip would be surprised.
        Assert.True((second.FirstIngestedAtUtc - first.FirstIngestedAtUtc).Duration() < TimeSpan.FromMilliseconds(5),
            $"first-seen moved on replay: {first.FirstIngestedAtUtc:O} then {second.FirstIngestedAtUtc:O}");
        Assert.True(all[0].UpdatedAtUtc >= all[0].FirstIngestedAtUtc);
    }

    [Fact]
    public async Task Authorship_verification_is_stored_and_a_replay_can_change_it()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Commit authorship verification (SkillsEvidence step 06) was not verified against SQL Server.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var sha = Guid.NewGuid().ToString("N");

        await repository.UpsertEvidenceAsync(Row(connection, sha, EvidenceRole.CommitAuthor) with { AuthorshipVerified = true });
        await repository.UpsertEvidenceAsync(Row(connection, sha, EvidenceRole.CoAuthor, actorId: "2") with { AuthorshipVerified = false });
        await repository.UpsertEvidenceAsync(Row(connection, sha, EvidenceRole.CommitCommitter, actorId: "3"));

        var rows = (await repository.GetEvidenceForConnectionAsync(connection.ConnectionKey, TenantA)).ToDictionary(r => r.Role);
        Assert.True(rows[EvidenceRole.CommitAuthor].AuthorshipVerified);
        Assert.False(rows[EvidenceRole.CoAuthor].AuthorshipVerified);
        Assert.Null(rows[EvidenceRole.CommitCommitter].AuthorshipVerified);
        Assert.True(rows[EvidenceRole.CommitCommitter].AuthorshipUnverified);

        // A re-signed or re-read commit replays onto the same row.
        await repository.UpsertEvidenceAsync(Row(connection, sha, EvidenceRole.CommitAuthor) with { AuthorshipVerified = false });
        var author = Assert.Single(await repository.GetEvidenceForConnectionAsync(connection.ConnectionKey, TenantA), r => r.Role == EvidenceRole.CommitAuthor);
        Assert.False(author.AuthorshipVerified);
    }

    [Fact]
    public async Task The_same_artefact_in_two_roles_is_two_rows_not_a_collision()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Engineering evidence SQL persistence, isolation and source-key constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var sha = Guid.NewGuid().ToString("N");

        // Role is part of the identity key precisely so that an author and
        // a committer on one commit are two distinct claims.
        await repository.UpsertEvidenceAsync(Row(connection, sha, EvidenceRole.CommitAuthor, "1"));
        await repository.UpsertEvidenceAsync(Row(connection, sha, EvidenceRole.CommitCommitter, "2"));

        var all = await repository.GetEvidenceForConnectionAsync(connection.ConnectionKey, TenantA);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Two_tenants_may_hold_the_same_external_id_without_colliding()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Engineering evidence SQL persistence, isolation and source-key constraints were not verified.")) return;

        var repository = Repository();
        var sha = Guid.NewGuid().ToString("N");
        var connectionA = await ConnectAsync(repository, TenantA, NewAccount());
        var connectionB = await ConnectAsync(repository, TenantB, NewAccount());

        await repository.UpsertEvidenceAsync(Row(connectionA, sha, EvidenceRole.CommitAuthor));
        await repository.UpsertEvidenceAsync(Row(connectionB, sha, EvidenceRole.CommitAuthor));

        Assert.Single(await repository.GetEvidenceForConnectionAsync(connectionA.ConnectionKey, TenantA));
        Assert.Single(await repository.GetEvidenceForConnectionAsync(connectionB.ConnectionKey, TenantB));
        Assert.Empty(await repository.GetEvidenceForConnectionAsync(connectionA.ConnectionKey, TenantB));
    }

    [Fact]
    public async Task One_tenant_may_connect_two_accounts_of_the_same_provider()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Engineering evidence SQL persistence, isolation and source-key constraints were not verified.")) return;

        var repository = Repository();
        var first = await ConnectAsync(repository, TenantA, NewAccount());
        var second = await ConnectAsync(repository, TenantA, NewAccount());

        Assert.NotEqual(first.ConnectionKey, second.ConnectionKey);

        var connections = await repository.GetConnectionsAsync(TenantA);
        Assert.Contains(connections, c => c.ConnectionKey == first.ConnectionKey);
        Assert.Contains(connections, c => c.ConnectionKey == second.ConnectionKey);
    }

    [Fact]
    public async Task Reconnecting_the_same_account_updates_in_place_rather_than_duplicating()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Engineering evidence SQL persistence, isolation and source-key constraints were not verified.")) return;

        var repository = Repository();
        var account = NewAccount();
        var first = await ConnectAsync(repository, TenantA, account);
        var second = await ConnectAsync(repository, TenantA, account);

        Assert.Equal(first.ConnectionKey, second.ConnectionKey);
    }

    [Fact]
    public async Task One_account_cannot_be_mapped_to_two_people_on_one_connection()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Engineering evidence SQL persistence, isolation and source-key constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var actorId = Guid.NewGuid().ToString("N");

        await repository.CreateActorLinkAsync(Link(connection, actorId, Guid.NewGuid()));

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() =>
            repository.CreateActorLinkAsync(Link(connection, actorId, Guid.NewGuid())));
    }

    [Fact]
    public async Task A_link_against_another_tenants_connection_is_refused()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Engineering evidence SQL persistence, isolation and source-key constraints were not verified.")) return;

        var repository = Repository();
        var theirs = await ConnectAsync(repository, TenantB, NewAccount());

        var link = Link(theirs, Guid.NewGuid().ToString("N"), Guid.NewGuid()) with { TenantId = TenantA };

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => repository.CreateActorLinkAsync(link));
    }

    [Fact]
    public async Task Approving_then_revoking_attributes_and_detaches_existing_rows()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Engineering evidence SQL persistence, isolation and source-key constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var actorId = Guid.NewGuid().ToString("N");
        var staffKey = Guid.NewGuid();

        await repository.UpsertEvidenceAsync(Row(connection, Guid.NewGuid().ToString("N"), EvidenceRole.CommitAuthor, actorId));
        await repository.UpsertEvidenceAsync(Row(connection, Guid.NewGuid().ToString("N"), EvidenceRole.CommitAuthor, actorId));

        await repository.CreateActorLinkAsync(Link(connection, actorId, staffKey));
        var attributed = await repository.AttributeEvidenceToStaffAsync(connection.ConnectionKey, actorId, staffKey, TenantA, DateTime.UtcNow);
        Assert.Equal(2, attributed);
        Assert.Equal(2, (await repository.GetEvidenceForStaffAsync(staffKey, TenantA)).Count);

        var detached = await repository.DetachEvidenceFromStaffAsync(connection.ConnectionKey, actorId, TenantA, DateTime.UtcNow);
        Assert.Equal(2, detached);
        Assert.Empty(await repository.GetEvidenceForStaffAsync(staffKey, TenantA));
        // Detached, not deleted — the commits really happened.
        Assert.Equal(2, (await repository.GetEvidenceForConnectionAsync(connection.ConnectionKey, TenantA)).Count);
    }

    [Fact]
    public async Task A_repeated_sighting_updates_one_queue_row_and_reopens_a_resolved_one()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Engineering evidence SQL persistence, isolation and source-key constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var actorId = Guid.NewGuid().ToString("N");

        var first = await repository.RecordUnmappedSightingAsync(Sighting(connection, actorId));
        var second = await repository.RecordUnmappedSightingAsync(Sighting(connection, actorId));

        Assert.Equal(first.UnmappedActorKey, second.UnmappedActorKey);
        Assert.Equal(2, second.OccurrenceCount);

        await repository.MarkUnmappedResolvedAsync(first.UnmappedActorKey, TenantA, DateTime.UtcNow);
        var third = await repository.RecordUnmappedSightingAsync(Sighting(connection, actorId));
        Assert.True(third.IsOpen);
    }

    [Fact]
    public async Task Coverage_is_one_row_per_repository_and_stream_and_never_moves_observed_from_forwards()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Engineering evidence SQL persistence, isolation and source-key constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var repoKey = $"{connection.SourceAccountId}/web";
        var early = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await repository.UpsertCoverageAsync(Coverage(connection, repoKey, EvidenceStream.Commits, early));
        await repository.UpsertCoverageAsync(Coverage(connection, repoKey, EvidenceStream.Commits, early.AddMonths(6)));
        await repository.UpsertCoverageAsync(Coverage(connection, repoKey, EvidenceStream.PullRequests, early));

        var rows = await repository.GetCoverageForConnectionAsync(connection.ConnectionKey, TenantA);
        Assert.Equal(2, rows.Count);
        // The earliest point ever observed is a fact about history, not
        // about the most recent run.
        Assert.Equal(early, rows.Single(r => r.Stream == EvidenceStream.Commits).ObservedFromUtc);
    }

    [Fact]
    public async Task Erasure_removes_the_subjects_rows_and_leaves_the_unattributed_remainder()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Engineering evidence SQL persistence, isolation and source-key constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var staffKey = Guid.NewGuid();

        await repository.CreateActorLinkAsync(Link(connection, "1", staffKey));
        await repository.UpsertEvidenceAsync(Row(connection, Guid.NewGuid().ToString("N"), EvidenceRole.CommitAuthor, "1", staffKey));
        await repository.UpsertEvidenceAsync(Row(connection, Guid.NewGuid().ToString("N"), EvidenceRole.CommitAuthor, "9"));
        Assert.Equal(1, await repository.DeleteActorLinksForStaffAsync(staffKey));
        Assert.Equal(1, await repository.DeleteEvidenceForStaffAsync(staffKey));

        var remaining = await repository.GetEvidenceForConnectionAsync(connection.ConnectionKey, TenantA);
        Assert.Single(remaining);
        Assert.Null(remaining[0].StaffKey);
    }

    [Fact]
    public async Task Erasure_removes_every_trace_of_the_subjects_accounts_and_their_bronze_pages()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Engineering evidence SQL persistence, isolation and source-key constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());
        var staffKey = Guid.NewGuid();
        var (unattributed, attributed, colleague) = ($"x{Guid.NewGuid():N}", $"y{Guid.NewGuid():N}", $"z{Guid.NewGuid():N}");

        // Ingested before the link existed, so never attributed, but still the subject's account.
        await repository.UpsertEvidenceAsync(Row(connection, unattributed, EvidenceRole.CommitAuthor, "1"));
        await repository.RecordUnmappedSightingAsync(Sighting(connection, "1"));
        await repository.CreateActorLinkAsync(Link(connection, "1", staffKey));
        await repository.UpsertEvidenceAsync(Row(connection, attributed, EvidenceRole.CommitAuthor, "1", staffKey));
        await repository.UpsertEvidenceAsync(Row(connection, colleague, EvidenceRole.CommitAuthor, "9"));
        foreach (var id in new[] { unattributed, attributed, colleague })
        {
            await repository.SaveRawAsync(connection.TenantId, connection.ConnectionKey, "GitHub", connection.SourceAccountId, "commit", id, "{}", DateTime.UtcNow);
        }

        var identities = (await repository.GetActorLinksForStaffAsync(staffKey)).Select(l => (l.TenantId, l.ConnectionKey, l.ExternalActorId)).ToList();
        await repository.DeleteActorLinksForStaffAsync(staffKey);
        var counts = await repository.EraseSubjectTracesAsync(staffKey, identities);

        Assert.Equal((2, 1, 2), (counts.EvidenceRows, counts.UnmappedActors, counts.RawPages));
        Assert.Equal([colleague], (await repository.GetEvidenceForConnectionAsync(connection.ConnectionKey, TenantA)).Select(e => e.ExternalId));
        using var scope = factory.Services.GetRequiredService<Umbraco.Cms.Infrastructure.Scoping.IScopeProvider>().CreateScope(autoComplete: true);
        Assert.Equal(1, await scope.Database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM [SkillsEvidence_RawPayload] WHERE [connectionKey] = @0", new object[] { connection.ConnectionKey }));
    }

    [Fact]
    public async Task Enums_language_hints_and_repository_selections_round_trip_through_the_real_columns()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Engineering evidence SQL persistence, isolation and source-key constraints were not verified.")) return;

        var repository = Repository();
        var connection = await ConnectAsync(repository, TenantA, NewAccount());

        var saved = await repository.UpsertEvidenceAsync(
            Row(connection, Guid.NewGuid().ToString("N"), EvidenceRole.Reviewer, hints: ["csharp", "razor"]) with
            {
                SourceType = EvidenceSourceType.Review,
                AttributionStatus = EvidenceAttributionStatus.Ambiguous,
                ActorIsBot = true
            });

        var read = (await repository.GetEvidenceForConnectionAsync(connection.ConnectionKey, TenantA))
            .Single(e => e.EvidenceKey == saved.EvidenceKey);

        Assert.Equal(EvidenceSourceType.Review, read.SourceType);
        Assert.Equal(EvidenceRole.Reviewer, read.Role);
        Assert.Equal(EvidenceAttributionStatus.Bot, read.AttributionStatus);
        Assert.True(read.ActorIsBot);
        Assert.Equal(["csharp", "razor"], read.LanguageHints);

        var storedConnection = await repository.GetConnectionAsync(connection.ConnectionKey, TenantA);
        Assert.Equal([$"{connection.SourceAccountId}/web"], storedConnection!.SelectedRepositories);
    }

    private static EvidenceActorLink Link(EvidenceConnection connection, string actorId, Guid staffKey) => new()
    {
        LinkKey = Guid.NewGuid(),
        TenantId = connection.TenantId,
        ConnectionKey = connection.ConnectionKey,
        Provider = "GitHub",
        ExternalActorId = actorId,
        ExternalLogin = "alex",
        StaffKey = staffKey,
        ApprovedAtUtc = DateTime.UtcNow
    };

    private static UnmappedEvidenceActor Sighting(EvidenceConnection connection, string actorId) => new()
    {
        UnmappedActorKey = Guid.NewGuid(),
        TenantId = connection.TenantId,
        ConnectionKey = connection.ConnectionKey,
        Provider = "GitHub",
        ExternalActorId = actorId,
        ExternalLogin = "alex",
        IsBot = false,
        Reason = UnmappedActorReason.NoCandidate,
        OccurrenceCount = 1,
        FirstSeenUtc = DateTime.UtcNow,
        LastSeenUtc = DateTime.UtcNow
    };

    private static EvidenceCoverage Coverage(EvidenceConnection connection, string repositoryKey, EvidenceStream stream, DateTime observedFrom) => new()
    {
        CoverageKey = Guid.NewGuid(),
        TenantId = connection.TenantId,
        ConnectionKey = connection.ConnectionKey,
        RepositoryKey = repositoryKey,
        Stream = stream,
        Cursor = "c1",
        ObservedFromUtc = observedFrom,
        CompleteThroughUtc = DateTime.UtcNow,
        Status = EvidenceCoverageStatus.Complete,
        UpdatedAtUtc = DateTime.UtcNow
    };
}
