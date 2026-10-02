using Microsoft.Extensions.Configuration;
using ProgrammePulse.Composers;
using ProgrammePulse.Startup;

namespace ProgrammePulse.Tests.Startup;

/// <summary>
/// Locks in the fail-fast contract Program.cs relies on: a non-Development
/// environment with developer-only or insecure configuration must refuse to
/// start, while Development keeps working with its LocalDB defaults.
/// </summary>
public class ProductionConfigurationGuardTests
{
    private static IConfiguration Build(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static (string, string?)[] ProductionReady() =>
    [
        ("DataProtection:KeyRingDirectory", Path.Combine(Path.GetTempPath(), "pp-test-keys")),
        ("DataProtection:CertificatePath", Path.Combine(Path.GetTempPath(), "pp-test-certificate.pfx")),
        ("DataProtection:CertificatePassword", "test-only-password"),
        ("ConnectionStrings:umbracoDbDSN", "Server=sql.internal;Database=Ops;User Id=ops;Password=x;"),
        ("Umbraco:CMS:Global:UseHttps", "true"),
        ("Umbraco:CMS:Hosting:Debug", "false"),
        ("ReverseProxy:Enabled", "false"),
        ("ClickUp:BaseUrl", "https://api.clickup.com/api/v2"),
        ("HubPlanner:BaseUrl", "https://api.hubplanner.com/v1")
    ];

    [Fact]
    public void Production_ready_configuration_has_no_errors()
    {
        var findings = ProductionConfigurationGuard.Validate(Build(ProductionReady()), "Production");

        Assert.False(findings.HasErrors, string.Join(" | ", findings.Errors));
        Assert.Empty(findings.Warnings);
    }

    [Fact]
    public void Production_requires_a_durable_encrypted_data_protection_key_ring()
    {
        var values = ProductionReady().Where(v => !v.Item1.StartsWith("DataProtection:", StringComparison.Ordinal)).ToArray();

        var findings = ProductionConfigurationGuard.Validate(Build(values), "Production");

        Assert.Contains(findings.Errors, error => error.Contains("KeyRingDirectory"));
        Assert.Contains(findings.Errors, error => error.Contains("CertificatePath"));
        Assert.Contains(findings.Errors, error => error.Contains("CertificatePassword"));
    }

    [Fact]
    public void Production_accepts_key_vault_certificate_configuration_without_local_file_paths()
    {
        var values = ProductionReady()
            .Where(v => v.Item1 != "DataProtection:KeyRingDirectory"
                && v.Item1 != "DataProtection:CertificatePath"
                && v.Item1 != "DataProtection:CertificatePassword")
            .Concat(
            [
                ("DataProtection:KeyVaultCertificateName", "programme-pulse-data-protection"),
                ("AzureKeyVault:Endpoint", "https://pp-demo.vault.azure.net/")
            ])
            .ToArray();

        var findings = ProductionConfigurationGuard.Validate(Build(values), "Production");

        Assert.DoesNotContain(findings.Errors, error => error.Contains("CertificatePath"));
        Assert.DoesNotContain(findings.Errors, error => error.Contains("CertificatePassword"));
        Assert.DoesNotContain(findings.Errors, error => error.Contains("KeyVaultCertificateName"));
    }

    [Fact]
    public void Production_rejects_shared_source_credentials()
    {
        var findings = ProductionConfigurationGuard.Validate(
            Build([.. ProductionReady(), ("ProgrammeOps:AllowSharedSourceCredentials", "true")]), "Production");
        Assert.Contains(findings.Errors, error => error.Contains("AllowSharedSourceCredentials"));
    }

    [Fact]
    public void Production_rejects_file_secrets_even_when_overridden_and_does_not_echo_them()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """{"ConnectorOAuth":{"JiraClientSecret":"never-echo-this"}}""");
            var config = new ConfigurationBuilder().AddJsonFile(path)
                .AddInMemoryCollection(ProductionReady().Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2)))
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectorOAuth:JiraClientSecret"] = "override" }).Build();
            var findings = ProductionConfigurationGuard.Validate(config, "Production");
            Assert.Contains(findings.Errors, e => e.Contains("JiraClientSecret"));
            Assert.DoesNotContain("never-echo-this", string.Join(" ", findings.Errors));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("ClickUp:ApiToken")]
    [InlineData("HubPlanner:ApiKey")]
    public void Production_rejects_unused_deployment_wide_provider_tokens(string key)
    {
        var findings = ProductionConfigurationGuard.Validate(Build([.. ProductionReady(), (key, "test-token")]), "Production");
        Assert.Contains(findings.Errors, e => e.Contains(key));
    }

    [Fact]
    public void Production_rejects_legacy_windows_ad_identity_configuration()
    {
        var values = ProductionReady()
            .Concat([
                ("Authentication:WindowsAd:Domain", "CORP"),
                ("DirectoryServices:DomainController", "ldap://dc01.corp.local")
            ])
            .ToArray();

        var findings = ProductionConfigurationGuard.Validate(Build(values), "Production");

        Assert.Contains(findings.Errors, e => e.Contains("Windows AD", StringComparison.OrdinalIgnoreCase)
            || e.Contains("Microsoft Entra ID", StringComparison.OrdinalIgnoreCase)
            || e.Contains("Active Directory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Development_tolerates_localdb_and_debug_settings()
    {
        var config = Build(
            ("ConnectionStrings:umbracoDbDSN", "Server=(localdb)\\Dev;Database=UmbracoBase;Integrated Security=true;"),
            ("Umbraco:CMS:Hosting:Debug", "true"));

        var findings = ProductionConfigurationGuard.Validate(config, "Development");

        Assert.False(findings.HasErrors, string.Join(" | ", findings.Errors));
    }

    [Fact]
    public void Production_rejects_an_empty_connection_string()
    {
        var values = ProductionReady().Where(v => v.Item1 != "ConnectionStrings:umbracoDbDSN").ToArray();

        var findings = ProductionConfigurationGuard.Validate(Build(values), "Production");

        Assert.Contains(findings.Errors, e => e.Contains("umbracoDbDSN is empty"));
    }

    [Fact]
    public void Production_rejects_a_localdb_connection_string()
    {
        var values = ProductionReady()
            .Select(v => v.Item1 == "ConnectionStrings:umbracoDbDSN" ? (v.Item1, (string?)"Server=(LocalDb)\\MSSQLLocalDB;Database=X;") : v)
            .ToArray();

        var findings = ProductionConfigurationGuard.Validate(Build(values), "Production");

        Assert.Contains(findings.Errors, e => e.Contains("LocalDB"));
    }

    [Fact]
    public void Production_rejects_UseHttps_false_and_Hosting_Debug_true()
    {
        var values = ProductionReady()
            .Select(v => v.Item1 switch
            {
                "Umbraco:CMS:Global:UseHttps" => (v.Item1, (string?)"false"),
                "Umbraco:CMS:Hosting:Debug" => (v.Item1, (string?)"true"),
                _ => v
            })
            .ToArray();

        var findings = ProductionConfigurationGuard.Validate(Build(values), "Production");

        Assert.Contains(findings.Errors, e => e.Contains("UseHttps"));
        Assert.Contains(findings.Errors, e => e.Contains("Hosting:Debug"));
    }

    [Fact]
    public void Staging_is_treated_like_production()
    {
        var findings = ProductionConfigurationGuard.Validate(Build(), "Staging");

        Assert.True(findings.HasErrors);
    }

    [Fact]
    public void Missing_proxy_mode_is_a_startup_error()
    {
        var values = ProductionReady().Where(v => v.Item1 != "ReverseProxy:Enabled").ToArray();

        var findings = ProductionConfigurationGuard.Validate(Build(values), "Production");

        Assert.True(findings.HasErrors);
        Assert.Contains(findings.Errors, w => w.Contains("ReverseProxy:Enabled"));
    }

    [Fact]
    public void Workspace_without_token_warns_only_for_local_shared_credential_mode()
    {
        var config = Build(("ClickUp:WorkspaceId", "123"), ("ProgrammeOps:AllowSharedSourceCredentials", "true"));

        var findings = ProductionConfigurationGuard.Validate(config, "Development");

        Assert.Contains(findings.Warnings, w => w.Contains("ClickUp:ApiToken"));
    }

    [Theory]
    [InlineData("http://api.clickup.com/api/v2")]
    [InlineData("not a url")]
    public void Non_https_integration_base_url_is_an_error(string baseUrl)
    {
        var config = Build(("ClickUp:BaseUrl", baseUrl));

        var findings = ProductionConfigurationGuard.Validate(config, "Development");

        Assert.Contains(findings.Errors, e => e.Contains("ClickUp:BaseUrl"));
    }

    [Fact]
    public void Empty_integration_base_urls_are_safe_for_typed_client_registration()
    {
        var clickUp = ProgrammeOperationsComposer.CreateSafeBaseAddress(null, "ClickUp");
        var hubPlanner = ProgrammeOperationsComposer.CreateSafeBaseAddress(string.Empty, "HubPlanner");

        Assert.Equal("https://example.invalid/", clickUp.ToString());
        Assert.Equal("https://example.invalid/", hubPlanner.ToString());
    }
}
