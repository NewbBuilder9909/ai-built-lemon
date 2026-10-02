using System.Net;
using System.Text.Json;
using ProgrammePulse.Tests.Personas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.ExecutiveReview;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Services.Staff;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed partial class ExecutiveReviewIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private bool Database() => ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("Executive review migration, isolation, concurrency and rendered pages were not verified.", output);

    [Fact]
    public async Task Market_versions_are_isolated_and_concurrent_edits_cannot_overwrite_one_another()
    {
        if (!Database()) return;
        var tenant = await CreateTenantAsync();
        var other = await CreateTenantAsync();
        using var scope = factory.Services.CreateScope();
        var markets = scope.ServiceProvider.GetRequiredService<IMarketSettingsRepository>();
        Assert.Equal(0, (await markets.GetAsync(tenant)).Version);
        var first = await markets.SaveAsync(new(tenant, 1), new() { MarketCode = "IE", ReportingCurrency = "EUR" }, 0);
        Assert.Equal(1, first.Version);
        Assert.Equal("GBP", (await markets.GetAsync(other)).Settings.ReportingCurrency);

        async Task<bool> Save(string currency)
        {
            using var concurrentScope = factory.Services.CreateScope();
            try
            {
                await concurrentScope.ServiceProvider.GetRequiredService<IMarketSettingsRepository>()
                    .SaveAsync(new(tenant, 1), first.Settings with { ReportingCurrency = currency }, 1);
                return true;
            }
            catch (ReviewConflictException) { return false; }
        }
        var outcomes = await Task.WhenAll(Task.Run(() => Save("USD")), Task.Run(() => Save("GBP")));
        Assert.Single(outcomes, passed => passed);
        Assert.Equal(2, (await markets.GetAsync(tenant)).Version);
        Assert.Equal("EUR", first.Settings.ReportingCurrency);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => markets.SaveAsync(new(Guid.NewGuid(), 1), new(), 0));
        await Assert.ThrowsAsync<ReviewValidationException>(() => markets.SaveAsync(new(other, 1), new() { ReportingCurrency = "wrong" }, 0));
        Assert.Equal(0, (await markets.GetAsync(other)).Version);
    }

    [Fact]
    public async Task Pack_retains_evidence_and_settings_after_sources_change_and_foreign_reads_are_empty()
    {
        if (!Database()) return;
        var tenant = await CreateTenantAsync();
        var item = await SeedSourceAsync(tenant);
        using var scope = factory.Services.CreateScope();
        var packs = scope.ServiceProvider.GetRequiredService<IExecutivePackRepository>();
        var markets = scope.ServiceProvider.GetRequiredService<IMarketSettingsRepository>();
        await markets.SaveAsync(new(tenant, 1), new() { MarketCode = "IE", FormatCulture = "en-IE", ReportingCurrency = "EUR", TimeZoneId = "Europe/Dublin" }, 0);
        var pack = await packs.CaptureAsync(new(tenant, 1), "ClickUp", 48);
        var originalJson = JsonSerializer.Serialize(pack);
        Assert.Equal(1, pack.Blocked);
        Assert.Null(pack.Market.ChangedByMemberId);
        await scope.ServiceProvider.GetRequiredService<IProgrammeRepository>()
            .UpsertWorkItemAsync(item with { Stage = WorkItemLifecycleStage.Done, UpdatedAtUtc = DateTime.UtcNow }, tenant);
        await markets.SaveAsync(new(tenant, 1), new(), 1);
        Assert.Equal(originalJson, JsonSerializer.Serialize(await packs.GetAsync(tenant, pack.PackKey)));
        Assert.Null(await packs.GetAsync(Guid.NewGuid(), pack.PackKey));
        Assert.Empty(await packs.GetRecentAsync(Guid.NewGuid()));
        var second = await packs.CaptureAsync(new(tenant, 1), "ClickUp", 48);
        Assert.Equal(pack.PackKey, second.PreviousPackKey);
        Assert.Equal(0, second.Blocked);
        Assert.Equal(2, second.Market.Version);
    }

    [Fact]
    public async Task Running_and_failed_syncs_block_capture_without_creating_a_pack()
    {
        if (!Database()) return;
        var tenant = await CreateTenantAsync();
        await SeedSourceAsync(tenant);
        using var scope = factory.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<ISyncRunRepository>();
        var packs = scope.ServiceProvider.GetRequiredService<IExecutivePackRepository>();
        var run = await runs.TryAcquireAsync(tenant, "ClickUp", "review-running", null, DateTime.UtcNow, TimeSpan.FromMinutes(1));
        Assert.NotNull(run);
        await Assert.ThrowsAsync<ReviewValidationException>(() => packs.CaptureAsync(new(tenant, 1), "ClickUp", 48));
        await runs.FailAsync(run.RunKey, "test", "fixture failure", DateTime.UtcNow);
        await runs.ReleaseAsync(tenant, "ClickUp", "review-running", run.RunKey);
        await Assert.ThrowsAsync<ReviewValidationException>(() => packs.CaptureAsync(new(tenant, 1), "ClickUp", 48));
        Assert.Empty(await packs.GetRecentAsync(tenant));
    }

    [Fact]
    public async Task Board_can_read_and_download_but_cannot_capture_or_change_market_even_with_valid_antiforgery()
    {
        if (!Database()) return;
        using var enabled = Enable();
        var tenant = await CreateTenantAsync();
        await SeedSourceAsync(tenant);
        var board = await PersonaAsync(NorthstarPersonas.Board, tenant);
        var session = await PersonaSignIn.SignInAsync(factory, board);
        using var scope = factory.Services.CreateScope();
        var pack = await scope.ServiceProvider.GetRequiredService<IExecutivePackRepository>().CaptureAsync(new(tenant, 1), "ClickUp", 48);
        var index = await session.GetAsync("/staffops/executive");
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.DoesNotContain("Capture operational pack", await index.Content.ReadAsStringAsync());
        var detail = await session.GetAsync($"/staffops/executive/{pack.PackKey}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var html = await detail.Content.ReadAsStringAsync();
        Assert.Contains("not independently reviewed", html);
        Assert.DoesNotContain("sensitive task title", html);
        Assert.DoesNotContain(NorthstarPersonaSeeder.CostSentinel, html);
        Assert.True(detail.Headers.CacheControl?.NoStore);
        var download = await session.GetAsync($"/staffops/executive/{pack.PackKey}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(pack.PackKey, JsonSerializer.Deserialize<ExecutivePack>(await download.Content.ReadAsStringAsync())!.PackKey);
        foreach (var route in new[] { "/staffops/executive/capture", "/staffops/market" })
        {
            var denied = await session.PostWithTokenAsync("/staffops/executive", route, new Dictionary<string, string> { ["source"] = "ClickUp" });
            Assert.NotNull(denied);
            Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(denied));
        }
        var foreignTenant = await CreateTenantAsync();
        await SeedSourceAsync(foreignTenant);
        var foreign = await scope.ServiceProvider.GetRequiredService<IExecutivePackRepository>().CaptureAsync(new(foreignTenant, 1), "ClickUp", 48);
        Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync($"/staffops/executive/{foreign.PackKey}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync($"/staffops/executive/{foreign.PackKey}/download")).StatusCode);
        await scope.ServiceProvider.GetRequiredService<IStaffRepository>().AnonymizeAsync(board.StaffKey, DateTime.UtcNow);
        Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(await session.GetAsync($"/staffops/executive/{pack.PackKey}/download")));
    }

    [Fact]
    public async Task Admin_can_save_settings_capture_and_render_Welsh_without_enabling_Welsh_number_format()
    {
        if (!Database()) return;
        using var enabled = Enable();
        var tenant = await CreateTenantAsync();
        await SeedSourceAsync(tenant);
        var admin = await PersonaAsync(NorthstarPersonas.TenantAdmin, tenant);
        var session = await PersonaSignIn.SignInAsync(factory, admin);
        var fields = new Dictionary<string, string>
        {
            ["ExpectedVersion"] = "0", ["MarketCode"] = "IE", ["UiCulture"] = "cy-GB", ["FormatCulture"] = "en-IE",
            ["ReportingCurrency"] = "USD", ["TimeZoneId"] = "Europe/Dublin", ["FiscalYearStartMonth"] = "4"
        };
        var save = await session.PostWithTokenAsync("/staffops/market", "/staffops/market", fields);
        Assert.NotNull(save);
        Assert.Equal(AccessOutcome.Redirected, PersonaSession.Outcome(save));
        var page = await session.GetAsync("/staffops/market");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Gosodiadau marchnad", html);
        Assert.Contains("formatCulture=en-IE", html);
        var conflict = await session.PostWithTokenAsync("/staffops/market", "/staffops/market", fields);
        Assert.Equal(HttpStatusCode.Conflict, conflict!.StatusCode);
        fields["ExpectedVersion"] = "1";
        fields["ReportingCurrency"] = "BAD";
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PostWithTokenAsync("/staffops/market", "/staffops/market", fields))!.StatusCode);
        var captured = await session.PostWithTokenAsync("/staffops/executive", "/staffops/executive/capture", new Dictionary<string, string> { ["source"] = "ClickUp" });
        Assert.NotNull(captured);
        Assert.Equal(AccessOutcome.Redirected, PersonaSession.Outcome(captured));
        var rendered = await session.GetAsync(captured.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, rendered.StatusCode);
        Assert.Contains("Europe/Dublin", await rendered.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Feature_flag_and_unsupported_language_and_external_redirect_fail_closed()
    {
        if (!Database()) return;
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/staffops/executive")).StatusCode);
        var invalid = await client.GetAsync("/language?culture=made-up&returnUrl=/staffops");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        // An off-site return URL falls back to the neutral site root, never follows it.
        var external = await client.GetAsync("/language?culture=cy-GB&returnUrl=https://example.com");
        Assert.Equal("/", external.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Platform_operator_and_staff_cannot_read_review_data_and_suspension_blocks_existing_sessions()
    {
        if (!Database()) return;
        using var enabled = Enable();
        var tenant = await CreateTenantAsync();
        foreach (var basis in new[] { NorthstarPersonas.PlatformAdmin, NorthstarPersonas.Developer })
        {
            var persona = await PersonaAsync(basis, tenant);
            var session = await PersonaSignIn.SignInAsync(factory, persona);
            foreach (var route in new[] { "/staffops/executive", "/staffops/market" })
                Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(await session.GetAsync(route)));
        }
        var board = await PersonaAsync(NorthstarPersonas.Board, tenant);
        var reader = await PersonaSignIn.SignInAsync(factory, board);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/staffops/executive")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var current = await tenants.GetByKeyAsync(tenant);
        await tenants.UpdateAsync(current! with { Status = TenantStatus.Suspended, IsActive = false });
        Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(await reader.GetAsync("/staffops/executive")));
    }

    private IDisposable Enable()
    {
        var options = factory.Services.GetRequiredService<IOptions<ExecutiveReviewOptions>>().Value;
        var previous = options.Enabled;
        options.Enabled = true;
        return new Cleanup(() => options.Enabled = previous);
    }

    private sealed class Cleanup(Action action) : IDisposable { public void Dispose() => action(); }

    private async Task<Guid> CreateTenantAsync()
    {
        var key = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantRepository>().CreateAsync(new Tenant
        {
            TenantKey = key, Name = "Review test", ShortCode = "rv-" + key.ToString("N")[..20],
            IsActive = true, CreatedAtUtc = DateTime.UtcNow
        });
        // Executive review is a benched module; this tenant exists only for this test.
        await TestModules.SwitchOnForTestTenantAsync(factory.Services, key, Models.Tenancy.ProductModules.ExecutiveReview);
        return key;
    }

    private async Task<PersonaDefinition> PersonaAsync(PersonaDefinition basis, Guid tenant)
    {
        var key = Guid.NewGuid();
        var persona = basis with { StaffKey = key, TenantKey = tenant, Email = $"review-{key:N}@northstar.test" };
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([persona]);
        return persona;
    }

    private async Task<WorkItem> SeedSourceAsync(Guid tenant)
    {
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProgrammeRepository>();
        var now = DateTime.UtcNow.AddMinutes(-1);
        var programme = await repo.UpsertProgrammeAsync(new Programme
        {
            ProgrammeKey = Guid.NewGuid(), Name = "Review", ExternalSource = "ClickUp", ExternalId = "programme",
            CreatedAtUtc = now, UpdatedAtUtc = now
        }, tenant);
        var project = await repo.UpsertProjectAsync(new Project
        {
            ProjectKey = Guid.NewGuid(), ProgrammeKey = programme.ProgrammeKey, Name = "Review", ExternalSource = "ClickUp", ExternalId = "project",
            CreatedAtUtc = now, UpdatedAtUtc = now
        }, tenant);
        var stream = await repo.UpsertWorkstreamAsync(new Workstream
        {
            WorkstreamKey = Guid.NewGuid(), ProjectKey = project.ProjectKey, Name = "Review", ExternalSource = "ClickUp", ExternalId = "stream",
            CreatedAtUtc = now, UpdatedAtUtc = now
        }, tenant);
        var item = await repo.UpsertWorkItemAsync(new WorkItem
        {
            WorkItemKey = Guid.NewGuid(), WorkstreamKey = stream.WorkstreamKey, Title = "sensitive task title",
            Stage = WorkItemLifecycleStage.Blocked, DueDateUtc = now.AddDays(-1), ExternalSource = "ClickUp", ExternalId = "item",
            CreatedAtUtc = now, UpdatedAtUtc = now
        }, tenant);
        await scope.ServiceProvider.GetRequiredService<ISourceConnectionRepository>().GetOrCreateActiveAsync(tenant, "ClickUp", "review-account", now);
        var runs = scope.ServiceProvider.GetRequiredService<ISyncRunRepository>();
        var run = await runs.TryAcquireAsync(tenant, "ClickUp", "review-seed", null, now, TimeSpan.FromMinutes(2));
        Assert.NotNull(run);
        Assert.True(await runs.CompleteAsync(run.RunKey, tenant, "ClickUp", "review-seed", "fixture", "{}", now.AddSeconds(1)));
        await runs.ReleaseAsync(tenant, "ClickUp", "review-seed", run.RunKey);
        return item;
    }
}
