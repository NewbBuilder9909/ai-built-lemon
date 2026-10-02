using System.Text.Json.Serialization;

namespace ProgrammePulse.Models.Integrations.ClickUp.Raw;

public sealed class ClickUpTaskDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public ClickUpStatusDto? Status { get; set; }

    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    [JsonPropertyName("date_due")]
    public string? DateDue { get; set; }

    [JsonPropertyName("time_estimate")]
    public long? TimeEstimateMs { get; set; }

    [JsonPropertyName("assignees")]
    public List<ClickUpUserDto> Assignees { get; set; } = [];

    [JsonPropertyName("custom_fields")]
    public List<ClickUpCustomFieldDto> CustomFields { get; set; } = [];
}
