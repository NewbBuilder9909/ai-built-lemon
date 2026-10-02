using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// The key rules and the rubric. Both are pure, and both are the sort of
/// thing that looks obvious until two spellings of the same skill split a
/// coverage count in half.
/// </summary>
public class SkillTaxonomyTests
{
    [Theory]
    [InlineData("C Sharp", "c-sharp")]
    [InlineData("  ASP.NET Core  ", "asp-net-core")]
    [InlineData("incident_command", "incident-command")]
    [InlineData("Umbraco", "umbraco")]
    [InlineData("SQL/T-SQL", "sql-t-sql")]
    [InlineData("C++", "c")]
    public void Keys_fold_to_one_canonical_spelling(string raw, string expected) =>
        Assert.Equal(expected, SkillTaxonomy.NormalizeKey(raw));

    [Theory]
    [InlineData("a  b", "a-b")]
    [InlineData("--messy--", "messy")]
    [InlineData("Déjà Vu", "dj-vu")]
    public void Separator_runs_and_unmappable_characters_never_produce_a_ragged_key(string raw, string expected) =>
        Assert.Equal(expected, SkillTaxonomy.NormalizeKey(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Input_that_reduces_to_nothing_is_not_a_valid_key(string? raw) =>
        Assert.False(SkillTaxonomy.IsValidKey(SkillTaxonomy.NormalizeKey(raw)));

    [Fact]
    public void A_normalized_key_is_always_valid_or_empty()
    {
        // Property-ish check across a spread of awkward inputs: normalising
        // must never produce something the validator then rejects as
        // malformed, or the create form becomes unusable for valid names.
        string[] inputs = ["C#", "F#", ".NET", "node.js", "Zoho Desk", "  ", "a", "A-B-C", new('x', 400)];

        foreach (var input in inputs)
        {
            var key = SkillTaxonomy.NormalizeKey(input);
            if (key.Length == 0)
            {
                continue;
            }

            Assert.DoesNotContain("--", key, StringComparison.Ordinal);
            Assert.False(key.StartsWith('-') || key.EndsWith('-'));
            Assert.Equal(key.ToLowerInvariant(), key);
        }
    }

    [Fact]
    public void An_over_long_key_is_rejected_rather_than_truncated() =>
        Assert.False(SkillTaxonomy.IsValidKey(new string('a', SkillTaxonomy.MaxKeyLength + 1)));

    [Fact]
    public void The_rubric_describes_every_proficiency_level()
    {
        foreach (var level in Enum.GetValues<ProficiencyLevel>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ProficiencyRubric.Describe(level)));
            Assert.Contains(level, ProficiencyRubric.All);
        }

        Assert.Equal(Enum.GetValues<ProficiencyLevel>().Length, ProficiencyRubric.All.Count);
    }

    [Fact]
    public void Proficiency_levels_are_ordered_so_at_or_above_comparisons_mean_something()
    {
        Assert.True(ProficiencyLevel.Awareness < ProficiencyLevel.Working);
        Assert.True(ProficiencyLevel.Working < ProficiencyLevel.Practitioner);
        Assert.True(ProficiencyLevel.Practitioner < ProficiencyLevel.Lead);
        Assert.True(ProficiencyRubric.CoverThreshold >= ProficiencyLevel.Working);
    }

    [Fact]
    public void The_default_review_date_is_the_stated_interval_ahead()
    {
        var validatedAt = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            new DateOnly(2027, 9, 20),
            ProficiencyRubric.DefaultReviewDue(validatedAt));
    }

    [Fact]
    public async Task Creating_a_skill_stamps_the_taxonomy_version_in_force()
    {
        var ctx = new SkillsEvidenceTestContext();

        var skill = await ctx.Service.CreateSkillAsync(
            "", "Incident command", SkillKind.Practice, "Running a live incident", SkillsEvidenceTestContext.TenantA, 1);

        Assert.Equal(SkillTaxonomy.CurrentVersion, skill.TaxonomyVersion);
        Assert.Equal("incident-command", skill.SkillKey);   // derived from the name
        Assert.Equal("Incident command", skill.Name);
        Assert.True(skill.IsActive);
    }

    [Fact]
    public async Task A_name_that_reduces_to_no_usable_key_is_refused_with_an_explanation()
    {
        var ctx = new SkillsEvidenceTestContext();

        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() =>
            ctx.Service.CreateSkillAsync("", "!!!", SkillKind.Domain, null, SkillsEvidenceTestContext.TenantA, 1));

        Assert.Contains("skill key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_undefined_skill_kind_is_refused()
    {
        var ctx = new SkillsEvidenceTestContext();

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() =>
            ctx.Service.CreateSkillAsync("x", "X", (SkillKind)99, null, SkillsEvidenceTestContext.TenantA, 1));
    }
}
