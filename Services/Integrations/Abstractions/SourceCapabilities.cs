namespace ProgrammePulse.Services.Integrations.Abstractions;

/// <summary>
/// What kinds of canonical data a Bronze source actually supplies. Declared
/// per source on <see cref="ISyncSource.Capabilities"/>.
///
/// This is the deliberately-scoped answer to the staged brief's request for
/// IWorkSource / IResourceSource / ITimeSource / IAbsenceSource /
/// ICommercialSource. Five fetch interfaces were rejected: ClickUp and Hub
/// Planner have genuinely different fetch shapes (spaces → folders → lists →
/// tasks versus flat projects + bookings), so a shared
/// "GetWorkItemsAsync()" would be a lowest-common-denominator contract that
/// every implementation fights — and nothing in Silver or Gold would ever
/// call it, because only the source's own sync orchestrator fetches, and it
/// already knows its own shape. An interface with exactly one caller per
/// implementation is ceremony, not abstraction.
///
/// What Silver and Gold genuinely need is not "fetch work from any source"
/// but "is a source of this kind even connected?" — so that a capacity or
/// data-confidence figure can say *no absence source is connected, so leave
/// is unaccounted for* instead of quietly reporting as though it were.
/// That question is answered by a flags enum, not by five interfaces.
/// </summary>
[Flags]
public enum SourceCapabilities
{
    None = 0,

    /// <summary>Work items with lifecycle, estimates, due dates (ClickUp tasks, Jira issues).</summary>
    Work = 1,

    /// <summary>People/resources and their scheduled allocations (Hub Planner bookings).</summary>
    Resource = 2,

    /// <summary>Recorded actual time against work (ClickUp time entries, Harvest).</summary>
    Time = 4,

    /// <summary>Leave, holidays and non-working time. Nothing supplies this yet — leave comes from the internal Staff domain.</summary>
    Absence = 8,

    /// <summary>Contract value, rates or invoices from an external commercial system. Nothing supplies this yet.</summary>
    Commercial = 16
}
