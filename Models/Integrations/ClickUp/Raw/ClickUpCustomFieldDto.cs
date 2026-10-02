using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProgrammePulse.Models.Integrations.ClickUp.Raw;

public sealed class ClickUpCustomFieldDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }
}
