namespace ProgrammePulse.Services.Integrations;

/// <summary>Fixed provider origins and path boundaries, checked before credentials leave the process.</summary>
public static class OutboundEndpointPolicy
{
    public static bool IsAllowed(Uri uri, string provider)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        var (host, prefix) = provider switch
        {
            "ClickUp" => ("api.clickup.com", "/api/v2"),
            "HubPlanner" => ("api.hubplanner.com", "/v1"),
            "Jira" => ("api.atlassian.com", "/ex/jira"),
            "Tempo" => ("api.tempo.io", "/4/worklogs"),
            "TempoAudit" => ("api.tempo.io", "/audit/1/events/deleted/types/worklog"),
            _ => ("", "")
        };
        return host.Length > 0 && uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            && (uri.AbsolutePath == prefix || uri.AbsolutePath.StartsWith(prefix + "/", StringComparison.Ordinal));
    }

    public static void RequireAllowed(Uri uri, string provider)
    {
        if (!IsAllowed(uri, provider))
            throw new InvalidOperationException($"Outbound endpoint is not allowed for {provider}.");
    }

    public static bool IsAllowedBaseUrl(string value, string provider) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && IsAllowed(uri, provider)
        && uri.Query.Length == 0 && uri.AbsolutePath.TrimEnd('/') == (provider == "ClickUp" ? "/api/v2" : "/v1");

    // Redirects can bypass destination validation, and pooled cookies can cross tenant boundaries.
    public static HttpClientHandler CreateHandler() => new() { AllowAutoRedirect = false, UseCookies = false };
}
