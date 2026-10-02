using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Real LocalDB, real SkillsEvidenceRepository, real SQL Server indexes.
///
/// The one thing the in-memory fake cannot honestly prove: that
/// UX_SkillsEvidence_StaffSkillAssertion_current — a *filtered* unique
/// index, <c>WHERE [supersededAtUtc] IS NULL</c> — actually permits an
/// unbounded history while allowing exactly one live row per
/// (tenant, staff, skill). The fake reproduces that rule in C#; only the
/// database can show the rule is really there. Same reasoning as
/// ProgrammeRepositoryTenantIsolationIntegrationTests.
///
/// Each test seeds uniquely-keyed rows so runs against the same persistent
/// test database neither collide nor need cleaning up.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SkillsEvidenceRepositoryIntegrationTests(ProgrammePulseWebApplicationFactory factory)
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private ISkillsEvidenceRepository Repository() =>
        factory.Services.CreateScope().ServiceProvider.GetRequiredService<ISkillsEvidenceRepository>();

    private static string NewSkillKey() => "it-" + Guid.NewGuid().ToString("N");

    private async Task<string> SeedSkillAsync(ISkillsEvidenceRepository repository, Guid tenantId)
    {
        var key = NewSkillKey();
        var now = DateTime.UtcNow;
        await repository.CreateSkillAsync(new SkillDefinition
        {
            SkillDefinitionKey = Guid.NewGuid(),
            TenantId = tenantId,
            SkillKey = key,
            Name = $"Integration test skill {key}",
            Kind = SkillKind.Practice,
            TaxonomyVersion = SkillTaxonomy.CurrentVersion,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        return key;
    }

    private static StaffSkillAssertion NewAssertion(Guid tenantId, Guid staffKey, string skillKey, ProficiencyLevel level) => new()
    {
        AssertionKey = Guid.NewGuid(),
        TenantId = tenantId,
        StaffKey = staffKey,
        SkillKey = skillKey,
        Proficiency = level,
        Origin = AssertionOrigin.SelfDeclared,
        Status = AssertionStatus.Submitted,
        RecordedByStaffKey = staffKey,
        RecordedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task History_grows_without_limit_while_exactly_one_row_stays_current()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Skills evidence SQL persistence, isolation and revision constraints were not verified.")) return;

        var repository = Repository();
        var skillKey = await SeedSkillAsync(repository, TenantA);
        var staffKey = Guid.NewGuid();

        var current = await repository.AppendAsync(
            NewAssertion(TenantA, staffKey, skillKey, ProficiencyLevel.Awareness), null, DateTime.UtcNow);

        // Five supersessions through the real filtered index.
        foreach (var level in new[] { ProficiencyLevel.Working, ProficiencyLevel.Practitioner, ProficiencyLevel.Lead, ProficiencyLevel.Working, ProficiencyLevel.Practitioner })
        {
            current = await repository.AppendAsync(
                NewAssertion(TenantA, staffKey, skillKey, level) with { SupersedesAssertionKey = current.AssertionKey },
                current,
                DateTime.UtcNow);
        }

        var history = await repository.GetHistoryForStaffAsync(staffKey, TenantA);
        Assert.Equal(6, history.Count);
        Assert.Single(history, a => a.IsCurrent);
        Assert.Equal(ProficiencyLevel.Practitioner, history.Single(a => a.IsCurrent).Proficiency);

        var live = await repository.GetCurrentForStaffAsync(staffKey, TenantA);
        Assert.Single(live);
    }

    [Fact]
    public async Task A_second_live_row_for_the_same_person_and_skill_is_refused_by_the_database()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Skills evidence SQL persistence, isolation and revision constraints were not verified.")) return;

        var repository = Repository();
        var skillKey = await SeedSkillAsync(repository, TenantA);
        var staffKey = Guid.NewGuid();

        await repository.AppendAsync(NewAssertion(TenantA, staffKey, skillKey, ProficiencyLevel.Working), null, DateTime.UtcNow);

        // Appending without naming what it supersedes is the bug this index
        // exists to catch. SQL Server refuses it; the exception type is the
        // provider's, so this asserts only that the write does not succeed.
        await Assert.ThrowsAnyAsync<Exception>(() => repository.AppendAsync(
            NewAssertion(TenantA, staffKey, skillKey, ProficiencyLevel.Lead), null, DateTime.UtcNow));

        Assert.Single(await repository.GetCurrentForStaffAsync(staffKey, TenantA));
    }

    [Fact]
    public async Task Two_tenants_may_hold_the_same_skill_key_and_never_see_each_others_rows()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Skills evidence SQL persistence, isolation and revision constraints were not verified.")) return;

        var repository = Repository();
        var sharedKey = NewSkillKey();
        var now = DateTime.UtcNow;

        foreach (var tenantId in new[] { TenantA, TenantB })
        {
            await repository.CreateSkillAsync(new SkillDefinition
            {
                SkillDefinitionKey = Guid.NewGuid(),
                TenantId = tenantId,
                SkillKey = sharedKey,
                Name = "Shared key, different tenants",
                Kind = SkillKind.Component,
                TaxonomyVersion = SkillTaxonomy.CurrentVersion,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        var staffA = Guid.NewGuid();
        var staffB = Guid.NewGuid();
        await repository.AppendAsync(NewAssertion(TenantA, staffA, sharedKey, ProficiencyLevel.Lead), null, now);
        await repository.AppendAsync(NewAssertion(TenantB, staffB, sharedKey, ProficiencyLevel.Lead), null, now);

        Assert.Single(await repository.GetCurrentForStaffAsync(staffA, TenantA));
        Assert.Empty(await repository.GetCurrentForStaffAsync(staffA, TenantB));
        Assert.DoesNotContain(await repository.GetCurrentForTenantAsync(TenantA), a => a.StaffKey == staffB);
    }

    [Fact]
    public async Task A_duplicate_skill_key_within_one_tenant_is_refused()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Skills evidence SQL persistence, isolation and revision constraints were not verified.")) return;

        var repository = Repository();
        var key = await SeedSkillAsync(repository, TenantA);

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => repository.CreateSkillAsync(new SkillDefinition
        {
            SkillDefinitionKey = Guid.NewGuid(),
            TenantId = TenantA,
            SkillKey = key,
            Name = "Second definition, same key",
            Kind = SkillKind.Practice,
            TaxonomyVersion = SkillTaxonomy.CurrentVersion,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        }));
    }

    [Fact]
    public async Task Declaring_against_another_tenants_skill_is_refused_as_not_found()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Skills evidence SQL persistence, isolation and revision constraints were not verified.")) return;

        var repository = Repository();
        var theirKey = await SeedSkillAsync(repository, TenantB);

        // A real, existing skill key — in the wrong tenant. The write must
        // read as "not found", indistinguishable from a key nobody owns.
        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => repository.AppendAsync(
            NewAssertion(TenantA, Guid.NewGuid(), theirKey, ProficiencyLevel.Working), null, DateTime.UtcNow));
    }

    [Fact]
    public async Task Superseding_another_tenants_assertion_is_refused()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Skills evidence SQL persistence, isolation and revision constraints were not verified.")) return;

        var repository = Repository();
        var keyA = await SeedSkillAsync(repository, TenantA);
        var keyB = await SeedSkillAsync(repository, TenantB);
        var staffA = Guid.NewGuid();
        var staffB = Guid.NewGuid();

        var theirs = await repository.AppendAsync(
            NewAssertion(TenantB, staffB, keyB, ProficiencyLevel.Lead), null, DateTime.UtcNow);

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => repository.AppendAsync(
            NewAssertion(TenantA, staffA, keyA, ProficiencyLevel.Working), theirs, DateTime.UtcNow));

        // And their row is untouched — a rejected write must not have
        // half-applied the supersession stamp.
        Assert.Single(await repository.GetCurrentForStaffAsync(staffB, TenantB));
    }

    [Fact]
    public async Task Erasure_removes_every_row_for_the_subject_and_leaves_everyone_else_alone()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Skills evidence SQL persistence, isolation and revision constraints were not verified.")) return;

        var repository = Repository();
        var skillKey = await SeedSkillAsync(repository, TenantA);
        var subject = Guid.NewGuid();
        var colleague = Guid.NewGuid();

        var first = await repository.AppendAsync(
            NewAssertion(TenantA, subject, skillKey, ProficiencyLevel.Working), null, DateTime.UtcNow);
        await repository.AppendAsync(
            NewAssertion(TenantA, subject, skillKey, ProficiencyLevel.Lead) with { SupersedesAssertionKey = first.AssertionKey },
            first, DateTime.UtcNow);
        await repository.AppendAsync(
            NewAssertion(TenantA, colleague, skillKey, ProficiencyLevel.Lead), null, DateTime.UtcNow);

        var deleted = await repository.DeleteAllForStaffAsync(subject);

        Assert.Equal(2, deleted);   // current *and* superseded history
        Assert.Empty(await repository.GetHistoryForStaffAsync(subject, TenantA));
        Assert.Single(await repository.GetCurrentForStaffAsync(colleague, TenantA));
    }

    [Fact]
    public async Task A_retired_skill_is_kept_and_excluded_from_the_active_list_rather_than_deleted()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Skills evidence SQL persistence, isolation and revision constraints were not verified.")) return;

        var repository = Repository();
        var key = await SeedSkillAsync(repository, TenantA);

        await repository.SetSkillActiveAsync(key, false, TenantA, DateTime.UtcNow);

        Assert.DoesNotContain(await repository.GetSkillsAsync(TenantA, includeRetired: false), s => s.SkillKey == key);
        Assert.Contains(await repository.GetSkillsAsync(TenantA, includeRetired: true), s => s.SkillKey == key);
        Assert.NotNull(await repository.GetSkillAsync(key, TenantA));
    }

    [Fact]
    public async Task Enums_and_dates_round_trip_through_the_real_columns()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Skills evidence SQL persistence, isolation and revision constraints were not verified.")) return;

        var repository = Repository();
        var skillKey = await SeedSkillAsync(repository, TenantA);
        var staffKey = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var reviewDue = new DateOnly(2027, 3, 14);

        var saved = await repository.AppendAsync(NewAssertion(TenantA, staffKey, skillKey, ProficiencyLevel.Practitioner) with
        {
            Origin = AssertionOrigin.ReviewerRecorded,
            Status = AssertionStatus.Validated,
            EvidenceNote = "note with a — dash and an ' apostrophe",
            ReviewerStaffKey = reviewer,
            ReviewedAtUtc = new DateTime(2026, 3, 14, 10, 30, 0, DateTimeKind.Utc),
            ReviewNote = "reviewer rationale",
            ReviewDueOn = reviewDue
        }, null, DateTime.UtcNow);

        var read = await repository.GetAssertionAsync(saved.AssertionKey, TenantA);

        Assert.NotNull(read);
        // Enums are stored by name, so these would survive a re-ordering of
        // the enum — the reason for that choice, checked against real columns.
        Assert.Equal(ProficiencyLevel.Practitioner, read.Proficiency);
        Assert.Equal(AssertionOrigin.ReviewerRecorded, read.Origin);
        Assert.Equal(AssertionStatus.Validated, read.Status);
        // DateOnly has no SQL Server column type here; it goes through
        // datetime at midnight and must come back as the same date.
        Assert.Equal(reviewDue, read.ReviewDueOn);
        Assert.Equal("note with a — dash and an ' apostrophe", read.EvidenceNote);
        Assert.Equal("reviewer rationale", read.ReviewNote);
    }
}
