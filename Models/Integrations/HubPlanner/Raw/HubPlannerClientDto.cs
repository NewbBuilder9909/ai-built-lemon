using System.Text.Json.Serialization;

namespace ProgrammePulse.Models.Integrations.HubPlanner.Raw;

/// <summary>
/// Bronze-layer DTO for a Hub Planner Client. Captured to Bronze only and
/// never mapped to Silver — Models/Programme/Customer.cs is deliberately
/// admin-authored, not synced from any source (see its own doc comment), so
/// wiring this into Customer would reverse that decision rather than extend
/// it. See docs/programme-ops.md's Hub Planner section.
/// </summary>
public sealed class HubPlannerClientDto
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
