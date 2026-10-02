using Microsoft.Extensions.Configuration;
using ProgrammePulse.Startup;

namespace ProgrammePulse.Tests.Startup;

public class OpenTelemetrySettingsTests
{
    [Fact]
    public void Missing_monitor_connection_string_disables_open_telemetry()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var settings = OpenTelemetrySettings.FromConfiguration(configuration);

        Assert.False(settings.Enabled);
        Assert.Null(settings.ConnectionString);
    }

    [Theory]
    [InlineData("OpenTelemetry:ApplicationInsights:ConnectionString", "InstrumentationKey=abc;IngestionEndpoint=https://example/;LiveEndpoint=https://example/")]
    [InlineData("APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=abc;IngestionEndpoint=https://example/;LiveEndpoint=https://example/")]
    public void Azure_monitor_connection_string_is_loaded_from_supported_configuration_keys(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [key] = value
            })
            .Build();

        var settings = OpenTelemetrySettings.FromConfiguration(configuration);

        Assert.True(settings.Enabled);
        Assert.Equal(value, settings.ConnectionString);
    }
}
