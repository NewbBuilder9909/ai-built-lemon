using Microsoft.Extensions.DependencyInjection;
using NPoco;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;
using Umbraco.Cms.Infrastructure.Scoping;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Declared repository → project links against real SQL Server: tenant
/// isolation both ways, one live link per repository and project (the
/// filtered unique index), history kept when a link ends, and a project from
/// another tenant refused before anything is written.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class CodeRepositoryLinkIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "repository links were not exercised against real SQL Server.";
    private static readonly DateTime Now = new(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);

    private T Resolve<T>() where T : notnull => factory.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    [Fact]
    public async Task Links_are_tenant_scoped_unique_while_live_and_kept_when_ended()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var links = Resolve<ICodeRepositoryLinkRepository>();
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var project = await SeedProjectAsync(tenant);
        var repository = new CodeRepositoryRef("GitHub", "acme", $"acme/{Guid.NewGuid():N}");

        var first = Link(tenant, repository, project);
        Assert.True(await links.TryCreateAsync(first));
        Assert.False(await links.TryCreateAsync(Link(tenant, repository, project)));

        Assert.Equal(first.LinkKey, Assert.Single(await links.GetLiveAsync(tenant)).LinkKey);
        Assert.Empty(await links.GetLiveAsync(otherTenant));
        Assert.Null(await links.GetLiveByKeyAsync(first.LinkKey, otherTenant));
        Assert.False(await links.EndAsync(first.LinkKey, otherTenant, null, Now));

        Assert.True(await links.EndAsync(first.LinkKey, tenant, Guid.NewGuid(), Now));
        Assert.Empty(await links.GetLiveAsync(tenant));
        Assert.True(await links.TryCreateAsync(Link(tenant, repository, project)));

        using var scope = factory.Services.GetRequiredService<IScopeProvider>().CreateScope(autoComplete: true);
        var rows = await scope.Database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ProgrammeOps_CodeRepositoryLink WHERE tenantId = @0", tenant);
        Assert.Equal(2, rows);
    }

    [Fact]
    public async Task A_project_from_another_tenant_is_refused_and_nothing_is_written()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var links = Resolve<ICodeRepositoryLinkRepository>();
        var tenant = Guid.NewGuid();
        var foreignProject = await SeedProjectAsync(Guid.NewGuid());

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() =>
            links.TryCreateAsync(Link(tenant, new CodeRepositoryRef("GitHub", "acme", "acme/api"), foreignProject)));
        Assert.Empty(await links.GetLiveAsync(tenant));
    }

    [Fact]
    public async Task The_live_link_index_exists()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var scope = factory.Services.GetRequiredService<IScopeProvider>().CreateScope(autoComplete: true);
        var filter = await scope.Database.ExecuteScalarAsync<string>(
            "SELECT filter_definition FROM sys.indexes WHERE name = @0 AND is_unique = 1",
            ProgrammePulse.Migrations.ProgrammeOps.AddCodeRepositoryLinkTable.LiveIndexName);
        Assert.Equal("([removedAtUtc] IS NULL)", filter);
    }

    private static CodeRepositoryLink Link(Guid tenant, CodeRepositoryRef repository, Guid project) => new()
    {
        LinkKey = Guid.NewGuid(), TenantId = tenant, Repository = repository, ProjectKey = project, LinkedAtUtc = Now
    };

    private async Task<Guid> SeedProjectAsync(Guid tenant)
    {
        var repository = Resolve<IProgrammeRepository>();
        var programme = await repository.UpsertProgrammeAsync(new Programme
        {
            ProgrammeKey = Guid.NewGuid(), Name = "Links programme", CreatedAtUtc = Now, UpdatedAtUtc = Now
        }, tenant);
        var project = await repository.UpsertProjectAsync(new Project
        {
            ProjectKey = Guid.NewGuid(), ProgrammeKey = programme.ProgrammeKey, Name = "Links project", CreatedAtUtc = Now, UpdatedAtUtc = Now
        }, tenant);
        return project.ProjectKey;
    }
}
