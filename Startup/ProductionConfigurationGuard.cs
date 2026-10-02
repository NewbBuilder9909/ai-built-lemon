using Microsoft.Extensions.Configuration;

namespace ProgrammePulse.Startup;

/// <summary>
/// Fail-fast validation of the configuration a non-Development deployment
/// must have before Umbraco is allowed to boot. Runs from Program.cs after
/// the host is built and before BootUmbracoAsync — so a misconfigured
/// container/App Service exits with one clear message in the log instead
/// of, e.g., booting against a LocalDB connection string that only exists
/// on a developer laptop, or presenting Umbraco's unauthenticated installer
/// because the connection string was empty.
///
/// Pure and static (IConfiguration + environment name in, findings out) so
/// it's unit-testable without a host — see ProductionConfigurationGuardTests.
/// Errors abort startup; warnings are logged and startup continues.
/// </summary>
public static class ProductionConfigurationGuard
{
    public const string ConnectionStringName = "umbracoDbDSN";

    public sealed record Findings(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
    {
        public bool HasErrors => Errors.Count > 0;
    }

    public static Findings Validate(IConfiguration configuration, string environmentName)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (!isDevelopment)
        {
            TrustedProxyConfiguration.Validate(configuration, errors);
            ValidateLegacyIdentityConfiguration(configuration, errors);
            ValidateSecretSources(configuration, errors);
            foreach (var key in new[] { "ClickUp:ApiToken", "HubPlanner:ApiKey" })
                if (!string.IsNullOrWhiteSpace(configuration[key]))
                    errors.Add($"{key} must be empty outside Development; use tenant-owned credentials.");
            if (configuration.GetValue<bool>("ProgrammeOps:AllowSharedSourceCredentials"))
                errors.Add("ProgrammeOps:AllowSharedSourceCredentials must be false outside Development. Connect each tenant to its own source account.");
            RequireAbsolutePath(configuration, "DataProtection:KeyRingDirectory", errors);

            var keyVaultCertificateName = FirstNonEmpty(configuration, "DataProtection:KeyVaultCertificateName", "DataProtection:CertificateName", "DataProtection__KeyVaultCertificateName", "DataProtection__CertificateName");
            var localCertificatePath = configuration["DataProtection:CertificatePath"];
            var localCertificatePassword = configuration["DataProtection:CertificatePassword"];
            var hasKeyVaultCertificate = !string.IsNullOrWhiteSpace(keyVaultCertificateName);
            var hasLocalCertificate = !string.IsNullOrWhiteSpace(localCertificatePath) && !string.IsNullOrWhiteSpace(localCertificatePassword);

            if (!hasKeyVaultCertificate && !hasLocalCertificate)
            {
                errors.Add("A production Data Protection certificate must be configured either through DataProtection:KeyVaultCertificateName (recommended) or through DataProtection:CertificatePath + DataProtection:CertificatePassword.");
            }

            if (hasLocalCertificate)
            {
                RequireAbsolutePath(configuration, "DataProtection:CertificatePath", errors);
                if (string.IsNullOrWhiteSpace(localCertificatePassword))
                    errors.Add("DataProtection:CertificatePassword is required outside Development.");
            }
            else if (hasKeyVaultCertificate)
            {
                var keyVaultEndpoint = FirstNonEmpty(configuration, "AzureKeyVault:Endpoint", "AzureKeyVault:VaultUri", "AzureKeyVault__Endpoint", "AzureKeyVault__VaultUri", "AZURE_KEY_VAULT_ENDPOINT", "AZURE_KEY_VAULT_URI");
                if (string.IsNullOrWhiteSpace(keyVaultEndpoint))
                    errors.Add("AzureKeyVault:Endpoint is required outside Development when DataProtection:KeyVaultCertificateName is used.");
            }

            var previousKeyVaultCertificateName = FirstNonEmpty(configuration, "DataProtection:PreviousKeyVaultCertificateName", "DataProtection:PreviousCertificateName", "DataProtection__PreviousKeyVaultCertificateName", "DataProtection__PreviousCertificateName");
            var previousCertificatePath = configuration["DataProtection:PreviousCertificatePath"];
            var previousCertificatePassword = configuration["DataProtection:PreviousCertificatePassword"];
            var hasPreviousLocalCertificate = !string.IsNullOrWhiteSpace(previousCertificatePath) || !string.IsNullOrWhiteSpace(previousCertificatePassword);
            var hasPreviousKeyVaultCertificate = !string.IsNullOrWhiteSpace(previousKeyVaultCertificateName);
            if (hasPreviousLocalCertificate || hasPreviousKeyVaultCertificate)
            {
                if (hasPreviousLocalCertificate)
                {
                    RequireAbsolutePath(configuration, "DataProtection:PreviousCertificatePath", errors);
                    if (string.IsNullOrWhiteSpace(previousCertificatePassword))
                        errors.Add("DataProtection:PreviousCertificatePassword is required when rotating a certificate.");
                }
                else if (string.IsNullOrWhiteSpace(FirstNonEmpty(configuration, "AzureKeyVault:Endpoint", "AzureKeyVault:VaultUri", "AzureKeyVault__Endpoint", "AzureKeyVault__VaultUri", "AZURE_KEY_VAULT_ENDPOINT", "AZURE_KEY_VAULT_URI")))
                {
                    errors.Add("AzureKeyVault:Endpoint is required when rotating a Data Protection certificate from Azure Key Vault.");
                }
            }
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                errors.Add($"ConnectionStrings:{ConnectionStringName} is empty. Set it via the CONNECTIONSTRINGS__UMBRACODBDSN environment variable (or your secret store) — an empty connection string would boot Umbraco into its unauthenticated installer.");
            }
            else if (connectionString.Contains("(localdb)", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"ConnectionStrings:{ConnectionStringName} points at a LocalDB instance, which only exists on a developer machine. Provide the target environment's SQL Server connection string.");
            }

            if (!configuration.GetValue<bool>("Umbraco:CMS:Global:UseHttps"))
            {
                errors.Add("Umbraco:CMS:Global:UseHttps must be true outside Development so Umbraco's own authentication cookies are issued Secure.");
            }

            if (configuration.GetValue<bool>("Umbraco:CMS:Hosting:Debug"))
            {
                errors.Add("Umbraco:CMS:Hosting:Debug must be false outside Development — it exposes detailed error pages.");
            }

        }

        var clickUpWorkspaceId = configuration["ClickUp:WorkspaceId"];
        var clickUpToken = configuration["ClickUp:ApiToken"];
        if (isDevelopment && configuration.GetValue<bool>("ProgrammeOps:AllowSharedSourceCredentials")
            && !string.IsNullOrWhiteSpace(clickUpWorkspaceId) && string.IsNullOrWhiteSpace(clickUpToken))
        {
            warnings.Add("ClickUp:WorkspaceId is set but ClickUp:ApiToken is empty; local shared-credential sync requires CLICKUP__APITOKEN.");
        }

        ValidateBaseUrl(configuration, "ClickUp:BaseUrl", errors);
        ValidateBaseUrl(configuration, "HubPlanner:BaseUrl", errors);

        return new Findings(errors, warnings);
    }

    private static string? FirstNonEmpty(IConfiguration configuration, params string[] keys)
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

    private static void RequireAbsolutePath(IConfiguration configuration, string key, List<string> errors)
    {
        var path = configuration[key];
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            errors.Add($"{key} must be an absolute path outside Development.");
    }

    private static void ValidateBaseUrl(IConfiguration configuration, string key, List<string> errors)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var provider = key.Split(':')[0];
        if (!Services.Integrations.OutboundEndpointPolicy.IsAllowedBaseUrl(value, provider))
        {
            errors.Add($"{key} must use the approved HTTPS provider host and API base path without user info, a custom port, query or fragment.");
        }
    }

    private static void ValidateLegacyIdentityConfiguration(IConfiguration configuration, List<string> errors)
    {
        var legacyKeys = configuration.AsEnumerable()
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
            .Select(kvp => kvp.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(UsesLegacyWindowsAdPattern)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var key in legacyKeys)
        {
            errors.Add($"{key} configures legacy Windows AD / LDAP identity. Remove it and use Microsoft Entra ID instead.");
        }
    }

    private static bool UsesLegacyWindowsAdPattern(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;

        var normalized = key
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace(":", string.Empty)
            .Replace(".", string.Empty)
            .Replace("/", string.Empty)
            .Replace("\\", string.Empty)
            .ToLowerInvariant();

        if (normalized.Contains("microsoftentra") || normalized.Contains("azuread") || normalized.Contains("oidc") || normalized.Contains("oauth"))
            return false;

        return normalized.Contains("windowsad")
            || normalized.Contains("windowsauthentication")
            || normalized.Contains("activedirectory")
            || normalized.Contains("directoryservice")
            || normalized.Contains("ldap")
            || normalized.Contains("adal")
            || normalized.Contains("domaincontroller")
            || normalized.Contains("authenticationwindows");
    }

    private static void ValidateSecretSources(IConfiguration configuration, List<string> errors)
    {
        if (configuration is not IConfigurationRoot root) return;
        string[] secretKeys = ["ConnectionStrings:umbracoDbDSN", "DataProtection:CertificatePassword",
            "DataProtection:PreviousCertificatePassword", "ConnectorOAuth:JiraClientSecret", "ClickUp:ApiToken", "HubPlanner:ApiKey",
            "Umbraco:CMS:Imaging:HMACSecretKey", "Umbraco:CMS:Unattended:UnattendedUserPassword"];
        foreach (var provider in root.Providers.OfType<FileConfigurationProvider>())
            foreach (var key in secretKeys)
                if (provider.TryGet(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    errors.Add($"{key} must not be stored in configuration files outside Development. Inject it from the deployment secret store.");
    }
}
