namespace ProgrammePulse.Models.Programme;

public enum AlertType
{
    WorkstreamBlocked,
    NegativeResidualCapacity,

    /// <summary>
    /// Forward-looking, unlike NegativeResidualCapacity — raised when a
    /// contributor's primary-allocated open work due within the next
    /// AlertDetectionService.ForwardWindowDays already exceeds their
    /// remaining capacity in that window, before any time has been logged
    /// against it. See AlertDetectionService.DetectUpcomingOverAllocationsAsync.
    /// </summary>
    UpcomingOverAllocation,

    /// <summary>
    /// The opposite direction from NegativeResidualCapacity — a contributor
    /// who had meaningful available hours in the period (not just someone on
    /// full leave) but logged well under
    /// AlertDetectionService.LowUtilisationThresholdPercent of them. A PMO
    /// resourcing signal (bench time, not just overload) that the codebase
    /// had no way to surface until now.
    /// </summary>
    LowUtilisation
}
