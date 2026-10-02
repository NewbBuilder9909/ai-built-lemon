using System.Text.Json.Serialization;

namespace ProgrammePulse.Models.Integrations.ClickUp.Raw;

/// <summary>
/// Bronze-layer DTO — matches ClickUp's JSON shape verbatim, snake_case
/// field names included. Only Services/Integrations/ClickUp/ClickUpMappingService
/// should ever construct a Silver model from one of these.
/// </summary>
public sealed class ClickUpUserDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}
