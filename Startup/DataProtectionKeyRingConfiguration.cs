using System.Security.Cryptography.X509Certificates;
using Azure.Security.KeyVault.Certificates;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;

namespace ProgrammePulse.Startup;

/// <summary>Uses a shared, durable and certificate-encrypted key ring outside Development.</summary>
public static class DataProtectionKeyRingConfiguration
{
    public static void Configure(IServiceCollection services, IConfiguration configuration, string environmentName)
    {
        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)) return;

        var azureConnectionString = ResolveSetting(configuration, "AzureStorage:ConnectionString", "AzureStorage__ConnectionString", "BlobStorage:ConnectionString", "BlobStorage__ConnectionString", "DataProtection:BlobConnectionString", "DataProtection__BlobConnectionString");
        var azureContainerName = ResolveSetting(configuration, "AzureStorage:ContainerName", "AzureStorage__ContainerName", "BlobStorage:ContainerName", "BlobStorage__ContainerName", "DataProtection:BlobContainerName", "DataProtection__BlobContainerName");

        var certificatePath = configuration["DataProtection:CertificatePath"];
        var certificatePassword = configuration["DataProtection:CertificatePassword"];
        var keyVaultCertificateName = ResolveSetting(configuration, "DataProtection:KeyVaultCertificateName", "DataProtection__KeyVaultCertificateName", "DataProtection:CertificateName", "DataProtection__CertificateName");
        var hasCertificate = (!string.IsNullOrWhiteSpace(certificatePath) && !string.IsNullOrWhiteSpace(certificatePassword))
            || !string.IsNullOrWhiteSpace(keyVaultCertificateName);

        if (!hasCertificate && string.IsNullOrWhiteSpace(azureConnectionString) && string.IsNullOrWhiteSpace(azureContainerName))
        {
            return;
        }

        var protection = services.AddDataProtection().SetApplicationName("ProgrammePulse");

        if (!string.IsNullOrWhiteSpace(azureConnectionString) && !string.IsNullOrWhiteSpace(azureContainerName))
        {
            var blobClient = new BlobClient(azureConnectionString, azureContainerName, "data-protection-keys.xml");
            protection.PersistKeysToAzureBlobStorage(blobClient);
        }
        else
        {
            var directory = configuration["DataProtection:KeyRingDirectory"];
            if (string.IsNullOrWhiteSpace(directory)) return;
            protection.PersistKeysToFileSystem(new DirectoryInfo(directory));
        }

        if (hasCertificate)
        {
            protection.ProtectKeysWithCertificate(LoadCertificate(configuration, certificatePath, certificatePassword, keyVaultCertificateName));
            var previousPath = configuration["DataProtection:PreviousCertificatePath"];
            var previousPassword = configuration["DataProtection:PreviousCertificatePassword"];
            var previousKeyVaultCertificateName = ResolveSetting(configuration, "DataProtection:PreviousKeyVaultCertificateName", "DataProtection__PreviousKeyVaultCertificateName", "DataProtection:PreviousCertificateName", "DataProtection__PreviousCertificateName");
            if ((!string.IsNullOrWhiteSpace(previousPath) && !string.IsNullOrWhiteSpace(previousPassword)) || !string.IsNullOrWhiteSpace(previousKeyVaultCertificateName))
                protection.UnprotectKeysWithAnyCertificate(LoadCertificate(configuration, previousPath, previousPassword, previousKeyVaultCertificateName));
        }
    }

    private static string? ResolveSetting(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value)) return value;

            var environmentVariableName = key.Replace(':', '_').Replace("__", "_").Replace("-", "_").ToUpperInvariant();
            var environmentValue = Environment.GetEnvironmentVariable(environmentVariableName);
            if (!string.IsNullOrWhiteSpace(environmentValue)) return environmentValue;
        }

        return null;
    }

    private static X509Certificate2 LoadCertificate(IConfiguration configuration, string? path, string? password, string? keyVaultCertificateName)
    {
        if (!string.IsNullOrWhiteSpace(keyVaultCertificateName))
        {
            return LoadCertificateFromKeyVault(configuration, keyVaultCertificateName);
        }

        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("The Data Protection certificate path is required when protecting the key ring.");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("The Data Protection certificate password is required when protecting the key ring.");

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException("The Data Protection certificate must include its private key.");
        return certificate;
    }

    private static X509Certificate2 LoadCertificateFromKeyVault(IConfiguration configuration, string certificateName)
    {
        var keyVaultEndpoint = ResolveSetting(configuration, "AzureKeyVault:Endpoint", "AzureKeyVault__Endpoint", "AZURE_KEY_VAULT_ENDPOINT", "AzureKeyVault:VaultUri", "AzureKeyVault__VaultUri", "AZURE_KEY_VAULT_URI");
        if (string.IsNullOrWhiteSpace(keyVaultEndpoint))
            throw new InvalidOperationException("The Azure Key Vault certificate configuration requires AzureKeyVault:Endpoint or AzureKeyVault:VaultUri when DataProtection:KeyVaultCertificateName is used.");

        var credentialType = Type.GetType("Azure.Identity.ManagedIdentityCredential, Azure.Identity");
        if (credentialType is null)
            throw new InvalidOperationException("Azure managed identity support is not available because Azure.Identity could not be loaded.");

        var instance = Activator.CreateInstance(credentialType);
        if (instance is not Azure.Core.TokenCredential credential)
            throw new InvalidOperationException("The resolved Azure managed identity credential type was not compatible with Azure.Core.TokenCredential.");

        var vaultUri = new Uri(keyVaultEndpoint, UriKind.Absolute);
        var certificateClient = new CertificateClient(vaultUri, credential);
        var certificate = certificateClient.DownloadCertificate(certificateName);
        if (!certificate.Value.HasPrivateKey)
            throw new InvalidOperationException($"The Data Protection certificate '{certificateName}' from Azure Key Vault must include its private key.");

        return certificate.Value;
    }
}
