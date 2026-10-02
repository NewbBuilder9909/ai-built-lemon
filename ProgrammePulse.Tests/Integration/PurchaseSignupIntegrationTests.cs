using System.Net;
using ProgrammePulse.Tests.Personas;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Tenancy;
using Umbraco.Cms.Web.Common.Security;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class PurchaseSignupIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the public self-service signup never provisioned a trial tenant and first admin account.";

    [Fact]
    public async Task Start_trial_creates_a_tenant_admin_and_selected_add_ons()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var enabledFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Commercial:SelfServiceTrialEnabled"] = "true"
                })));
        using var client = enabledFactory.CreateClient(new() { AllowAutoRedirect = false });
        var session = new PersonaSession(client, "anonymous");
        var token = await session.TryGetAntiForgeryTokenAsync("/purchase");
        Assert.False(string.IsNullOrWhiteSpace(token));

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"trial-{suffix}@example.com";
        var shortCode = $"trial-{suffix}";

        using var response = await client.PostAsync("/purchase/start-trial", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token!,
            ["companyName"] = $"Trial {suffix}",
            ["shortCode"] = shortCode,
            ["adminFullName"] = "Trial Admin",
            ["adminEmail"] = email,
            ["password"] = "Trial-P@ssword-1",
            ["confirmPassword"] = "Trial-P@ssword-1",
            ["plan"] = TenantPlan.Starter,
            ["selectedModules"] = ProductFeature.ContractOps
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("signup=success", response.Headers.Location?.ToString(), StringComparison.Ordinal);

        using var scope = enabledFactory.Services.CreateScope();
        var tenants = await scope.ServiceProvider.GetRequiredService<ITenantRepository>().GetAllAsync();
        var tenant = Assert.Single(tenants, t => t.ShortCode == shortCode);
        Assert.Equal(TenantStatus.Trial, tenant.Status);

        var memberManager = scope.ServiceProvider.GetRequiredService<MemberManager>();
        var member = await memberManager.FindByEmailAsync(email);
        Assert.NotNull(member);
        Assert.True(int.TryParse(member!.Id, out var memberId));

        var staff = await scope.ServiceProvider.GetRequiredService<ProgrammePulse.Services.Staff.IStaffRepository>().GetByMemberIdAsync(memberId);
        Assert.NotNull(staff);
        Assert.Equal(tenant.TenantKey, staff!.TenantId);

        var features = await scope.ServiceProvider.GetRequiredService<ITenantFeatureSelectionRepository>().GetSelectedFeaturesAsync(tenant.TenantKey);
        Assert.Contains(ProductFeature.ContractOps, features);
    }

    [Fact]
    public async Task With_the_switch_off_start_trial_provisions_nothing()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var shortCode = $"off-{Guid.NewGuid().ToString("N")[..8]}";

        var page = await client.GetStringAsync("/purchase");
        Assert.DoesNotContain("trial-signup-form", page, StringComparison.Ordinal);

        using var response = await client.PostAsync("/purchase/start-trial", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["companyName"] = "Switched off",
            ["shortCode"] = shortCode,
            ["adminFullName"] = "Nobody",
            ["adminEmail"] = $"{shortCode}@example.com",
            ["password"] = "Trial-P@ssword-1",
            ["confirmPassword"] = "Trial-P@ssword-1",
            ["plan"] = TenantPlan.Starter
        }));

        // The page carries no token to send when the trial is off, so the
        // antiforgery filter may answer first (400); either way, nothing is created.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest });
        using var scope = factory.Services.CreateScope();
        var tenants = await scope.ServiceProvider.GetRequiredService<ITenantRepository>().GetAllAsync();
        Assert.DoesNotContain(tenants, t => t.ShortCode == shortCode);
    }
}
