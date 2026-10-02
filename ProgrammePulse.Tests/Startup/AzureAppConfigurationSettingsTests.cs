using System.IO;
using Microsoft.Extensions.Configuration;
using ProgrammePulse.Startup;

namespace ProgrammePulse.Tests.Startup;

public class AzureAppConfigurationSettingsTests
{
    [Fact]
    public void Missing_endpoint_and_connection_string_disables_azure_app_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var settings = AzureAppConfigurationSettings.FromConfiguration(configuration);

        Assert.False(settings.Enabled);
        Assert.Null(settings.Endpoint);
        Assert.Null(settings.ConnectionString);
    }

    [Fact]
    public void Base_appsettings_file_does_not_embed_azure_app_configuration_defaults()
    {
        // Walk up to the repository root rather than a fixed "../../../..":
        // the depth of the test binaries changes under --artifacts-path,
        // which is how CI and the consolidation runbook build.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProgrammePulse.csproj")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var content = File.ReadAllText(Path.Combine(directory.FullName, "appsettings.json"));

        Assert.DoesNotContain("\"AzureAppConfiguration\"", content);
        Assert.DoesNotContain("AZURE_APPCONFIG_CONNECTION_STRING", content);
    }

    [Fact]
    public void Environment_specific_values_are_loaded_from_the_azure_app_configuration_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAppConfiguration:ConnectionString"] = "Endpoint=https://contoso-config.azconfig.io;Id=app-config-id;Secret=app-config-secret",
                ["AzureAppConfiguration:Label"] = "Production",
                ["AzureAppConfiguration:SentinelKey"] = "AppConfig:Sentinel",
                ["AzureAppConfiguration:Refresh:CacheExpirationMinutes"] = "5"
            })
            .Build();

        var settings = AzureAppConfigurationSettings.FromConfiguration(configuration);

        Assert.True(settings.Enabled);
        Assert.Equal("Endpoint=https://contoso-config.azconfig.io;Id=app-config-id;Secret=app-config-secret", settings.ConnectionString);
        Assert.Equal("Production", settings.Label);
        Assert.Equal("AppConfig:Sentinel", settings.SentinelKey);
        Assert.Equal(TimeSpan.FromMinutes(5), settings.RefreshCacheExpiration);
    }
}
