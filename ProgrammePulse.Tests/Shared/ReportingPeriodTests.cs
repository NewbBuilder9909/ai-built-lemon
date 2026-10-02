using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.Shared;

public class ReportingPeriodTests
{
    private static readonly DateOnly Today = new(2026, 9, 27);

    private static (bool Ok, DateOnly Start, DateOnly End, string? Error) Resolve(DateOnly? from, DateOnly? to)
    {
        var ok = ReportingPeriod.TryResolve(from, to, Today, 29, Today, out var start, out var end, out var error);
        return (ok, start, end, error);
    }

    [Fact]
    public void No_dates_is_the_default_window_ending_today()
    {
        var (ok, start, end, error) = Resolve(null, null);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new DateOnly(2026, 8, 29), start);
        Assert.Equal(Today, end);
    }

    [Fact]
    public void A_year_on_year_range_is_allowed()
    {
        var (ok, start, end, _) = Resolve(new DateOnly(2024, 10, 1), new DateOnly(2026, 9, 30));

        Assert.True(ok);
        Assert.Equal(new DateOnly(2024, 10, 1), start);
        Assert.Equal(new DateOnly(2026, 9, 30), end);
    }

    [Theory]
    [InlineData("0001-01-01", null)]
    [InlineData(null, "9999-12-31")]
    [InlineData("0001-01-01", "9999-12-31")]
    [InlineData(null, "0001-01-01")]
    [InlineData("2020-01-01", "2026-09-27")]
    [InlineData("2026-09-27", "2026-09-01")]
    public void Unbounded_backwards_or_overflowing_ranges_are_refused_with_a_message(string? from, string? to)
    {
        var (ok, _, _, error) = Resolve(from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to));

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void The_longest_allowed_span_is_exactly_the_limit()
    {
        var end = Today;
        Assert.True(Resolve(end.AddDays(-ReportingPeriod.MaxDays), end).Ok);
        Assert.False(Resolve(end.AddDays(-ReportingPeriod.MaxDays - 1), end).Ok);
    }
}
