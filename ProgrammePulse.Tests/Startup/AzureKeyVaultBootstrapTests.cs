using Microsoft.Extensions.Configuration;
using ProgrammePulse.Startup;

namespace ProgrammePulse.Tests.Startup;

public class AzureKeyVaultBootstrapTests
{
    [Theory]
    [InlineData("AzureKeyVault:Endpoint", "https://pp-demo.vault.azure.net/")]
    [InlineData("AzureKeyVault__Endpoint", "https://pp-demo.vault.azure.net/")]
    [InlineData("AZURE_KEY_VAULT_ENDPOINT", "https://pp-demo.vault.azure.net/")]
    public void Key_vault_settings_are_detected_from_standard_configuration_locations(string key, string value)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [key] = value
            })
            .Build();

        var settings = AzureKeyVaultSettings.FromConfiguration(config);

        Assert.True(settings.Enabled);
        Assert.Equal(value, settings.Endpoint);
    }
}
