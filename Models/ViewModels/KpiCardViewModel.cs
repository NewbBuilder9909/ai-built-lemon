namespace ProgrammePulse.Models.ViewModels;

/// <summary>
/// A headline figure. With a <see cref="Denominator"/> the card says what the
/// number is out of ("of 67 open items", through the resource key
/// <see cref="DenominatorKey"/>) and draws the share as a meter: a count with
/// no denominator can't be judged.
/// </summary>
public sealed record KpiCardViewModel(
    string Label,
    string Value,
    string? SubLabel = null,
    bool NeedsAttention = false,
    int? Numerator = null,
    int? Denominator = null,
    string? DenominatorKey = null)
{
    /// <summary>The share for the meter, as a CSS percentage; null without a denominator.</summary>
    public string? SharePercent => Numerator is { } n && Denominator is > 0
        ? Math.Round(Math.Clamp(n * 100m / Denominator.Value, 0m, 100m), 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%"
        : null;
}
