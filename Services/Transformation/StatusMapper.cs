using ProgrammePulse.Models.Erp;

namespace ProgrammePulse.Services.Transformation;

/// <summary>
/// Maps the source ERP system's free-text status codes onto the canonical
/// NormalisedStatus. Anything not explicitly recognised — including missing
/// values — becomes Exception, so unexpected upstream data is never silently dropped.
/// </summary>
public sealed class StatusMapper : IStatusMapper
{
    public NormalisedStatus Map(string? rawStatus)
    {
        var trimmed = rawStatus?.Trim();

        return trimmed?.ToUpperInvariant() switch
        {
            "OPEN" => NormalisedStatus.New,
            "WIP" => NormalisedStatus.InProgress,
            "DONE" => NormalisedStatus.Complete,
            _ => NormalisedStatus.Exception
        };
    }
}
