using System.Text.Json.Serialization;

namespace ProgrammePulse.Models.Integrations.HubPlanner.Raw;

/// <summary>
/// Bronze-layer DTO for a Hub Planner Resource (a staff member in Hub
/// Planner's model). Captured to Bronze only — there's no Silver "resource"
/// entity, since staff identity already lives in the Staff domain and is
/// joined by email, same treatment ClickUp workspace members get.
/// </summary>
public sealed class HubPlannerResourceDto
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }
}
