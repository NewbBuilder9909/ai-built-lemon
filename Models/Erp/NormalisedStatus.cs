namespace ProgrammePulse.Models.Erp;

/// <summary>
/// The canonical business status for a record, mapped from whatever free-text
/// status the source ERP system used (e.g. OPEN, WIP, DONE).
/// </summary>
public enum NormalisedStatus
{
    New,
    InProgress,
    Complete,
    Exception
}
