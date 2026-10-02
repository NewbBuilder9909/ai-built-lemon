using System.Net;
using ProgrammePulse.Tests.Personas;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ContributionReviewIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output) : IDisposable
{
    private IServiceScope? testScope;
    private bool Ready() => ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("contribution review SQL and HTTP behaviour was not exercised.", output);
    private T Get<T>() where T : notnull => (testScope ??= factory.Services.CreateScope()).ServiceProvider.GetRequiredService<T>();
    public void Dispose() => testScope?.Dispose();

    [Fact]
    public async Task Review_correction_and_withdrawal_preserve_history_without_promoting_skill()
    {
        if (!Ready()) return;
        var seed = await SeedAsync();
        var service = Get<ContributionReviewService>();
        var key = await SubmitAsync(seed);
        await service.DecideAsync(key, seed.Reviewer, seed.Tenant, 1, ContributionReviewStatus.Accepted, "Shows verification of failure cases.");
        var accepted = await Get<IContributionReviewRepository>().GetCurrentAsync(key, seed.Tenant);
        Assert.Equal(ContributionReviewStatus.Accepted, accepted!.Status);
        Assert.Equal("Claude", accepted.AiTool);
        var unchanged = await Get<ISkillsEvidenceRepository>().GetAssertionAsync(seed.Assertion, seed.Tenant);
        Assert.Equal(AssertionStatus.Submitted, unchanged!.Status);
        Assert.Equal(ProficiencyLevel.Working, unchanged.Proficiency);
        await service.ReviseAsync(key, seed.Subject, seed.Tenant, 2, "I reviewed the tests rather than writing the code.", null, AiAssistanceWorkflow.Unspecified);
        var corrected = await Get<IContributionReviewRepository>().GetCurrentAsync(key, seed.Tenant);
        Assert.Equal(ContributionReviewStatus.Submitted, corrected!.Status);
        Assert.Null(corrected.AiTool);
        Assert.Null(corrected.DecisionNote);
        await service.WithdrawAsync(key, seed.Subject, seed.Tenant, 3);
        var history = await Get<IContributionReviewRepository>().GetHistoryAsync(key, seed.Tenant);
        Assert.Equal(new[] { 4, 3, 2, 1 }, history.Select(r => r.Revision));
        Assert.Equal(ContributionReviewStatus.Withdrawn, history[0].Status);
        Assert.Equal("Shows verification of failure cases.", history[2].DecisionNote);
    }

    [Fact]
    public async Task Another_subject_or_tenant_cannot_attach_or_edit_evidence()
    {
        if (!Ready()) return;
        var seed = await SeedAsync();
        var other = await SeedAsync();
        var service = Get<ContributionReviewService>();
        var key = await SubmitAsync(seed);
        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => service.SubmitAsync(seed.Reviewer, seed.Tenant, seed.Assertion, seed.Evidence.EvidenceKey, "Forged", null, AiAssistanceWorkflow.Unspecified));
        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => service.SubmitAsync(seed.Subject, seed.Tenant, seed.Assertion, other.Evidence.EvidenceKey, "Forged", null, AiAssistanceWorkflow.Unspecified));
        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => service.WithdrawAsync(key, seed.Reviewer, seed.Tenant, 1));
        Assert.Null(await Get<IContributionReviewRepository>().GetCurrentAsync(key, other.Tenant));
        Assert.Empty(await Get<IContributionReviewRepository>().GetPageAsync(seed.Subject, other.Tenant, 1));
        Assert.Empty(await Get<IContributionReviewRepository>().ExportAsync(seed.Subject, other.Tenant));
    }

    [Fact]
    public async Task Self_review_empty_notes_and_unknown_enum_values_are_refused()
    {
        if (!Ready()) return;
        var seed = await SeedAsync();
        var service = Get<ContributionReviewService>();
        var key = await SubmitAsync(seed);
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => service.DecideAsync(key, seed.Subject, seed.Tenant, 1, ContributionReviewStatus.Accepted, "Self"));
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => service.DecideAsync(key, seed.Reviewer, seed.Tenant, 1, ContributionReviewStatus.Accepted, " "));
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => service.DecideAsync(key, seed.Reviewer, seed.Tenant, 1, (ContributionReviewStatus)99, "Invalid"));
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => service.ReviseAsync(key, seed.Subject, seed.Tenant, 1, "Correction", "Tool", (AiAssistanceWorkflow)99));
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => service.ReviseAsync(key, seed.Subject, seed.Tenant, 1, "Correction", null, AiAssistanceWorkflow.Testing));
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => service.ReviseAsync(key, seed.Subject, seed.Tenant, 1, new string('x', 1001), null, AiAssistanceWorkflow.Unspecified));
        Assert.Single(await Get<IContributionReviewRepository>().GetHistoryAsync(key, seed.Tenant));
    }

    [Fact]
    public async Task Concurrent_decisions_accept_exactly_one_expected_revision()
    {
        if (!Ready()) return;
        var seed = await SeedAsync();
        var key = await SubmitAsync(seed);
        async Task<bool> Decide()
        {
            using var scope = factory.Services.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<ContributionReviewService>().DecideAsync(key, seed.Reviewer, seed.Tenant, 1,
                    ContributionReviewStatus.Accepted, "Concurrent decision");
                return true;
            }
            catch (ContributionReviewConflictException) { return false; }
        }
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(Decide)));
        Assert.Single(outcomes, x => x);
        Assert.Equal(2, (await Get<IContributionReviewRepository>().GetHistoryAsync(key, seed.Tenant)).Count);
    }

    [Fact]
    public async Task Duplicate_submission_is_refused_without_creating_another_link()
    {
        if (!Ready()) return;
        var seed = await SeedAsync();
        await SubmitAsync(seed);
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => SubmitAsync(seed));
        Assert.Single(await Get<IContributionReviewRepository>().GetPageAsync(seed.Subject, seed.Tenant, 1));
    }

    [Fact]
    public async Task Revoked_attribution_hides_the_source_and_blocks_review_but_allows_withdrawal()
    {
        if (!Ready()) return;
        var seed = await SeedAsync();
        var key = await SubmitAsync(seed);
        await Get<IEngineeringEvidenceRepository>().DeleteActorLinkAsync(seed.SubjectLink, seed.Tenant);
        var detail = await Get<ContributionReviewService>().GetDetailAsync(key, seed.Tenant);
        Assert.Null(detail.Source);
        Assert.False(detail.CanUseEvidence);
        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => Get<ContributionReviewService>().DecideAsync(key, seed.Reviewer, seed.Tenant, 1, ContributionReviewStatus.Accepted, "Too late"));
        await Get<ContributionReviewService>().WithdrawAsync(key, seed.Subject, seed.Tenant, 1);
    }

    [Fact]
    public async Task Disconnected_and_deselected_repositories_are_not_eligible()
    {
        if (!Ready()) return;
        var seed = await SeedAsync();
        var evidence = Get<IEngineeringEvidenceRepository>();
        var connection = await evidence.GetConnectionAsync(seed.Evidence.ConnectionKey, seed.Tenant);
        await evidence.UpsertConnectionAsync(connection! with { SelectedRepositories = [] });
        Assert.Empty(await Get<IContributionReviewRepository>().GetSourcesAsync(seed.Subject, seed.Tenant));
        await evidence.UpsertConnectionAsync(connection! with { Status = EvidenceConnectionStatus.Disconnected });
        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => SubmitAsync(seed));
    }

    [Fact]
    public async Task Superseded_assertion_cannot_receive_a_new_decision()
    {
        if (!Ready()) return;
        var seed = await SeedAsync();
        var key = await SubmitAsync(seed);
        var assertion = await Get<ISkillsEvidenceRepository>().GetAssertionAsync(seed.Assertion, seed.Tenant);
        await Get<ISkillAssertionService>().DeclareAsync(seed.Subject, assertion!.SkillKey, ProficiencyLevel.Practitioner, "New claim", seed.Tenant, null);
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => Get<ContributionReviewService>().DecideAsync(key, seed.Reviewer, seed.Tenant, 1, ContributionReviewStatus.Accepted, "Stale"));
        Assert.False((await Get<ContributionReviewService>().GetDetailAsync(key, seed.Tenant)).CanUseEvidence);
        await Get<ContributionReviewService>().WithdrawAsync(key, seed.Subject, seed.Tenant, 1);
    }

    [Fact]
    public async Task Erasure_removes_subject_history_and_redacts_reviewer_identity_and_rationale()
    {
        if (!Ready()) return;
        var seed = await SeedAsync();
        var key = await SubmitAsync(seed);
        await Get<ContributionReviewService>().DecideAsync(key, seed.Reviewer, seed.Tenant, 1, ContributionReviewStatus.Accepted, "Reviewer personal rationale");
        var participant = new ContributionReviewDataParticipant(Get<IContributionReviewRepository>(), Get<IStaffRepository>());
        Assert.Equal(2, (await participant.ExportAsync(seed.Subject)).Count);
        var reviewerExport = Assert.Single(await participant.ExportAsync(seed.Reviewer));
        Assert.Contains("Reviewer personal rationale", reviewerExport.Summary);
        await participant.EraseAsync(seed.Reviewer, DateTime.UtcNow);
        var current = await Get<IContributionReviewRepository>().GetCurrentAsync(key, seed.Tenant);
        Assert.Equal(Guid.Empty, current!.RecordedByStaffKey);
        Assert.Null(current.DecisionNote);
        Assert.Equal(ContributionReviewStatus.Accepted, current.Status);
        await participant.EraseAsync(seed.Subject, DateTime.UtcNow);
        Assert.Empty(await participant.ExportAsync(seed.Subject));
        Assert.Empty(await Get<IContributionReviewRepository>().GetHistoryAsync(key, seed.Tenant));
    }

    [Fact]
    public async Task Page_boundaries_return_latest_revisions_only()
    {
        if (!Ready()) return;
        var seed = await SeedAsync();
        var links = new List<Guid>();
        for (var i = 0; i < 22; i++)
        {
            var evidence = await Get<IEngineeringEvidenceRepository>().UpsertEvidenceAsync(seed.Evidence with { EvidenceKey = Guid.NewGuid(), ExternalId = "page-" + i });
            links.Add(await Get<ContributionReviewService>().SubmitAsync(seed.Subject, seed.Tenant, seed.Assertion, evidence.EvidenceKey, "Example " + i, null, AiAssistanceWorkflow.Unspecified));
        }
        await Get<ContributionReviewService>().DecideAsync(links[0], seed.Reviewer, seed.Tenant, 1, ContributionReviewStatus.Rejected, "Needs detail");
        var first = await Get<ContributionReviewService>().GetPageAsync(seed.Subject, seed.Tenant, 1);
        var second = await Get<ContributionReviewService>().GetPageAsync(seed.Subject, seed.Tenant, 2);
        Assert.True(first.HasMore);
        Assert.Equal(20, first.Examples.Count);
        Assert.Equal(2, second.Examples.Count);
        Assert.False(second.HasMore);
        Assert.Equal(22, first.Examples.Concat(second.Examples).Select(r => r.LinkKey).Distinct().Count());
        Assert.Equal(2, first.Examples.Single(r => r.LinkKey == links[0]).Revision);
    }

    [Fact]
    public async Task Older_source_choices_are_bounded_isolated_and_preserved_after_validation_errors()
    {
        if (!Ready()) return;
        var seed = await SeedAsync(members: true);
        var evidenceRepository = Get<IEngineeringEvidenceRepository>();
        for (var i = 0; i < 101; i++)
            await evidenceRepository.UpsertEvidenceAsync(seed.Evidence with
            {
                EvidenceKey = Guid.NewGuid(), ExternalId = "older-" + i,
                OccurredAtUtc = seed.Evidence.OccurredAtUtc.AddDays(-1)
            });
        // A newer row belonging to somebody else must not displace a subject's row.
        await evidenceRepository.UpsertEvidenceAsync(seed.Evidence with
        {
            EvidenceKey = Guid.NewGuid(), ExternalId = "other-person", ActorExternalId = "reviewer-actor",
            OccurredAtUtc = seed.Evidence.OccurredAtUtc.AddDays(1)
        });
        var repository = Get<IContributionReviewRepository>();
        var first = await repository.GetSourcePageAsync(seed.Subject, seed.Tenant, 1);
        var second = await repository.GetSourcePageAsync(seed.Subject, seed.Tenant, 2);
        Assert.Equal(100, first.Items.Count);
        Assert.True(first.HasNext);
        Assert.Equal(2, second.Items.Count);
        Assert.False(second.HasNext);
        Assert.Equal(102, first.Items.Concat(second.Items).Select(r => r.EvidenceKey).Distinct().Count());
        Assert.Equal(seed.Evidence.EvidenceKey, first.Items[0].EvidenceKey);
        Assert.Empty((await repository.GetSourcePageAsync(seed.Subject, Guid.NewGuid(), 2)).Items);
        Assert.Empty((await repository.GetSourcePageAsync(seed.Subject, seed.Tenant, 3)).Items);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetSourcePageAsync(seed.Subject, seed.Tenant, int.MaxValue));

        var dev = await PersonaSignIn.SignInAsync(factory, Persona(seed.Tenant, seed.Subject, false));
        const string path = "/staffops/skills/examples";
        var html = await (await dev.GetAsync(path + "?sourcePage=2")).Content.ReadAsStringAsync();
        Assert.Contains("Contribution page 2", html);
        Assert.Contains(second.Items[0].EvidenceKey.ToString(), html);
        Assert.DoesNotContain("Older contributions</a>", html);
        Assert.Equal(HttpStatusCode.BadRequest, (await dev.GetAsync(path + "?sourcePage=2147483647")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await dev.GetAsync(path + "?sourcePage=abc")).StatusCode);
        var form = new Dictionary<string, string>
        {
            ["Form.SourcePage"] = "2", ["Form.AssertionKey"] = seed.Assertion.ToString(),
            ["Form.EvidenceKey"] = second.Items[0].EvidenceKey.ToString(),
            ["Form.DemonstrationNote"] = "My earlier verification work",
            ["Form.AiWorkflow"] = "Testing"
        };
        var invalid = await dev.PostWithTokenAsync(path + "?sourcePage=2", path, form);
        Assert.Equal(HttpStatusCode.BadRequest, invalid!.StatusCode);
        var errorHtml = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("Contribution page 2", errorHtml);
        Assert.Contains("My earlier verification work", errorHtml);
        Assert.Contains(second.Items[0].EvidenceKey.ToString(), errorHtml);
        form["Form.AiTool"] = "Declared tool";
        var submitted = await dev.PostWithTokenAsync(path + "?sourcePage=2", path, form);
        Assert.Equal(HttpStatusCode.Redirect, submitted!.StatusCode);
    }

    [Fact]
    public async Task Http_subject_submits_reviewer_decides_and_stale_form_returns_409()
    {
        if (!Ready()) return;
        var seed = await SeedAsync(members: true);
        var developer = Persona(seed.Tenant, seed.Subject, false);
        var reviewer = Persona(seed.Tenant, seed.Reviewer, true);
        var dev = await PersonaSignIn.SignInAsync(factory, developer);
        var lead = await PersonaSignIn.SignInAsync(factory, reviewer);
        var submitted = await dev.PostWithTokenAsync("/staffops/skills/examples", "/staffops/skills/examples", new Dictionary<string, string>
        {
            ["Form.AssertionKey"] = seed.Assertion.ToString(), ["Form.EvidenceKey"] = seed.Evidence.EvidenceKey.ToString(),
            ["Form.DemonstrationNote"] = "I tested <script>unsafe()</script> output", ["Form.AiTool"] = "Claude", ["Form.AiWorkflow"] = "Testing"
        });
        Assert.NotNull(submitted);
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        var path = submitted.Headers.Location!.OriginalString;
        var html = await (await dev.GetAsync(path)).Content.ReadAsStringAsync();
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>unsafe()", html);
        Assert.Contains("Unverified replay", html);
        var hidden = await SeedAsync();
        var foreignLink = await SubmitAsync(hidden);
        Assert.Equal(HttpStatusCode.NotFound, (await dev.GetAsync($"/staffops/skills/examples/{foreignLink}")).StatusCode);
        // The query string cannot choose a different tenant or subject.
        var ownPage = await (await dev.GetAsync($"/staffops/skills/examples?tenantId={hidden.Tenant}&staffKey={hidden.Subject}")).Content.ReadAsStringAsync();
        Assert.Contains(developer.FullName, ownPage);
        Assert.DoesNotContain(foreignLink.ToString(), ownPage);
        Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(await dev.GetAsync($"/staffops/skills/examples/staff/{seed.Reviewer}")));
        Assert.Equal(HttpStatusCode.BadRequest, (await dev.GetAsync("/staffops/skills/examples?page=-1")).StatusCode);
        var badForm = await dev.PostWithTokenAsync(path, path + "/revise", new Dictionary<string, string>
        {
            ["Form.Revision"] = "1", ["Form.DemonstrationNote"] = "Preserve this explanation", ["Form.AiWorkflow"] = "not-an-enum"
        });
        Assert.Equal(HttpStatusCode.BadRequest, badForm!.StatusCode);
        Assert.Contains("Preserve this explanation", await badForm.Content.ReadAsStringAsync());
        var decision = new Dictionary<string, string> { ["Decision.Revision"] = "1", ["Decision.Status"] = "Accepted", ["Decision.Note"] = "Relevant verification" };
        var refused = await dev.PostWithTokenAsync(path, path + "/decide", decision);
        Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(refused!));
        var accepted = await lead.PostWithTokenAsync(path, path + "/decide", decision);
        Assert.Equal(HttpStatusCode.Redirect, accepted!.StatusCode);
        // Use another form with a token: the accepted detail intentionally has no decision form.
        var stale = await lead.PostWithTokenAsync("/staffops/skills", path + "/decide", decision);
        Assert.Equal(HttpStatusCode.Conflict, stale!.StatusCode);
        var ownMutation = await lead.PostWithTokenAsync("/staffops/skills", path + "/withdraw", new Dictionary<string, string> { ["revision"] = "2" });
        Assert.Equal(HttpStatusCode.NotFound, ownMutation!.StatusCode);
        var withoutToken = await dev.Client.PostAsync(path + "/withdraw", new FormUrlEncodedContent(new Dictionary<string, string> { ["revision"] = "2" }));
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        var tenant = await Get<ITenantRepository>().GetByKeyAsync(seed.Tenant);
        await Get<ITenantRepository>().UpdateAsync(tenant! with { Plan = TenantPlan.Starter });
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync(path)).StatusCode);
    }

    private async Task<Guid> SubmitAsync(Seed seed) => await Get<ContributionReviewService>().SubmitAsync(seed.Subject, seed.Tenant,
        seed.Assertion, seed.Evidence.EvidenceKey, "I checked the generated tests and corrected their assumptions.", "Claude", AiAssistanceWorkflow.Testing);

    private sealed record Seed(Guid Tenant, Guid Subject, Guid Reviewer, Guid Assertion, EngineeringEvidence Evidence, Guid SubjectLink);

    private static PersonaDefinition Persona(Guid tenant, Guid staff, bool reviewer) =>
        (reviewer ? NorthstarPersonas.ProjectManager : NorthstarPersonas.Developer) with
        { Key = staff.ToString("N"), StaffKey = staff, TenantKey = tenant, Email = $"{staff:N}@example.test" };

    private async Task<Seed> SeedAsync(bool members = false)
    {
        var now = DateTime.UtcNow;
        var tenant = Guid.NewGuid();
        await Get<ITenantRepository>().CreateAsync(new Tenant { TenantKey = tenant, Name = "Contribution test", ShortCode = tenant.ToString("N"),
            IsActive = true, Plan = TenantPlan.Enterprise, CreatedAtUtc = now });
        // Evidence writes refuse a tenant with no current lawful basis, notice and DPIA decision.
        await EvidenceSqlSetup.PermitAsync(factory, tenant);
        var subject = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        if (members)
            await new NorthstarPersonaSeeder(factory.Services).SeedWithEveryModuleOnAsync([Persona(tenant, subject, false), Persona(tenant, reviewer, true)]);
        else
            foreach (var key in new[] { subject, reviewer })
                await Get<IStaffRepository>().CreateAsync(new StaffProfile { StaffKey = key, TenantId = tenant, MemberId = Random.Shared.Next(-2000000000, -1000000),
                    FullName = "Contribution subject", Email = $"{key:N}@example.test", CreatedAtUtc = now, UpdatedAtUtc = now });
        var skillKey = "contribution-" + Guid.NewGuid().ToString("N");
        await Get<ISkillsEvidenceRepository>().CreateSkillAsync(new SkillDefinition { SkillDefinitionKey = Guid.NewGuid(), TenantId = tenant,
            SkillKey = skillKey, Name = "AI output verification", Kind = SkillKind.Practice, TaxonomyVersion = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        var assertion = await Get<ISkillAssertionService>().DeclareAsync(subject, skillKey, ProficiencyLevel.Working, "Learning verification", tenant, null);
        var connection = await Get<IEngineeringEvidenceRepository>().UpsertConnectionAsync(new EvidenceConnection { ConnectionKey = Guid.NewGuid(), TenantId = tenant,
            Provider = "GitHub", SourceAccountId = tenant.ToString("N"), DisplayName = "Fixture", ApiBaseUrl = "https://api.github.com",
            SelectedRepositories = ["example/repo"], Status = EvidenceConnectionStatus.Active, CreatedAtUtc = now, UpdatedAtUtc = now });
        // A person is attributed only through an approved actor link, never by the row's own StaffKey.
        var subjectLink = await Get<IEngineeringEvidenceRepository>().CreateActorLinkAsync(new EvidenceActorLink { LinkKey = Guid.NewGuid(), TenantId = tenant,
            ConnectionKey = connection.ConnectionKey, Provider = "GitHub", ExternalActorId = "test-actor", StaffKey = subject, ApprovedAtUtc = now });
        await Get<IEngineeringEvidenceRepository>().CreateActorLinkAsync(new EvidenceActorLink { LinkKey = Guid.NewGuid(), TenantId = tenant,
            ConnectionKey = connection.ConnectionKey, Provider = "GitHub", ExternalActorId = "reviewer-actor", StaffKey = reviewer, ApprovedAtUtc = now });
        var evidence = await Get<IEngineeringEvidenceRepository>().UpsertEvidenceAsync(new EngineeringEvidence { EvidenceKey = Guid.NewGuid(), TenantId = tenant,
            ConnectionKey = connection.ConnectionKey, Provider = "GitHub", SourceAccountId = connection.SourceAccountId, RepositoryKey = "example/repo",
            SourceType = EvidenceSourceType.Commit, ExternalId = "test-sha", Role = EvidenceRole.CommitAuthor, ActorExternalId = "test-actor",
            ActorIsBot = false, StaffKey = subject, AttributionStatus = EvidenceAttributionStatus.Mapped, Title = "Test contribution",
            SourceUrl = "https://github.com/example/repo/commit/test-sha", OccurredAtUtc = now, SchemaVersion = 1, FirstIngestedAtUtc = now, UpdatedAtUtc = now });
        return new(tenant, subject, reviewer, assertion.AssertionKey, evidence, subjectLink.LinkKey);
    }
}
