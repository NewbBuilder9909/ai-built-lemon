namespace ProgrammePulse.Services.Shared;

/// <summary>
/// A report's date range from optional <c>from</c>/<c>to</c> query values,
/// with limits. Unbounded, <c>?from=0001-01-01</c> read a tenant's whole time
/// history and walked every day since year one for every person, and
/// <c>?to=9999-12-31</c> overflowed the date arithmetic into a 500 (Aikido:
/// uncontrolled resource consumption; CLAUDE.md: never read a tenant's whole
/// time history to show a period).
/// </summary>
public static class ReportingPeriod
{
    /// <summary>Two years and a day: a year-on-year comparison fits, a decade doesn't.</summary>
    public const int MaxDays = 731;

    /// <summary>How far from today either date may be.</summary>
    public const int MaxYearsBack = 20;
    public const int MaxYearsAhead = 2;

    /// <summary>
    /// The period to report on. A missing <paramref name="to"/> is
    /// <paramref name="defaultEnd"/>; a missing <paramref name="from"/> is
    /// <paramref name="defaultLengthDays"/> before the end. Refused, with a
    /// message the page can show, when either date is out of range, the end
    /// is before the start, or the span exceeds <see cref="MaxDays"/>.
    /// Whether the end is inclusive is the caller's convention.
    /// </summary>
    public static bool TryResolve(
        DateOnly? from, DateOnly? to, DateOnly defaultEnd, int defaultLengthDays, DateOnly today,
        out DateOnly start, out DateOnly end, out string? error)
    {
        start = end = default;
        var earliest = today.AddYears(-MaxYearsBack);
        var latest = today.AddYears(MaxYearsAhead);
        if (from is { } f && (f < earliest || f > latest) || to is { } t && (t < earliest || t > latest))
        {
            error = $"Choose dates between {earliest:yyyy-MM-dd} and {latest:yyyy-MM-dd}.";
            return false;
        }

        end = to ?? defaultEnd;
        start = from ?? end.AddDays(-defaultLengthDays);
        if (end < start)
        {
            error = "The period ends before it starts.";
            return false;
        }

        if (end.DayNumber - start.DayNumber > MaxDays)
        {
            error = $"A period can be at most {MaxDays} days. Choose a shorter range.";
            return false;
        }

        error = null;
        return true;
    }
}
