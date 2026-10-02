using Microsoft.Extensions.Configuration.AzureAppConfiguration;

namespace ProgrammePulse.Startup;

/// <summary>
/// Opts the app into Azure App Configuration when the deployment provides a
/// configuration endpoint or connection string. Environment-specific values are
/// supplied via deployment configuration or environment variables and are not
/// committed in source control or local appsettings files.
/// </summary>
public static class AzureAppConfigurationBootstrap
{
    public static void Configure(WebApplicationBuilder builder)
    {
        var settings = AzureAppConfigurationSettings.FromConfiguration(builder.Configuration);
        if (!settings.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            return;
        }

        builder.Configuration.AddAzureAppConfiguration(options =>
        {
            options.Connect(settings.ConnectionString);

            if (!string.IsNullOrWhiteSpace(settings.Label))
            {
                options.Select(KeyFilter.Any, settings.Label);
            }
            else
            {
                options.Select(KeyFilter.Any);
            }

            options.ConfigureRefresh(refresh =>
            {
                refresh.Register(settings.SentinelKey, refreshAll: true)
                    .SetRefreshInterval(settings.RefreshCacheExpiration);
            });
        }, optional: true);

        builder.Services.AddAzureAppConfiguration();
    }
}

public sealed class AzureAppConfigurationSettings
{
    public const string SectionName = "AzureAppConfiguration";

    public static AzureAppConfigurationSettings FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var endpoint = FirstNonEmpty(
            section["Endpoint"],
            configuration["AZURE_APPCONFIG_ENDPOINT"],
            configuration["AzureAppConfiguration__Endpoint"]);
        var connectionString = FirstNonEmpty(
            section["ConnectionString"],
            configuration["AZURE_APPCONFIG_CONNECTION_STRING"],
            configuration["AzureAppConfiguration__ConnectionString"]);
        var label = FirstNonEmpty(
            section["Label"],
            configuration["AZURE_APPCONFIG_LABEL"],
            configuration["AzureAppConfiguration__Label"]);
        var sentinelKey = FirstNonEmpty(
            section["SentinelKey"],
            section["Refresh:SentinelKey"],
            configuration["AZURE_APPCONFIG_SENTINEL_KEY"],
            configuration["AzureAppConfiguration__SentinelKey"]) ?? "AppConfig:Sentinel";
        var cacheExpirationMinutes = FirstNonEmpty(
            section["Refresh:CacheExpirationMinutes"],
            configuration["AzureAppConfiguration__Refresh__CacheExpirationMinutes"]) ?? "5";

        var refreshCacheExpiration = int.TryParse(cacheExpirationMinutes, out var minutes)
            ? TimeSpan.FromMinutes(minutes)
            : TimeSpan.FromMinutes(5);

        return new AzureAppConfigurationSettings
        {
            Endpoint = endpoint,
            ConnectionString = connectionString,
            Label = label,
            SentinelKey = sentinelKey,
            RefreshCacheExpiration = refreshCacheExpiration,
            Enabled = !string.IsNullOrWhiteSpace(connectionString)
        };
    }

    public string? Endpoint { get; init; }

    public string? ConnectionString { get; init; }

    public string? Label { get; init; }

    public string SentinelKey { get; init; } = "AppConfig:Sentinel";

    public TimeSpan RefreshCacheExpiration { get; init; } = TimeSpan.FromMinutes(5);

    public bool Enabled { get; init; }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
