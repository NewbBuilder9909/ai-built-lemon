using System.Text.Json.Serialization;

namespace ProgrammePulse.Models.Integrations.ClickUp.Raw;

public sealed class ClickUpTimeEntryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("task")]
    public ClickUpTaskReferenceDto? Task { get; set; }

    [JsonPropertyName("duration")]
    public long DurationMs { get; set; }

    [JsonPropertyName("start")]
    public string? StartMs { get; set; }

    [JsonPropertyName("billable")]
    public bool Billable { get; set; }

    [JsonPropertyName("user")]
    public ClickUpUserDto? User { get; set; }
}

public sealed class ClickUpTaskReferenceDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}
