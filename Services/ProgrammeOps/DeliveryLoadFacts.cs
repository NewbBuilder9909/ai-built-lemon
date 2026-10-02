namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Weekly concurrency arithmetic for the delivery load view: how many
/// distinct project contexts one person was active in, compared with that
/// same person's own recent history.
///
/// This type is deliberately shaped so that the view built on it cannot be
/// inverted into a low-performer list. Four rules, each pinned by a test in
/// DeliveryLoadFactsTests / DeliveryLoadShapeTests:
///
/// 1. <b>No row below baseline.</b> A person whose context count fell is
///    never returned. There is no "bottom of the list" to read, because
///    people at or under their own normal are absent from the result
///    entirely — not present with a low number.
/// 2. <b>Self-referential only.</b> Every comparison is against the same
///    person's trailing median. Nothing here compares two people, and the
///    result carries no rank, percentile, position or peer denominator.
/// 3. <b>No score.</b> <see cref="LoadSignal"/> is a three-value enum of
///    surfaceable states. "Below normal" is not expressible in the type
///    system, so no consumer can render it by accident.
/// 4. <b>Findings are ordered by key, never by magnitude.</b> Sorting by
///    severity would reintroduce an ordering whose tail reads as a ranking.
///
/// The elevation threshold is an absolute count of extra contexts, not a
/// ratio or a model output. A percentage would be fabricated precision over
/// what is ultimately a small integer.
/// </summary>
public static class DeliveryLoadFacts
{
    public static DeliveryLoadResult Calculate(
        IReadOnlyList<PersonLoadInput> people,
        DateOnly currentWeekStart,
        DeliveryLoadThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(thresholds);

        var findings = new List<PersonLoadFinding>();
        var withSufficientHistory = 0;
        var withoutTimeData = 0;

        foreach (var person in people)
        {
            var baseline = person.Weeks
                .Where(w => w.WeekStart < currentWeekStart && w.HasTimeData)
                .OrderByDescending(w => w.WeekStart)
                .Take(thresholds.BaselineWeeks)
                .ToList();

            var current = person.Weeks.FirstOrDefault(w => w.WeekStart == currentWeekStart);

            // Not enough of this person's own history to say anything. They
            // are counted, never guessed at: an absent baseline is unknown
            // load, and unknown load is not low load.
            if (baseline.Count < thresholds.MinimumBaselineWeeks)
            {
                continue;
            }

            withSufficientHistory++;

            // The under-logging trap, handled explicitly. Someone who logged
            // consistently and then stopped is the most likely person to be
            // genuinely swamped, because logging discipline fails first when
            // work piles up. Reading that silence as "lighter" would invert
            // the whole measure, so it is reported as a data exception and
            // carries no context count at all.
            if (current is null || !current.HasTimeData)
            {
                findings.Add(new PersonLoadFinding(
                    person.StaffKey,
                    LoadSignal.CoverageFellAway,
                    CurrentContexts: null,
                    BaselineContexts: Median(baseline),
                    ConsecutiveWeeks: 0,
                    person.OpenAllocatedContexts));
                continue;
            }

            var baselineMedian = Median(baseline);
            var threshold = baselineMedian + thresholds.ElevationContexts;

            if (current.DistinctContexts < threshold)
            {
                // At or under their own normal. No row, by rule 1.
                continue;
            }

            var consecutive = CountConsecutiveElevated(person.Weeks, currentWeekStart, thresholds, baselineMedian);

            findings.Add(new PersonLoadFinding(
                person.StaffKey,
                consecutive >= thresholds.SustainedWeeks ? LoadSignal.Sustained : LoadSignal.Elevated,
                current.DistinctContexts,
                baselineMedian,
                consecutive,
                person.OpenAllocatedContexts));
        }

        withoutTimeData = people.Count(p => p.Weeks.All(w => !w.HasTimeData));

        return new DeliveryLoadResult(
            // Ordered by key, not by magnitude (rule 4).
            findings.OrderBy(f => f.StaffKey).ToList(),
            people.Count,
            withSufficientHistory,
            withoutTimeData,
            currentWeekStart,
            thresholds);
    }

    private static int CountConsecutiveElevated(
        IReadOnlyList<WeeklyLoadObservation> weeks,
        DateOnly currentWeekStart,
        DeliveryLoadThresholds thresholds,
        int baselineMedian)
    {
        var threshold = baselineMedian + thresholds.ElevationContexts;
        var count = 0;
        var cursor = currentWeekStart;

        while (true)
        {
            var week = weeks.FirstOrDefault(w => w.WeekStart == cursor);
            if (week is null || !week.HasTimeData || week.DistinctContexts < threshold)
            {
                return count;
            }

            count++;
            cursor = cursor.AddDays(-7);
        }
    }

    private static int Median(IReadOnlyList<WeeklyLoadObservation> weeks)
    {
        var ordered = weeks.Select(w => w.DistinctContexts).OrderBy(c => c).ToList();
        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2;
    }
}

/// <summary>
/// The only states this measure can express. There is deliberately no
/// "Steady" or "BelowNormal" member: a person at or under their own normal
/// produces no finding at all, so the absence of a member for that state is
/// what stops a consumer rendering a full ordered list of everyone.
/// DeliveryLoadShapeTests fails the build if a fourth member is added.
/// </summary>
public enum LoadSignal
{
    /// <summary>Above this person's own trailing median for the current week.</summary>
    Elevated,

    /// <summary>Elevated for several consecutive weeks — the state that actually matters.</summary>
    Sustained,

    /// <summary>
    /// This person logged consistently and then stopped. A data-quality
    /// exception to chase, never a reading that their load is lower.
    /// </summary>
    CoverageFellAway
}

/// <summary>
/// One surfaced person. Every numeric member is either this person's own
/// count or their own baseline — there is no rank, percentile, peer
/// comparison or composite score, and a shape test fails the build if one
/// is added. <see cref="CurrentContexts"/> is null for
/// <see cref="LoadSignal.CoverageFellAway"/> because no honest count exists
/// for a week whose time data is missing.
/// </summary>
public sealed record PersonLoadFinding(
    Guid StaffKey,
    LoadSignal Signal,
    int? CurrentContexts,
    int BaselineContexts,
    int ConsecutiveWeeks,
    int OpenAllocatedContexts);

/// <summary>
/// Thresholds are agreed counts, not tuned parameters of a model. Defaults
/// are a starting point for a design partner to change, not a calibrated
/// recommendation.
/// </summary>
public sealed record DeliveryLoadThresholds
{
    /// <summary>How many of the person's own prior weeks form the baseline.</summary>
    public int BaselineWeeks { get; init; } = 8;

    /// <summary>Below this many observed weeks, this person is not assessed at all.</summary>
    public int MinimumBaselineWeeks { get; init; } = 4;

    /// <summary>Extra concurrent project contexts, over the person's own median, that count as elevated.</summary>
    public int ElevationContexts { get; init; } = 2;

    /// <summary>Consecutive elevated weeks before the signal becomes Sustained.</summary>
    public int SustainedWeeks { get; init; } = 3;
}

/// <summary>
/// Findings plus the coverage figures needed to read them honestly. The
/// counts are what let a reader see how much of the team the findings are
/// actually drawn from — a findings list without its denominator invites
/// exactly the reading this measure exists to prevent.
/// </summary>
public sealed record DeliveryLoadResult(
    IReadOnlyList<PersonLoadFinding> Findings,
    int PeopleInScope,
    int PeopleWithSufficientHistory,
    int PeopleWithoutTimeData,
    DateOnly CurrentWeekStart,
    DeliveryLoadThresholds Thresholds);

/// <summary>One person's weekly series plus their current standing concurrency.</summary>
public sealed record PersonLoadInput(
    Guid StaffKey,
    IReadOnlyList<WeeklyLoadObservation> Weeks,
    int OpenAllocatedContexts);

/// <summary>
/// One ISO week for one person. <see cref="HasTimeData"/> is tracked
/// separately from a zero count because "logged nothing" and "logged work
/// against no project" are different facts, and collapsing them is what
/// makes an under-logging week look like a quiet one.
/// </summary>
public sealed record WeeklyLoadObservation(
    DateOnly WeekStart,
    int DistinctContexts,
    bool HasTimeData);
