using System.Text.Json.Serialization;

namespace ProgrammePulse.Models.Integrations.HubPlanner.Raw;

/// <summary>
/// Bronze-layer DTO — matches Hub Planner's JSON shape verbatim. Only the
/// fields HubPlannerMappingService actually reads are modelled; the full
/// response (budget/billing/tags/custom fields, etc.) still lands in Bronze
/// verbatim via the raw JSON capture, just not as typed properties here.
/// </summary>
public sealed class HubPlannerProjectDto
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
