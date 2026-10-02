namespace ProgrammePulse.Services.Integrations.HubPlanner;

/// <summary>
/// Bound from configuration section "HubPlanner". BaseUrl is non-secret and
/// must be supplied through deployment or environment configuration. ApiKey
/// must never be embedded in source or app defaults. Use a Read Only key
/// (Hub Planner supports scoping a key to Read Only vs. Read &amp; Write) —
/// this integration only ever reads. See docs/programme-ops.md.
/// </summary>
public sealed class HubPlannerOptions
{
    public const string SectionName = "HubPlanner";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;
}
