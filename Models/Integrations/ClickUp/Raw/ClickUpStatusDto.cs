using System.Text.Json.Serialization;

namespace ProgrammePulse.Models.Integrations.ClickUp.Raw;

public sealed class ClickUpStatusDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
