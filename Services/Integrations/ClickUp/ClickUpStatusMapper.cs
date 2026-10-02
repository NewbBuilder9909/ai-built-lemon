using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.Integrations.ClickUp;

/// <summary>
/// ClickUp statuses are free text and defined per-Space, so this can only
/// recognise the common ones teams typically configure. Anything else maps
/// to Unmapped rather than a guess — same "never silently drop unexpected
/// upstream data" rule Services/Transformation/StatusMapper follows for ERP
/// statuses. Extend this list as real customer ClickUp spaces are onboarded.
/// </summary>
public sealed class ClickUpStatusMapper : IClickUpStatusMapper
{
    public WorkItemLifecycleStage Map(string? rawStatus)
    {
        var trimmed = rawStatus?.Trim();

        return trimmed?.ToUpperInvariant() switch
        {
            "TO DO" or "OPEN" or "BACKLOG" => WorkItemLifecycleStage.Backlog,
            "READY" or "READY FOR DEV" or "READY TO START" => WorkItemLifecycleStage.Ready,
            "IN PROGRESS" or "IN DEVELOPMENT" or "WIP" => WorkItemLifecycleStage.InProgress,
            "BLOCKED" or "ON HOLD" => WorkItemLifecycleStage.Blocked,
            "IN REVIEW" or "REVIEW" or "QA" or "TESTING" => WorkItemLifecycleStage.InReview,
            "DONE" or "COMPLETE" or "CLOSED" => WorkItemLifecycleStage.Done,
            "CANCELLED" or "CANCELED" or "WON'T DO" => WorkItemLifecycleStage.Cancelled,
            _ => WorkItemLifecycleStage.Unmapped
        };
    }
}
