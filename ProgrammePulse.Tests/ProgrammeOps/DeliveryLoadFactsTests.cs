using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// Behaviour of the delivery load measure, and the four rules that stop the
/// view it feeds being read as a ranking. The rules are the reason this
/// feature is allowed to exist at all (see docs/delivery-load.md), so each
/// one is pinned here rather than left to a code comment.
/// </summary>
public sealed class DeliveryLoadFactsTests
{
    private static readonly DateOnly CurrentWeek = new(2026, 9, 21);
    private static readonly DeliveryLoadThresholds Defaults = new();

    // Differ only in the final byte, so PersonA sorts before PersonB by key.
    private static readonly Guid PersonA = new("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid PersonB = new("00000000-0000-0000-0000-0000000000bb");

    [Fact]
    public void Rule_1_a_person_below_their_own_baseline_is_not_returned_at_all()
    {
        var result = Calculate(Person(PersonA, current: 2, baseline: [5, 5, 5, 5]));

        // Not "returned with a low number" — absent. There is no bottom of
        // the list to read as an underperformer list, because people at or
        // under their own normal never appear.
        Assert.Empty(result.Findings);
        Assert.Equal(1, result.PeopleWithSufficientHistory);
    }

    [Fact]
    public void Rule_1_a_person_exactly_at_their_own_baseline_is_not_returned()
    {
        var result = Calculate(Person(PersonA, current: 5, baseline: [5, 5, 5, 5]));

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Rule_1_a_person_just_under_the_elevation_threshold_is_not_returned()
    {
        // Median 5, threshold 5 + 2 = 7. Six contexts is a rise, but not a
        // finding: a threshold that trips on noise produces a list of
        // everybody, which is the ranking this design exists to avoid.
        var result = Calculate(Person(PersonA, current: 6, baseline: [5, 5, 5, 5]));

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Crossing_the_threshold_reports_elevated_against_the_persons_own_median()
    {
        var result = Calculate(Person(PersonA, current: 7, baseline: [5, 5, 5, 5]));

        var finding = Assert.Single(result.Findings);
        Assert.Equal(LoadSignal.Elevated, finding.Signal);
        Assert.Equal(7, finding.CurrentContexts);
        Assert.Equal(5, finding.BaselineContexts);
        Assert.Equal(1, finding.ConsecutiveWeeks);
    }

    [Fact]
    public void Several_consecutive_elevated_weeks_become_sustained()
    {
        // Weeks 1 and 2 are elevated too and remain part of the baseline, so
        // the median stays 5 and the run is measured honestly at three.
        var result = Calculate(Person(PersonA, current: 7, baseline: [7, 7, 5, 5, 5, 5]));

        var finding = Assert.Single(result.Findings);
        Assert.Equal(LoadSignal.Sustained, finding.Signal);
        Assert.Equal(3, finding.ConsecutiveWeeks);
    }

    [Fact]
    public void Someone_who_logged_consistently_and_then_stopped_is_a_coverage_exception_not_a_low_reading()
    {
        // The under-logging trap: logging discipline fails first when work
        // piles up, so silence must never be rendered as a lighter week.
        var result = Calculate(new PersonLoadInput(
            PersonA,
            BaselineWeeks([6, 6, 6, 6]),
            OpenAllocatedContexts: 3));

        var finding = Assert.Single(result.Findings);
        Assert.Equal(LoadSignal.CoverageFellAway, finding.Signal);

        // No fabricated count for a week with no data.
        Assert.Null(finding.CurrentContexts);
        Assert.Equal(6, finding.BaselineContexts);
    }

    [Fact]
    public void Too_little_of_a_persons_own_history_means_no_finding_and_no_guess()
    {
        var result = Calculate(Person(PersonA, current: 40, baseline: [5, 5, 5]));

        // Forty contexts and still no finding: an absent baseline is unknown
        // load, and the measure will not assert against a baseline it does
        // not have.
        Assert.Empty(result.Findings);
        Assert.Equal(0, result.PeopleWithSufficientHistory);
        Assert.Equal(1, result.PeopleInScope);
    }

    [Fact]
    public void Rule_4_findings_are_ordered_by_key_never_by_how_elevated_anyone_is()
    {
        // PersonA is barely over; PersonB is far over. Magnitude ordering
        // would put B first, and would put someone last.
        var result = Calculate(
            Person(PersonA, current: 7, baseline: [5, 5, 5, 5]),
            Person(PersonB, current: 20, baseline: [5, 5, 5, 5]));

        Assert.Equal([PersonA, PersonB], result.Findings.Select(f => f.StaffKey));
        Assert.Equal(
            result.Findings.OrderBy(f => f.StaffKey).Select(f => f.StaffKey),
            result.Findings.Select(f => f.StaffKey));
    }

    [Fact]
    public void The_baseline_is_a_median_so_one_bad_week_does_not_reset_someones_normal()
    {
        // A single 20-context week among fives leaves the median at 5, so a
        // genuinely elevated week after it is still detected. A mean would
        // have moved the baseline to 8 and hidden it.
        var result = Calculate(Person(PersonA, current: 7, baseline: [20, 5, 5, 5, 5]));

        var finding = Assert.Single(result.Findings);
        Assert.Equal(5, finding.BaselineContexts);
        Assert.Equal(LoadSignal.Elevated, finding.Signal);
    }

    [Fact]
    public void Coverage_figures_report_how_much_of_the_team_the_findings_come_from()
    {
        var result = Calculate(
            Person(PersonA, current: 7, baseline: [5, 5, 5, 5]),
            new PersonLoadInput(PersonB, [], OpenAllocatedContexts: 0));

        Assert.Single(result.Findings);
        Assert.Equal(2, result.PeopleInScope);
        Assert.Equal(1, result.PeopleWithSufficientHistory);
        Assert.Equal(1, result.PeopleWithoutTimeData);
    }

    private static DeliveryLoadResult Calculate(params PersonLoadInput[] people) =>
        DeliveryLoadFacts.Calculate(people, CurrentWeek, Defaults);

    private static PersonLoadInput Person(Guid key, int current, int[] baseline) =>
        new(key,
            [new WeeklyLoadObservation(CurrentWeek, current, HasTimeData: true), .. BaselineWeeks(baseline)],
            OpenAllocatedContexts: 0);

    /// <summary>Weeks 1..n before the current one, most recent first.</summary>
    private static List<WeeklyLoadObservation> BaselineWeeks(int[] contexts) =>
        [.. contexts.Select((count, index) =>
            new WeeklyLoadObservation(CurrentWeek.AddDays(-7 * (index + 1)), count, HasTimeData: true))];
}
