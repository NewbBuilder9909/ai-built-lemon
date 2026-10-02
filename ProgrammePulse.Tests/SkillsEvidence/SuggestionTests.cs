using System.Reflection;
using ProgrammePulse.Models.Integrations.GitHub.Raw;
using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using static ProgrammePulse.Tests.SkillsEvidence.FakeGitHubEvidenceClient;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// The suggestion layer.
///
/// Almost every test here is about a **ceiling** rather than a feature:
/// what accepting a proposal is allowed to create. The design document's
/// fifth phase permits suggestions with confidence and citations that
/// require human acceptance, and explicitly forbids them becoming
/// assertions or causal links on their own. The tests that matter are
/// the ones proving the strongest thing a suggestion can produce is
/// still the weakest thing the domain can express.
/// </summary>
public class SuggestionTests
{
    private sealed class Context
    {
        public EvidenceTestContext Evidence { get; } = new();
        public FakeSuggestionRepository Suggestions { get; } = new();
        public FakeContinuityRepository Continuity { get; }
        public SkillAssertionService Assertions { get; }
        public ContinuityService ContinuityService { get; }
        public SuggestionService Service { get; }

        public Context()
        {
            Continuity = Evidence.Continuity;
            Assertions = new SkillAssertionService(Evidence.SkillsRepository, Evidence.AuditLog, Evidence.Time);
            ContinuityService = new ContinuityService(Continuity, Evidence.AuditLog, Evidence.Time);

            Service = new SuggestionService(
                Suggestions, Evidence.Repository, Evidence.SkillsRepository, Continuity,
                Assertions, ContinuityService, Evidence.AuditLog, Evidence.StaffRepository, Evidence.Time);
        }

        public Guid Tenant => EvidenceTestContext.TenantA;

        /// <summary>Adds a tracked component-kind skill so suggestions have something to propose.</summary>
        public async Task TrackSkillAsync(string key) =>
            await Assertions.CreateSkillAsync(key, key, SkillKind.Language, null, Tenant, 1);

        /// <summary>Ingests n commits touching files of the given language, attributed to Alex.</summary>
        public async Task WithAttributedCommitsAsync(int count, params string[] paths)
        {
            var commits = Enumerable.Range(1, count)
                .Select(i => Commit($"sha{i}", Human("1", "alex"), paths: paths))
                .ToList();

            Evidence.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(commits, null, true));
            await Evidence.RunAsync(Evidence.ConnectionA);
            await Evidence.ApproveAsync(Evidence.ConnectionA, "1", Evidence.Alex, "alex");
        }
    }

    private static readonly string[] CsharpFiles = ["src/A.cs", "src/B.cs"];

    // ---- Generation ----

    [Fact]
    public async Task Repeated_activity_in_a_tracked_language_proposes_a_skill_tag()
    {
        var ctx = new Context();
        await ctx.TrackSkillAsync("csharp");
        await ctx.WithAttributedCommitsAsync(4, CsharpFiles);

        var result = await ctx.Service.GenerateAsync(ctx.Tenant, 1);

        Assert.Equal(1, result.SkillTags);
        var suggestion = ctx.Suggestions.Suggestions.Single(s => s.Kind == SuggestionKind.SkillTag);
        Assert.Equal("csharp", suggestion.SubjectKey);
        Assert.Equal(ctx.Evidence.Alex.StaffKey, suggestion.SubjectStaffKey);
        Assert.True(suggestion.IsOpen);
        // The rationale has to be something a person can disagree with.
        Assert.Contains("participation, not proficiency", suggestion.Rationale, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(suggestion.Citations);
    }

    [Fact]
    public async Task Activity_below_the_threshold_proposes_nothing()
    {
        var ctx = new Context();
        await ctx.TrackSkillAsync("csharp");
        await ctx.WithAttributedCommitsAsync(SuggestionThresholds.MinimumArtefactsForSkillTag - 1, CsharpFiles);

        Assert.Equal(0, (await ctx.Service.GenerateAsync(ctx.Tenant, 1)).SkillTags);
        Assert.DoesNotContain(ctx.Suggestions.Suggestions, s => s.Kind == SuggestionKind.SkillTag);
    }

    [Theory]
    [InlineData(3, SuggestionConfidence.Weak)]
    [InlineData(8, SuggestionConfidence.Moderate)]
    [InlineData(20, SuggestionConfidence.Strong)]
    public void The_confidence_band_is_a_stated_threshold_not_a_model(int count, SuggestionConfidence expected) =>
        Assert.Equal(expected, SuggestionThresholds.BandFor(count));

    [Fact]
    public async Task A_language_the_tenant_does_not_track_is_not_proposed()
    {
        var ctx = new Context();
        // No skill definition for csharp — proposing it would offer
        // something the accept path could not create.
        await ctx.WithAttributedCommitsAsync(6, CsharpFiles);

        Assert.Equal(0, (await ctx.Service.GenerateAsync(ctx.Tenant, 1)).SkillTags);
    }

    [Fact]
    public async Task Unattributed_evidence_proposes_nothing_about_anyone()
    {
        var ctx = new Context();
        await ctx.TrackSkillAsync("csharp");

        // Ingested but never mapped to a person.
        ctx.Evidence.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("s1", Human("9", "contractor"), paths: CsharpFiles),
             Commit("s2", Human("9", "contractor"), paths: CsharpFiles),
             Commit("s3", Human("9", "contractor"), paths: CsharpFiles)], null, true));
        await ctx.Evidence.RunAsync(ctx.Evidence.ConnectionA);

        Assert.Equal(0, (await ctx.Service.GenerateAsync(ctx.Tenant, 1)).SkillTags);
    }

    [Fact]
    public async Task A_skill_the_person_has_already_spoken_about_is_not_proposed()
    {
        var ctx = new Context();
        await ctx.TrackSkillAsync("csharp");
        await ctx.WithAttributedCommitsAsync(6, CsharpFiles);

        await ctx.Assertions.DeclareAsync(
            ctx.Evidence.Alex.StaffKey, "csharp", ProficiencyLevel.Working, null, ctx.Tenant, 1);

        // A suggestion that could only ever be refused is noise.
        Assert.Equal(0, (await ctx.Service.GenerateAsync(ctx.Tenant, 1)).SkillTags);
    }

    [Fact]
    public async Task Regenerating_does_not_stack_duplicates()
    {
        var ctx = new Context();
        await ctx.TrackSkillAsync("csharp");
        await ctx.WithAttributedCommitsAsync(4, CsharpFiles);

        await ctx.Service.GenerateAsync(ctx.Tenant, 1);
        var second = await ctx.Service.GenerateAsync(ctx.Tenant, 1);

        Assert.Equal(0, second.SkillTags);
        Assert.Single(ctx.Suggestions.Suggestions, s => s.Kind == SuggestionKind.SkillTag);
    }

    [Fact]
    public async Task A_dismissed_suggestion_is_never_raised_again()
    {
        var ctx = new Context();
        await ctx.TrackSkillAsync("csharp");
        await ctx.WithAttributedCommitsAsync(4, CsharpFiles);
        await ctx.Service.GenerateAsync(ctx.Tenant, 1);

        var suggestion = ctx.Suggestions.Suggestions.Single();
        await ctx.Service.DismissAsync(suggestion.SuggestionKey, ctx.Evidence.Sarah.StaffKey, "not really their area", ctx.Tenant, 1);

        await ctx.Service.GenerateAsync(ctx.Tenant, 1);

        // An engine that nags is one people learn to ignore, and an
        // ignored queue hides the useful entries.
        Assert.Single(ctx.Suggestions.Suggestions);
        Assert.Equal(SuggestionOutcome.Dismissed, ctx.Suggestions.Suggestions[0].Outcome);
    }

    [Fact]
    public async Task A_component_with_an_owner_and_no_cover_proposes_a_coverage_action()
    {
        var ctx = new Context();
        await ctx.ContinuityService.DeclareComponentAsync(
            "billing", "Billing", null, ctx.Evidence.Alex.StaffKey, ctx.Evidence.Sarah.StaffKey, ctx.Tenant, 1);

        var result = await ctx.Service.GenerateAsync(ctx.Tenant, 1);

        Assert.Equal(1, result.CoverageGaps);
        var suggestion = ctx.Suggestions.Suggestions.Single(s => s.Kind == SuggestionKind.CoverageGap);
        Assert.Null(suggestion.SubjectStaffKey);
        Assert.Contains("not a judgement about the owner", suggestion.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_component_that_already_has_an_open_action_is_not_proposed_again()
    {
        var ctx = new Context();
        await ctx.ContinuityService.DeclareComponentAsync(
            "billing", "Billing", null, ctx.Evidence.Alex.StaffKey, ctx.Evidence.Sarah.StaffKey, ctx.Tenant, 1);
        await ctx.ContinuityService.RaiseActionAsync(
            "billing", CoverageActionType.Pair, ctx.Evidence.Sarah.StaffKey, "already on it",
            null, null, ctx.Evidence.Sarah.StaffKey, ctx.Tenant, 1);

        Assert.Equal(0, (await ctx.Service.GenerateAsync(ctx.Tenant, 1)).CoverageGaps);
    }

    [Fact]
    public async Task An_unowned_component_proposes_no_cover_gap()
    {
        var ctx = new Context();
        // Nobody accountable is a different, larger finding — the
        // coverage view shouts about it — and proposing a backup for a
        // component with no owner is the wrong order.
        await ctx.ContinuityService.DeclareComponentAsync(
            "billing", "Billing", null, null, ctx.Evidence.Sarah.StaffKey, ctx.Tenant, 1);

        Assert.Equal(0, (await ctx.Service.GenerateAsync(ctx.Tenant, 1)).CoverageGaps);
    }

    // ---- The ceiling on acceptance ----

    [Fact]
    public async Task Accepting_a_skill_tag_creates_the_weakest_thing_the_domain_can_express()
    {
        var ctx = new Context();
        await ctx.TrackSkillAsync("csharp");
        await ctx.WithAttributedCommitsAsync(20, CsharpFiles);
        await ctx.Service.GenerateAsync(ctx.Tenant, 1);

        var suggestion = ctx.Suggestions.Suggestions.Single(s => s.Kind == SuggestionKind.SkillTag);
        // Strong confidence, twenty artefacts — and still only this.
        Assert.Equal(SuggestionConfidence.Strong, suggestion.Confidence);

        await ctx.Service.AcceptAsync(suggestion.SuggestionKey, ctx.Evidence.Sarah.StaffKey, null, ctx.Tenant, 1);

        var assertion = ctx.Evidence.SkillsRepository.Assertions.Single(a => a.IsCurrent);
        Assert.Equal(ProficiencyLevel.Awareness, assertion.Proficiency);
        Assert.Equal(AssertionStatus.Submitted, assertion.Status);
        Assert.Equal(AssertionOrigin.SelfDeclared, assertion.Origin);
        // Unreviewed, so it counts as a claim and not as cover.
        Assert.Null(assertion.ReviewerStaffKey);
        Assert.Null(assertion.ReviewDueOn);
    }

    [Fact]
    public async Task Accepting_can_never_raise_an_existing_level()
    {
        var ctx = new Context();
        await ctx.TrackSkillAsync("csharp");
        await ctx.WithAttributedCommitsAsync(20, CsharpFiles);
        await ctx.Service.GenerateAsync(ctx.Tenant, 1);
        var suggestion = ctx.Suggestions.Suggestions.Single();

        // Somebody records a level between the suggestion and the accept.
        await ctx.Assertions.DeclareAsync(
            ctx.Evidence.Alex.StaffKey, "csharp", ProficiencyLevel.Working, null, ctx.Tenant, 1);

        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.AcceptAsync(
            suggestion.SuggestionKey, ctx.Evidence.Sarah.StaffKey, null, ctx.Tenant, 1));

        Assert.Contains("never change a level", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProficiencyLevel.Working, ctx.Evidence.SkillsRepository.Assertions.Single(a => a.IsCurrent).Proficiency);
    }

    [Fact]
    public async Task Accepting_a_coverage_gap_raises_an_action_with_a_chosen_owner()
    {
        var ctx = new Context();
        await ctx.ContinuityService.DeclareComponentAsync(
            "billing", "Billing", null, ctx.Evidence.Alex.StaffKey, ctx.Evidence.Sarah.StaffKey, ctx.Tenant, 1);
        await ctx.Service.GenerateAsync(ctx.Tenant, 1);
        var suggestion = ctx.Suggestions.Suggestions.Single(s => s.Kind == SuggestionKind.CoverageGap);

        await ctx.Service.AcceptAsync(
            suggestion.SuggestionKey, ctx.Evidence.Sarah.StaffKey, ctx.Evidence.Sarah.StaffKey, ctx.Tenant, 1);

        var action = ctx.Continuity.Actions.Single();
        Assert.Equal(CoverageActionType.NominateBackup, action.Type);
        Assert.Equal(ctx.Evidence.Sarah.StaffKey, action.OwnerStaffKey);
        Assert.True(action.IsOpen);
        // Accepting raises an action; it does not nominate anybody as
        // cover. A person still does that.
        Assert.Empty(ctx.Continuity.Backups);
    }

    [Fact]
    public async Task Accepting_a_coverage_gap_without_an_owner_is_refused()
    {
        var ctx = new Context();
        await ctx.ContinuityService.DeclareComponentAsync(
            "billing", "Billing", null, ctx.Evidence.Alex.StaffKey, ctx.Evidence.Sarah.StaffKey, ctx.Tenant, 1);
        await ctx.Service.GenerateAsync(ctx.Tenant, 1);
        var suggestion = ctx.Suggestions.Suggestions.Single(s => s.Kind == SuggestionKind.CoverageGap);

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.AcceptAsync(
            suggestion.SuggestionKey, ctx.Evidence.Sarah.StaffKey, null, ctx.Tenant, 1));
    }

    [Fact]
    public async Task A_suggestion_cannot_be_decided_twice()
    {
        var ctx = new Context();
        await ctx.TrackSkillAsync("csharp");
        await ctx.WithAttributedCommitsAsync(4, CsharpFiles);
        await ctx.Service.GenerateAsync(ctx.Tenant, 1);
        var suggestion = ctx.Suggestions.Suggestions.Single();

        await ctx.Service.DismissAsync(suggestion.SuggestionKey, ctx.Evidence.Sarah.StaffKey, null, ctx.Tenant, 1);

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.AcceptAsync(
            suggestion.SuggestionKey, ctx.Evidence.Sarah.StaffKey, null, ctx.Tenant, 1));
    }

    [Fact]
    public async Task Another_tenants_suggestion_reads_as_not_found()
    {
        var ctx = new Context();
        await ctx.TrackSkillAsync("csharp");
        await ctx.WithAttributedCommitsAsync(4, CsharpFiles);
        await ctx.Service.GenerateAsync(ctx.Tenant, 1);
        var suggestion = ctx.Suggestions.Suggestions.Single();

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => ctx.Service.AcceptAsync(
            suggestion.SuggestionKey, ctx.Evidence.Rhian.StaffKey, null, EvidenceTestContext.TenantB, 1));
    }

    [Fact]
    public async Task Every_decision_is_audited_with_who_made_it()
    {
        var ctx = new Context();
        await ctx.TrackSkillAsync("csharp");
        await ctx.WithAttributedCommitsAsync(4, CsharpFiles);
        await ctx.Service.GenerateAsync(ctx.Tenant, 7);
        var suggestion = ctx.Suggestions.Suggestions.Single();

        await ctx.Service.AcceptAsync(suggestion.SuggestionKey, ctx.Evidence.Sarah.StaffKey, null, ctx.Tenant, 7);

        Assert.Contains(ctx.Evidence.AuditLog.Entries, e => e.Action == SkillsEvidenceAuditAction.SuggestionsGenerated);
        var accepted = ctx.Evidence.AuditLog.Entries.Single(e => e.Action == SkillsEvidenceAuditAction.SuggestionAccepted);
        Assert.Equal(7, accepted.ActorMemberId);
        Assert.Contains(ctx.Evidence.Sarah.StaffKey.ToString(), accepted.DetailJson!, StringComparison.Ordinal);
    }

    // ---- Structural ----

    /// <summary>
    /// The prohibition that matters most: there is no kind of suggestion
    /// that produces a causal claim, and no path from the service to one.
    /// </summary>
    [Fact]
    public void No_suggestion_can_produce_a_confirmed_root_cause()
    {
        var dependencies = typeof(SuggestionService).GetConstructors().Single()
            .GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.DoesNotContain(dependencies, t => t.Name.Contains("SupportCodeLink", StringComparison.Ordinal));
        Assert.DoesNotContain(dependencies, t => t.Name.Contains("ServiceOps", StringComparison.Ordinal));

        // And the enum offers no value that would mean "confirmed".
        foreach (var name in Enum.GetNames<SuggestionKind>())
        {
            Assert.DoesNotContain("cause", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("confirm", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_suggestion_exposes_no_score_or_probability()
    {
        // Confidence is a band. A percentage would imply a calibrated
        // model, and these are threshold counts.
        string[] forbidden = ["score", "probability", "percent", "likelihood", "weight", "rank"];

        var offenders = typeof(Suggestion)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => forbidden.Any(f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"Suggestion gained a score-shaped member: {string.Join(", ", offenders)}.");

        Assert.Equal(typeof(SuggestionConfidence), typeof(Suggestion).GetProperty("Confidence")!.PropertyType);
    }

    [Fact]
    public async Task Suggestions_about_a_person_are_exported_and_erased_with_them()
    {
        var ctx = new Context();
        await ctx.TrackSkillAsync("csharp");
        await ctx.WithAttributedCommitsAsync(4, CsharpFiles);
        await ctx.Service.GenerateAsync(ctx.Tenant, 1);

        var participant = new SkillsEvidenceDataParticipant(
            ctx.Evidence.SkillsRepository, ctx.Evidence.Repository, ctx.Suggestions,
            ctx.Evidence.AuditLog, ctx.Evidence.StaffRepository);

        var exported = await participant.ExportAsync(ctx.Evidence.Alex.StaffKey);
        // A dismissed or open proposal still records that the system
        // inferred something about them.
        Assert.Contains(exported, r => r.Summary.Contains("Suggested 'csharp'", StringComparison.Ordinal));

        await participant.EraseAsync(ctx.Evidence.Alex.StaffKey, ctx.Evidence.Time.Now.UtcDateTime);

        Assert.DoesNotContain(ctx.Suggestions.Suggestions, s => s.SubjectStaffKey == ctx.Evidence.Alex.StaffKey);
    }

    [Fact]
    public async Task A_component_suggestion_survives_an_erasure_because_it_is_about_no_one()
    {
        var ctx = new Context();
        await ctx.ContinuityService.DeclareComponentAsync(
            "billing", "Billing", null, ctx.Evidence.Alex.StaffKey, ctx.Evidence.Sarah.StaffKey, ctx.Tenant, 1);
        await ctx.Service.GenerateAsync(ctx.Tenant, 1);

        var participant = new SkillsEvidenceDataParticipant(
            ctx.Evidence.SkillsRepository, ctx.Evidence.Repository, ctx.Suggestions,
            ctx.Evidence.AuditLog, ctx.Evidence.StaffRepository);

        await participant.EraseAsync(ctx.Evidence.Alex.StaffKey, ctx.Evidence.Time.Now.UtcDateTime);

        // Same rule as everywhere else: a record of the organisation's
        // exposure is not a claim about the person who happened to own it.
        Assert.Single(ctx.Suggestions.Suggestions, s => s.Kind == SuggestionKind.CoverageGap);
    }
}
