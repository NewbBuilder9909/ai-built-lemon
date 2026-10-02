using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Controllers;
using ProgrammePulse.Services.Integrations.AzureDevOps;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// The Azure DevOps evidence connector as the real application composes it.
/// Unit tests construct every type by hand, so a missing registration or an
/// unresolvable controller dependency would otherwise first appear as a 500
/// when an admin presses the button.
///
/// Not covered here: a signed-in render of the connections page. That needs
/// an Enterprise-plan tenant in the persona harness, which does not exist
/// yet; the view is type-checked by scripts/check-views.ps1 instead.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AzureDevOpsEvidenceWiringIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the Azure DevOps connector was not composed by the real application.";

    private bool Ready() => ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output);

    [Fact]
    public void The_real_container_builds_the_connector_and_its_controller()
    {
        if (!Ready()) return;
        using var scope = factory.Services.CreateScope();

        Assert.IsType<AzureDevOpsEvidenceClient>(scope.ServiceProvider.GetRequiredService<IAzureDevOpsEvidenceClient>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AzureDevOpsEvidenceIngestionService>());
        Assert.NotNull(ActivatorUtilities.CreateInstance<StaffAzureDevOpsEvidenceController>(scope.ServiceProvider));
    }

    [Fact]
    public async Task An_anonymous_request_never_reaches_the_repository_picker()
    {
        if (!Ready()) return;
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync($"/staffops/skills/evidence/azure-devops/{Guid.NewGuid()}/repositories");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.True((int)response.StatusCode < 500, $"Expected a refusal, got {(int)response.StatusCode}.");
    }
}
