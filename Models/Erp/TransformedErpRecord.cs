namespace ProgrammePulse.Models.Erp;

/// <summary>
/// The canonical, cleaned-up record produced by the transformation pipeline.
/// Only populated when transformation could produce meaningful values —
/// see ErpPipelineItem for how this relates back to the raw record and its validation.
/// </summary>
public sealed class TransformedErpRecord
{
    public required string SourceErpId { get; init; }

    public required string CustomerName { get; init; }

    public string WorkType { get; init; } = "Unspecified";

    public required NormalisedStatus Status { get; init; }

    public decimal? Amount { get; init; }

    public DateTime? SourceDateUtc { get; init; }
}
