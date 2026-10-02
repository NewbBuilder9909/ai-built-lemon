using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ProgrammePulse.Services.Integrations.OAuth;

public sealed record OAuthTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc);

/// <summary>Fixed-host OAuth code exchange and refresh. Provider errors never include response bodies in thrown messages.</summary>
public sealed class ConnectorOAuthClient(HttpClient client, IOptions<ConnectorOAuthOptions> options, TimeProvider timeProvider)
{
    public async Task<OAuthTokens> ExchangeJiraAsync(string code, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var response = await client.PostAsJsonAsync("https://auth.atlassian.com/oauth/token", new
        {
            grant_type = "authorization_code", client_id = settings.JiraClientId,
            client_secret = settings.JiraClientSecret, code, redirect_uri = settings.JiraRedirectUri
        }, cancellationToken);
        return await ReadTokensAsync(response, cancellationToken);
    }

    public async Task<OAuthTokens> RefreshJiraAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var response = await client.PostAsJsonAsync("https://auth.atlassian.com/oauth/token", new
        {
            grant_type = "refresh_token", client_id = settings.JiraClientId,
            client_secret = settings.JiraClientSecret, refresh_token = refreshToken
        }, cancellationToken);
        return await ReadTokensAsync(response, cancellationToken);
    }

    public async Task<OAuthTokens> ExchangeTempoAsync(string code, string clientId, string clientSecret, CancellationToken cancellationToken) =>
        await PostTempoAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = clientId,
            ["client_secret"] = clientSecret, ["redirect_uri"] = options.Value.TempoRedirectUri,
            ["code"] = code
        }, cancellationToken);

    public async Task<OAuthTokens> RefreshTempoAsync(string refreshToken, string clientId, string clientSecret, CancellationToken cancellationToken) =>
        await PostTempoAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["client_id"] = clientId,
            ["client_secret"] = clientSecret, ["redirect_uri"] = options.Value.TempoRedirectUri,
            ["refresh_token"] = refreshToken
        }, cancellationToken);

    public async Task<string?> JiraSiteUrlAsync(Guid cloudId, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.atlassian.com/oauth/token/accessible-resources");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
        foreach (var resource in document.RootElement.EnumerateArray())
        {
            if (resource.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var parsed)
                && parsed == cloudId && resource.TryGetProperty("url", out var url))
            {
                var siteUrl = url.GetString();
                if (Uri.TryCreate(siteUrl, UriKind.Absolute, out var site) && site.Scheme == Uri.UriSchemeHttps
                    && site.Port == 443 && site.UserInfo.Length == 0 && site.Query.Length == 0 && site.Fragment.Length == 0
                    && site.AbsolutePath == "/"
                    && site.Host.EndsWith(".atlassian.net", StringComparison.OrdinalIgnoreCase))
                    return site.GetLeftPart(UriPartial.Authority);
            }
        }

        return null;
    }

    private async Task<OAuthTokens> PostTempoAsync(Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync("https://api.tempo.io/oauth/token/", new FormUrlEncodedContent(fields), cancellationToken);
        return await ReadTokensAsync(response, cancellationToken);
    }

    private async Task<OAuthTokens> ReadTokensAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OAuth token exchange failed ({(int)response.StatusCode}).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var accessToken = root.GetProperty("access_token").GetString();
        var refreshToken = root.GetProperty("refresh_token").GetString();
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken) || expiresIn <= 0)
        {
            throw new JsonException("OAuth provider returned incomplete tokens.");
        }

        return new OAuthTokens(accessToken, refreshToken, timeProvider.GetUtcNow().AddSeconds(expiresIn));
    }
}
