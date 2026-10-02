using System.Reflection;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// Structural guards on the delivery load contract, in the spirit of the
/// skills module's EvidencePortfolioShapeTests: the rule that this view
/// cannot rank people is worth more as a failing build than as a paragraph
/// in a document. These tests fail if someone adds the field that would turn
/// the load view back into a performance league table — which is the single
/// most likely way this feature goes wrong later.
/// </summary>
public sealed class DeliveryLoadShapeTests
{
    private static readonly Type[] PersonFacingTypes =
    [
        typeof(PersonLoadFinding),
        typeof(PersonLoadRow),
        typeof(DeliveryLoadReport),
        typeof(DeliveryLoadResult)
    ];

    /// <summary>
    /// Words that would each express a judgement about a person rather than
    /// a count of their work contexts.
    /// </summary>
    private static readonly string[] ForbiddenFragments =
    [
        "score", "rank", "rating", "percentile", "grade", "tier",
        "performance", "productivity", "quality", "efficiency",
        "position", "league", "worst", "best", "top", "bottom"
    ];

    [Fact]
    public void No_person_facing_type_carries_a_score_or_rank_shaped_member()
    {
        var offenders =
            (from type in PersonFacingTypes
             from member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
             from fragment in ForbiddenFragments
             where member.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)
             select $"{type.Name}.{member.Name} (matched '{fragment}')").ToArray();

        Assert.True(offenders.Length == 0,
            "A delivery load type gained a member that expresses a judgement about a person rather than a "
            + "count of their work: " + string.Join(", ", offenders)
            + ". See docs/delivery-load.md — this view is only defensible while it cannot rank anyone.");
    }

    [Fact]
    public void No_person_facing_type_carries_a_computed_fractional_measure()
    {
        // Every honest number here is a count of contexts or weeks. A double
        // or decimal is how a composite score arrives: nobody adds one to
        // hold an integer.
        var fractional = new[] { typeof(double), typeof(float), typeof(decimal), typeof(double?), typeof(float?), typeof(decimal?) };

        var offenders =
            (from type in PersonFacingTypes
             from property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
             where fractional.Contains(property.PropertyType)
             select $"{type.Name}.{property.Name}").ToArray();

        Assert.True(offenders.Length == 0,
            "A delivery load type gained a fractional measure: " + string.Join(", ", offenders)
            + ". Counts are integers; a decimal here is a score in disguise.");
    }

    [Fact]
    public void LoadSignal_cannot_express_that_someone_is_below_normal()
    {
        var names = Enum.GetNames<LoadSignal>();

        // Three surfaceable states and no fourth. The absence of a
        // "Steady"/"Low"/"BelowNormal" member is what stops a consumer
        // rendering every person in an ordered list — a person under their
        // own baseline produces no finding, so there is nothing to render.
        Assert.Equal(
            new[] { nameof(LoadSignal.CoverageFellAway), nameof(LoadSignal.Elevated), nameof(LoadSignal.Sustained) },
            names.Order());

        foreach (var forbidden in new[] { "steady", "low", "below", "normal", "idle", "under", "light" })
        {
            Assert.DoesNotContain(names, n => n.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_report_cannot_be_built_with_names_unless_it_says_so()
    {
        // IncludesPeople exists so a view cannot silently render names that
        // the caller's capability did not permit; a report with findings but
        // the flag unset would mean the capability check was bypassed.
        var report = typeof(DeliveryLoadReport);

        Assert.NotNull(report.GetProperty(nameof(DeliveryLoadReport.IncludesPeople)));
        Assert.NotNull(report.GetProperty(nameof(DeliveryLoadReport.PeopleInScope)));
        Assert.NotNull(report.GetProperty(nameof(DeliveryLoadReport.PeopleWithoutTimeData)));
    }
}
