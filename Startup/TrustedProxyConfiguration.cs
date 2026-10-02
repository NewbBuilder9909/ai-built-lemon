using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace ProgrammePulse.Startup;

public static class TrustedProxyConfiguration
{
    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var entry in configuration.GetSection("ReverseProxy:KnownProxies").GetChildren())
            {
                if (!IPAddress.TryParse(entry.Value, out var address))
                    throw new InvalidOperationException("ReverseProxy:KnownProxies must contain IP addresses.");
                options.KnownProxies.Add(address);
            }
            if (options.KnownProxies.Count == 0)
                throw new InvalidOperationException("ReverseProxy:KnownProxies must not be empty when proxy mode is enabled.");
        });
    }

    public static void Validate(IConfiguration configuration, ICollection<string> errors)
    {
        if (configuration.GetValue<bool>("ASPNETCORE_FORWARDEDHEADERS_ENABLED")
            || configuration.GetValue<bool>("FORWARDEDHEADERS_ENABLED"))
            errors.Add("Automatic forwarded headers must be disabled. Use ReverseProxy:Enabled and explicit KnownProxies instead.");
        if (!bool.TryParse(configuration["ReverseProxy:Enabled"], out var enabled))
        {
            errors.Add("ReverseProxy:Enabled must explicitly be true (trusted proxy) or false (direct HTTPS).");
            return;
        }
        if (!enabled) return;
        var proxies = configuration.GetSection("ReverseProxy:KnownProxies").GetChildren().ToArray();
        if (proxies.Length == 0 || proxies.Any(p => !IPAddress.TryParse(p.Value, out _)))
            errors.Add("ReverseProxy:KnownProxies must contain explicit proxy IP addresses when ReverseProxy:Enabled is true.");
    }
}
