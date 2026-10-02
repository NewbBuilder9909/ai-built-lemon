using System.Text.Json.Serialization;

namespace ProgrammePulse.Models.Integrations.HubPlanner.Raw;

/// <summary>
/// Bronze-layer DTO for a Hub Planner Booking — a resource-to-project
/// allocation over a date range, not a task. See
/// HubPlannerMappingService.MapBookingWorkItemAsync for how this maps onto
/// the (task-shaped) Silver WorkItem model.
/// </summary>
public sealed class HubPlannerBookingDto
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("resource")]
    public string Resource { get; set; } = string.Empty;

    [JsonPropertyName("project")]
    public string Project { get; set; } = string.Empty;

    [JsonPropertyName("start")]
    public string? Start { get; set; }

    [JsonPropertyName("end")]
    public string? End { get; set; }

    /// <summary>e.g. "STATE_HOURS", "STATE_PERCENTAGE" — see StateValue.</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    /// <summary>
    /// Only meaningful together with State. Mapped to WorkItem.EstimatedHours
    /// when State == "STATE_HOURS"; left unmapped for other states (e.g.
    /// STATE_PERCENTAGE) since converting those to hours needs scale/calendar
    /// math not yet verified against real Hub Planner data.
    /// </summary>
    [JsonPropertyName("stateValue")]
    public double? StateValue { get; set; }

    /// <summary>e.g. "SCHEDULED" — Hub Planner's booking type, not a workflow status. Kept as WorkItem.RawStatus.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("categoryName")]
    public string? CategoryName { get; set; }
}
