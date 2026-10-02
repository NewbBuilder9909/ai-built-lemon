namespace ProgrammePulse.Services.Integrations.ClickUp;

/// <summary>
/// Bound from configuration section "ClickUp". BaseUrl/WorkspaceId are
/// non-secret and must be supplied via environment or deployment
/// configuration; ApiToken must never be embedded in source or app defaults.
/// See docs/programme-ops.md.
/// </summary>
public sealed class ClickUpOptions
{
    public const string SectionName = "ClickUp";

    public string BaseUrl { get; set; } = string.Empty;

    public string WorkspaceId { get; set; } = string.Empty;

    public string ApiToken { get; set; } = string.Empty;
}
