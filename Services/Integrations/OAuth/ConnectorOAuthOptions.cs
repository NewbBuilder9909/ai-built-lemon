namespace ProgrammePulse.Services.Integrations.OAuth;

/// <summary>One distributable Atlassian OAuth app per deployment. Tempo client credentials are tenant-owned.</summary>
public sealed class ConnectorOAuthOptions
{
    public const string SectionName = "ConnectorOAuth";
    public string JiraClientId { get; set; } = string.Empty;
    public string JiraClientSecret { get; set; } = string.Empty;
    public string JiraRedirectUri { get; set; } = string.Empty;
    public string TempoRedirectUri { get; set; } = string.Empty;
}
