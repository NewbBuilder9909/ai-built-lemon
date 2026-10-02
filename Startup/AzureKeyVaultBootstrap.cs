using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;

namespace ProgrammePulse.Startup;

/// <summary>
/// Loads configuration and secret material from Azure Key Vault through the
/// managed identity attached to the host. This keeps secrets out of source
/// control, appsettings files, and other plaintext config locations while
/// preserving the existing configuration contract used throughout the app.
/// </summary>
public static class AzureKeyVaultBootstrap
{
    public static void Configure(WebApplicationBuilder builder)
    {
        var settings = AzureKeyVaultSettings.FromConfiguration(builder.Configuration);
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            return;
        }

        var vaultUri = new Uri(settings.Endpoint, UriKind.Absolute);
        var credential = CreateManagedIdentityCredential();
        builder.Configuration.AddAzureKeyVault(vaultUri, credential);
        builder.Services.AddSingleton(_ => new SecretClient(vaultUri, credential));
    }

    private static Azure.Core.TokenCredential CreateManagedIdentityCredential()
    {
        var credentialType = Type.GetType("Azure.Identity.ManagedIdentityCredential, Azure.Identity");
        if (credentialType is null)
        {
            throw new InvalidOperationException("Azure managed identity support is not available because Azure.Identity could not be loaded.");
        }

        var instance = Activator.CreateInstance(credentialType);
        if (instance is not Azure.Core.TokenCredential credential)
        {
            throw new InvalidOperationException("The resolved Azure managed identity credential type was not compatible with Azure.Core.TokenCredential.");
        }

        return credential;
    }
}

public sealed class AzureKeyVaultSettings
{
    public const string SectionName = "AzureKeyVault";

    public static AzureKeyVaultSettings FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var endpoint = FirstNonEmpty(
            section["Endpoint"],
            section["VaultUri"],
            configuration["AZURE_KEY_VAULT_ENDPOINT"],
            configuration["AZURE_KEY_VAULT_URI"],
            configuration["AzureKeyVault__Endpoint"],
            configuration["AzureKeyVault__VaultUri"]);

        return new AzureKeyVaultSettings
        {
            Endpoint = endpoint,
            Enabled = !string.IsNullOrWhiteSpace(endpoint)
        };
    }

    public string? Endpoint { get; init; }

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
