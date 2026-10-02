namespace ProgrammePulse.Models.ViewModels.ProgrammeOverview;

/// <summary>
/// Per-source publication state for the management header: when the data
/// on the page was last completely published from that source, whether a
/// run is in flight, and what the last failure was — so a reader never has
/// to guess whether a number is current. Built by
/// Services/ProgrammeOps/SyncStatusQueryService from ProgrammeOps_SyncRun.
///
/// Honesty rule: syncs commit each Silver upsert as it goes and Gold reads
/// the live tables, so after a failed (or during a running) run the page
/// shows the last complete publication <i>plus</i> whatever that later run
/// wrote before it stopped. <see cref="CurrentDataMayBePartial"/> is true
/// in exactly those cases and the views say so, rather than claiming the
/// figures are "the last successful publication".
/// </summary>
public sealed record SourcePublicationStateViewModel(
    string Source,
    string DisplayName,
    DateTime? LastPublishedAtUtc,
    string? LastPublishedSummary,
    bool IsRunning,
    string? RunningStage,
    DateTime? RunningSinceUtc,
    bool LastRunFailed,
    DateTime? LastFailureAtUtc,
    string? LastFailureStage,
    string? LastFailureError,
    string FreshnessLabel)
{
    public string StatusLabel => IsRunning ? "Running" : LastRunFailed ? "Failed" : LastPublishedAtUtc is null ? "Never published" : "Published";

    /// <summary>
    /// Has this source ever run for the tenant? A source that never has is a
    /// product the customer hasn't connected, not a data-freshness fact, so the
    /// overview lists it under "connect" rather than as a row of "never".
    /// </summary>
    public bool HasActivity => LastPublishedAtUtc is not null || IsRunning || LastRunFailed;

    /// <summary>The live tables may mix the last complete publication with rows a later, unfinished run wrote.</summary>
    public bool CurrentDataMayBePartial => IsRunning || LastRunFailed;

    /// <summary>One sentence the views show whenever <see cref="CurrentDataMayBePartial"/> is true.</summary>
    public string PartialDataNote =>
        IsRunning
            ? "A run is in progress: figures may change as it publishes."
            : LastPublishedAtUtc is null
                ? "No complete publication yet: figures include only what the failed run wrote before it stopped."
                : $"Figures may mix the last complete publication ({LastPublishedAtUtc:yyyy-MM-dd HH:mm} UTC) with records the failed run wrote before it stopped{(LastFailureStage is null ? "" : $" while {LastFailureStage}")}; re-run to converge.";
}
