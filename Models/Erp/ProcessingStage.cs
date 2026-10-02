namespace ProgrammePulse.Models.Erp;

/// <summary>
/// Where a record currently sits in the demo transformation pipeline itself.
/// This is distinct from NormalisedStatus, which is the record's business status —
/// two records can both be "New" business-wise while one has already been
/// Transformed by the pipeline and the other is still Validating.
/// </summary>
public enum ProcessingStage
{
    New,
    Validating,
    Transformed,
    Exception
}
