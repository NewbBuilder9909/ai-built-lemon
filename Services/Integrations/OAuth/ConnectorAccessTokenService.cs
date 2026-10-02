using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Services.Integrations.OAuth;

/// <summary>Resolves a tenant-owned OAuth grant, refreshes expiring access tokens, and fails closed on corrupt/missing credentials.</summary>
public sealed class ConnectorAccessTokenService(
    ISourceConnectionRepository connections,
    ISourceCredentialProtector protector,
    ConnectorOAuthClient oauth,
    TimeProvider timeProvider,
    ILogger<ConnectorAccessTokenService> logger,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<SourceCredential> GetAsync(Guid tenantId, string source, CancellationToken cancellationToken)
    {
        if (source is not ("Jira" or "Tempo")) throw new ArgumentOutOfRangeException(nameof(source));
        var connection = await connections.GetActiveForTenantAsync(tenantId, source);
        var credential = protector.Unprotect(connection?.ProtectedCredentialJson)
            ?? throw new InvalidOperationException($"{source} is not connected for this tenant.");
        if (string.IsNullOrWhiteSpace(credential.RefreshToken) || credential.ExpiresAtUtc is null)
            throw new InvalidOperationException($"{source} OAuth credentials are incomplete. Reconnect the source.");
        if (credential.ExpiresAtUtc > timeProvider.GetUtcNow().AddMinutes(2)) return credential;

        OAuthTokens refreshed;
        try
        {
            refreshed = source == "Jira"
            ? await oauth.RefreshJiraAsync(credential.RefreshToken, cancellationToken)
            : await oauth.RefreshTempoAsync(credential.RefreshToken,
                credential.ClientId ?? throw new InvalidOperationException("Tempo OAuth client ID is missing."),
                credential.ClientSecret ?? throw new InvalidOperationException("Tempo OAuth client secret is missing."), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            Security.SecurityEvents.Write(logger, httpContextAccessor.HttpContext, "OAuthRefreshFailed", tenantId: tenantId, provider: source);
            throw;
        }
        var updated = credential with
        {
            ApiToken = refreshed.AccessToken,
            RefreshToken = refreshed.RefreshToken,
            ExpiresAtUtc = refreshed.ExpiresAtUtc
        };
        await connections.SetCredentialAsync(tenantId, source, protector.Protect(updated), timeProvider.GetUtcNow().UtcDateTime);
        return updated;
    }
}
