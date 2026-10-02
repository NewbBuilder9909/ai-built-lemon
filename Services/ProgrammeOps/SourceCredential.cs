namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// A tenant's own decrypted credential for one source connection — never
/// persisted in this shape, only ever the output of
/// ISourceCredentialProtector.Unprotect and the input to Protect. One shared
/// shape for every source rather than a ClickUpCredential/HubPlannerCredential
/// pair: ClickUp uses both fields, Hub Planner only ApiToken.
/// </summary>
public sealed record SourceCredential(
    string ApiToken,
    string? WorkspaceId = null,
    string? RefreshToken = null,
    DateTimeOffset? ExpiresAtUtc = null,
    string? SiteUrl = null,
    string? ProjectIds = null,
    string? ClientId = null,
    string? ClientSecret = null);
