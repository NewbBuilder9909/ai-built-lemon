using System.Reflection;
using ProgrammePulse.Models.SkillsEvidence;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// The aggregate view. Two things are being defended here: that the numbers
/// are honest about what they exclude, and that no person can reach this
/// answer — the coverage page is readable with a wider grant than the
/// person-level one.
/// </summary>
public class SkillCoverageQueryServiceTests
{
    private const string Billing = "billing";

    [Fact]
    public async Task Only_validated_in_date_assertions_at_or_above_the_threshold_count_as_cover()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Billing, SkillsEvidenceTestContext.TenantA, SkillKind.Component);

        // Alex: validated Practitioner — counts.
        await ctx.ValidatedAsync(ctx.Alex, Billing, ProficiencyLevel.Practitioner, ctx.Sarah, SkillsEvidenceTestContext.TenantA);
        // Nia: validated, but only Working — below the cover threshold.
        await ctx.ValidatedAsync(ctx.Nia, Billing, ProficiencyLevel.Working, ctx.Sarah, SkillsEvidenceTestContext.TenantA);
        // Sarah: claims Lead but nobody has reviewed it — a claim, not cover.
        await ctx.Service.DeclareAsync(
            ctx.Sarah.StaffKey, Billing, ProficiencyLevel.Lead, null, SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId);

        var report = await ctx.Coverage.BuildAsync(SkillsEvidenceTestContext.TenantA);
        var row = Assert.Single(report.Rows);

        Assert.Equal(1, row.ValidatedCover);
        Assert.Equal(2, row.ValidatedAtAnyLevel);
        Assert.Equal(1, row.AwaitingReview);
        Assert.True(row.IsSingleMaintainerRisk);
    }

    [Fact]
    public async Task A_validated_level_past_its_review_date_stops_counting_as_cover_and_is_reported_as_overdue()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Billing, SkillsEvidenceTestContext.TenantA, SkillKind.Component);
        await ctx.ValidatedAsync(ctx.Alex, Billing, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        var before = await ctx.Coverage.BuildAsync(SkillsEvidenceTestContext.TenantA);
        Assert.Equal(1, before.Rows[0].ValidatedCover);
        Assert.Equal(0, before.Rows[0].ReviewOverdue);

        // Past the 12-month rubric interval.
        ctx.Time.Advance(TimeSpan.FromDays(400));

        var after = await ctx.Coverage.BuildAsync(SkillsEvidenceTestContext.TenantA);
        Assert.Equal(0, after.Rows[0].ValidatedCover);
        Assert.Equal(1, after.Rows[0].ReviewOverdue);
        Assert.True(after.Rows[0].HasNoValidatedCover);
    }

    [Fact]
    public async Task A_challenged_assertion_does_not_count_as_cover_while_it_is_contested()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Billing, SkillsEvidenceTestContext.TenantA, SkillKind.Component);

        var validated = await ctx.ValidatedAsync(
            ctx.Alex, Billing, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);
        await ctx.Service.ChallengeAsync(
            validated.AssertionKey, ctx.Alex.StaffKey, ProficiencyLevel.Practitioner, "the level is wrong",
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);

        var report = await ctx.Coverage.BuildAsync(SkillsEvidenceTestContext.TenantA);

        Assert.Equal(0, report.Rows[0].ValidatedCover);
        Assert.Equal(1, report.Rows[0].Challenged);
    }

    [Fact]
    public async Task A_deactivated_person_stops_counting_as_cover()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Billing, SkillsEvidenceTestContext.TenantA, SkillKind.Component);
        await ctx.ValidatedAsync(ctx.Alex, Billing, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        // A leaver's validated skill is exactly the false reassurance this
        // view exists to prevent.
        ctx.StaffRepository.Staff[ctx.StaffRepository.Staff.IndexOf(ctx.Alex)] = ctx.Alex with { IsActive = false };

        var report = await ctx.Coverage.BuildAsync(SkillsEvidenceTestContext.TenantA);

        Assert.Equal(0, report.Rows[0].ValidatedCover);
        Assert.True(report.Rows[0].HasNoValidatedCover);
    }

    [Fact]
    public async Task Coverage_never_includes_another_tenants_people_or_skills()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Billing, SkillsEvidenceTestContext.TenantA, SkillKind.Component);
        await ctx.AddSkillAsync("their-secret-component", SkillsEvidenceTestContext.TenantB, SkillKind.Component);

        await ctx.ValidatedAsync(ctx.Alex, Billing, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);
        var reviewerB = ctx.AddStaff("Gareth Ellis", 202, SkillsEvidenceTestContext.TenantB);
        await ctx.ValidatedAsync(ctx.Rhian, "their-secret-component", ProficiencyLevel.Lead, reviewerB, SkillsEvidenceTestContext.TenantB);

        var report = await ctx.Coverage.BuildAsync(SkillsEvidenceTestContext.TenantA);

        Assert.Single(report.Rows);
        Assert.Equal(Billing, report.Rows[0].SkillKey);
        // Three active people in tenant A; tenant B's two are not in the denominator.
        Assert.Equal(3, report.StaffInScope);
    }

    [Fact]
    public async Task The_report_says_how_many_people_have_declared_nothing()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Billing, SkillsEvidenceTestContext.TenantA, SkillKind.Component);
        await ctx.ValidatedAsync(ctx.Alex, Billing, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        var report = await ctx.Coverage.BuildAsync(SkillsEvidenceTestContext.TenantA);

        Assert.Equal(3, report.StaffInScope);
        // Sarah reviewed but declared nothing of her own; Nia said nothing at all.
        Assert.Equal(2, report.StaffWithNoAssertions);
    }

    [Fact]
    public async Task A_withdrawn_assertion_puts_the_person_back_in_the_silent_count()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Billing, SkillsEvidenceTestContext.TenantA, SkillKind.Component);

        var validated = await ctx.ValidatedAsync(ctx.Alex, Billing, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);
        await ctx.Service.WithdrawAsync(validated.AssertionKey, ctx.Alex.StaffKey, SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);

        var report = await ctx.Coverage.BuildAsync(SkillsEvidenceTestContext.TenantA);

        Assert.Equal(0, report.Rows[0].ValidatedCover);
        Assert.Equal(3, report.StaffWithNoAssertions);
    }

    [Fact]
    public async Task A_retired_skill_drops_out_of_coverage()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Billing, SkillsEvidenceTestContext.TenantA, SkillKind.Component);
        await ctx.ValidatedAsync(ctx.Alex, Billing, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        await ctx.Service.SetSkillActiveAsync(Billing, false, SkillsEvidenceTestContext.TenantA, 1);

        var report = await ctx.Coverage.BuildAsync(SkillsEvidenceTestContext.TenantA);
        Assert.Empty(report.Rows);
    }

    [Fact]
    public async Task Two_reviewed_people_is_not_a_single_maintainer_risk()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Billing, SkillsEvidenceTestContext.TenantA, SkillKind.Component);
        await ctx.ValidatedAsync(ctx.Alex, Billing, ProficiencyLevel.Practitioner, ctx.Sarah, SkillsEvidenceTestContext.TenantA);
        await ctx.ValidatedAsync(ctx.Nia, Billing, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        var report = await ctx.Coverage.BuildAsync(SkillsEvidenceTestContext.TenantA);

        Assert.Equal(2, report.Rows[0].ValidatedCover);
        Assert.False(report.Rows[0].IsSingleMaintainerRisk);
        Assert.Equal(0, report.SingleMaintainerSkills);
    }

    /// <summary>
    /// The structural guard. ViewTeamSkillCoverage is a wider grant than
    /// ViewStaffSkillEvidence, so the coverage type must have nowhere to
    /// put a person — the same reasoning as ProgrammeOverviewViewModel
    /// carrying no cost field (CLAUDE.md). Adding a StaffKey or a name here
    /// fails the build rather than quietly widening who can see who is
    /// behind a number.
    /// </summary>
    [Fact]
    public void The_coverage_row_has_nowhere_to_put_a_person()
    {
        string[] forbidden = ["staff", "person", "member", "holder", "maintainer", "employee", "email", "user"];

        var offenders = typeof(SkillCoverageRow).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => forbidden.Any(f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            // The counts are fine — "how many", never "who".
            .Where(p => p.PropertyType != typeof(int) && p.PropertyType != typeof(bool))
            .Select(p => p.Name)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"SkillCoverageRow gained a person-shaped member: {string.Join(", ", offenders)}. "
            + "The aggregate view is readable with a wider capability than the person-level one — "
            + "put it on StaffSkillPortfolioViewModel instead.");

        Assert.DoesNotContain(
            typeof(SkillCoverageRow).GetProperties(),
            p => p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?));
    }

    /// <summary>
    /// Cost and rate data must never be fetched on a path a non-Admin can
    /// reach (CLAUDE.md). Team Lead holds ViewTeamSkillCoverage, so this
    /// service must not be able to ask for a rate at all.
    /// </summary>
    [Fact]
    public void The_coverage_service_takes_no_dependency_that_can_reach_a_rate()
    {
        var dependencies = typeof(ProgrammePulse.Services.SkillsEvidence.SkillCoverageQueryService)
            .GetConstructors().Single()
            .GetParameters()
            .Select(p => p.ParameterType.Name)
            .ToArray();

        Assert.DoesNotContain(dependencies, name => name.Contains("Rate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(dependencies, name => name.Contains("Contract", StringComparison.OrdinalIgnoreCase));
    }
}
