namespace ProgrammePulse.Models.Erp;

/// <summary>
/// One record's full lifecycle through the demo pipeline: what came in, what
/// validation found, what stage it reached, and — if it got far enough — what
/// the canonical transformed record looks like.
/// </summary>
public sealed class ErpPipelineItem
{
    public required RawErpRecord Raw { get; init; }

    public required ValidationResult Validation { get; init; }

    public required ProcessingStage Stage { get; init; }

    public required TransformedErpRecord Transformed { get; init; }
}
