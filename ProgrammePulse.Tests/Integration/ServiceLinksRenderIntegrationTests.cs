using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.ServiceOps;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Tests.Personas;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// The case-and-code links page over real HTTP, with a desk connected and a
/// link waiting for assessment, which neither the demo nor any other suite
/// renders. Pins three things a reviewer relies on: the desk is chosen by
/// name, never by pasting its key; one form offers both verdicts, each
/// posting to its own action; and another organisation's desk and link
/// never appear.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ServiceLinksRenderIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the case-and-code links page was never rendered with a desk and a link.";

    [Fact]
    public async Task A_reviewer_picks_the_desk_by_name_and_judges_a_link_in_one_form()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var tenant = await CreateTenantAsync();
        var other = await CreateTenantAsync();
        var (desk, link) = await SeedDeskWithLinkAsync(tenant);
        var (theirDesk, _) = await SeedDeskWithLinkAsync(other);

        var session = await PersonaSignIn.SignInAsync(factory, await PersonaAsync(NorthstarPersonas.TenantAdmin, tenant));
        var response = await session.GetAsync("/staffops/service/links");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains($"<option value=\"{desk.ConnectionKey}\">{desk.DisplayName}</option>", html);
        Assert.DoesNotContain("placeholder=\"connection key\"", html);
        Assert.Contains($"action=\"/staffops/service/links/{link.LinkKey}/confirm\"", html);
        Assert.Contains($"formaction=\"/staffops/service/links/{link.LinkKey}/rule-out\"", html);

        Assert.DoesNotContain(theirDesk.DisplayName, html);
        Assert.DoesNotContain(theirDesk.ConnectionKey.ToString(), html);
    }

    private async Task<(DeskConnection Desk, SupportCodeLink Link)> SeedDeskWithLinkAsync(Guid tenant)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IServiceOpsRepository>();
        var account = "acct" + Guid.NewGuid().ToString("N")[..12];
        var now = DateTime.UtcNow;
        var desk = await repository.UpsertConnectionAsync(new DeskConnection
        {
            ConnectionKey = Guid.NewGuid(), TenantId = tenant, Provider = DeskHostPolicy.FreshdeskProvider,
            SourceAccountId = account, DisplayName = $"{account}.freshdesk.com",
            ApiBaseUrl = DeskHostPolicy.CanonicalizeFreshdesk(account)!, ApprovedComponents = ["billing"],
            Status = DeskConnectionStatus.Active, ProtectedCredentialJson = "ciphertext",
            CreatedAtUtc = now, UpdatedAtUtc = now
        });
        var ticketId = Guid.NewGuid().ToString("N")[..10];
        await repository.UpsertCaseAsync(new SupportCaseFact
        {
            CaseKey = Guid.NewGuid(), TenantId = tenant, ConnectionKey = desk.ConnectionKey,
            Provider = DeskHostPolicy.FreshdeskProvider, SourceAccountId = account, ExternalTicketId = ticketId,
            CreatedAtUtc = now, UpdatedAtUtc = now, State = SupportCaseState.Active, ProviderStatus = "2",
            Priority = SupportCasePriority.High, ComponentKey = "billing", RawComponentTag = "billing",
            SourceUrl = "https://acme.freshdesk.com/a/tickets/1", IsReopened = false, IsWithdrawn = false,
            SchemaVersion = SupportCaseSchema.CurrentVersion,
            FirstIngestedAtUtc = now, IngestedAtUtc = now
        });
        var link = await repository.UpsertLinkAsync(new SupportCodeLink
        {
            LinkKey = Guid.NewGuid(), TenantId = tenant, ConnectionKey = desk.ConnectionKey, ExternalTicketId = ticketId,
            ArtifactType = LinkedArtifactType.PullRequest, ArtifactExternalId = "pr-42",
            Method = SupportLinkMethod.IssueKeyMatch, CreatedAtUtc = now, UpdatedAtUtc = now
        });
        return (desk, link);
    }

    private async Task<PersonaDefinition> PersonaAsync(PersonaDefinition basis, Guid tenant)
    {
        var key = Guid.NewGuid();
        var persona = basis with { StaffKey = key, TenantKey = tenant, Email = $"links-{key:N}@northstar.test" };
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([persona]);
        return persona;
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var key = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantRepository>().CreateAsync(new Tenant
        {
            TenantKey = key, Name = "Service links test", ShortCode = "sl-" + key.ToString("N")[..20],
            IsActive = true, CreatedAtUtc = DateTime.UtcNow
        });
        // Service health is a benched module; this tenant exists only for this test.
        await TestModules.SwitchOnForTestTenantAsync(factory.Services, key, Models.Tenancy.ProductModules.ServiceHealth);
        return key;
    }
}
