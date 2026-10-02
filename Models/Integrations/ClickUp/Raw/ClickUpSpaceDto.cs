using System.Text.Json.Serialization;

namespace ProgrammePulse.Models.Integrations.ClickUp.Raw;

public sealed class ClickUpSpaceDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
