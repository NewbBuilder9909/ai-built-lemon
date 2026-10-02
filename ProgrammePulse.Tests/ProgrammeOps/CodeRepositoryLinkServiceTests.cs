using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// Step 1 of docs/delivery-evidence-and-contract-assurance.md: declared
/// repository → project links. These pin the rules a person can get wrong
/// (malformed repository, duplicate link) and the ones that must hold
/// silently (normalisation, audit, the gap list).
/// </summary>
public sealed class CodeRepositoryLinkServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("7a1f0000-0000-0000-0000-000000000001");
    private static readonly DateTime Now = new(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);

    private readonly FakeCodeRepositoryLinkRepository _links = new();
    private readonly FakeProgrammeRepository _programmes = new();
    private readonly FakeAuditLogRepository _audit = new();
    private readonly Guid _project;

    public CodeRepositoryLinkServiceTests()
    {
        var customer = new Customer { CustomerKey = Guid.NewGuid(), TenantId = Tenant, Name = "Contoso", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var programme = new Programme { ProgrammeKey = Guid.NewGuid(), TenantId = Tenant, Name = "Portal", CustomerKey = customer.CustomerKey, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        _project = Guid.NewGuid();
        _programmes.Customers.Add(customer);
        _programmes.Programmes.Add(programme);
        _programmes.Projects.Add(new Project { ProjectKey = _project, TenantId = Tenant, ProgrammeKey = programme.ProgrammeKey, Name = "Web", CreatedAtUtc = Now, UpdatedAtUtc = Now });
    }

    private CodeRepositoryLinkService Sut() => new(_links, _programmes, _audit, new FixedTime(Now));

    [Theory]
    [InlineData("GitHub", "acme", "acme/api", true)]
    [InlineData("AzureDevOps", "contoso-org", "Web Platform/api", true)]
    [InlineData("GitHub", "acme", "api", false)]
    [InlineData("GitHub", "acme", "acme/api/extra", false)]
    [InlineData("GitHub", "acme", "../api", false)]
    [InlineData("GitHub", "acme", "acme/*", false)]
    [InlineData("GitHub", "", "acme/api", false)]
    [InlineData("", "acme", "acme/api", false)]
    [InlineData("Git Hub", "acme", "acme/api", false)]
    public void Repository_references_accept_both_provider_shapes_and_nothing_that_reshapes_a_url(
        string provider, string account, string key, bool valid)
    {
        var reference = CodeRepositoryRef.TryCreate(provider, account, key, out var error);

        Assert.Equal(valid, reference is not null);
        Assert.Equal(valid, error is null);
    }

    [Fact]
    public async Task A_link_is_normalised_created_and_audited()
    {
        var outcome = await Sut().LinkAsync(Tenant, " GitHub ", "ACME", "/Acme/API/", _project, "  shared UI  ", Guid.NewGuid(), 42);

        Assert.True(outcome.Succeeded);
        var link = Assert.Single(_links.Rows);
        Assert.Equal(new CodeRepositoryRef("GitHub", "acme", "acme/api"), link.Repository);
        Assert.Equal("shared UI", link.Note);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal((CodeRepositoryLinkService.AuditEntityType, "Linked", 42, Tenant), (entry.EntityType, entry.Action, entry.ActorMemberId, entry.TenantId));
        Assert.Contains("\"RepositoryKey\":\"acme/api\"", entry.DetailJson);
    }

    [Fact]
    public async Task A_malformed_repository_is_refused_with_a_reason_and_nothing_is_written()
    {
        var outcome = await Sut().LinkAsync(Tenant, "GitHub", "acme", "api", _project, null, null, null);

        Assert.False(outcome.Succeeded);
        Assert.Contains("two parts", outcome.Error);
        Assert.Empty(_links.Rows);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task The_same_repository_and_project_cannot_be_linked_twice_while_live()
    {
        await Sut().LinkAsync(Tenant, "GitHub", "acme", "acme/api", _project, null, null, null);
        var second = await Sut().LinkAsync(Tenant, "GitHub", "ACME", "acme/API", _project, null, null, null);

        Assert.False(second.Succeeded);
        Assert.Contains("already linked", second.Error);
        Assert.Single(_links.Rows);
    }

    [Fact]
    public async Task Ending_a_link_keeps_it_on_record_audits_it_and_allows_relinking()
    {
        await Sut().LinkAsync(Tenant, "GitHub", "acme", "acme/api", _project, null, null, null);
        var key = _links.Rows[0].LinkKey;

        Assert.True((await Sut().UnlinkAsync(Tenant, key, null, 7)).Succeeded);
        Assert.False((await Sut().UnlinkAsync(Tenant, key, null, 7)).Succeeded);
        Assert.True((await Sut().LinkAsync(Tenant, "GitHub", "acme", "acme/api", _project, null, null, null)).Succeeded);

        Assert.Equal(2, _links.Rows.Count);
        Assert.NotNull(_links.Rows[0].RemovedAtUtc);
        Assert.Equal(["Linked", "Unlinked", "Linked"], _audit.Entries.Select(e => e.Action));
    }

    [Fact]
    public async Task The_page_names_the_customer_and_lists_only_repositories_no_project_claims()
    {
        await Sut().LinkAsync(Tenant, "GitHub", "acme", "acme/api", _project, null, null, null);
        CodeRepositoryRef[] known =
        [
            new("GitHub", "acme", "ACME/API"),
            new("GitHub", "acme", "acme/web"),
            new("AzureDevOps", "contoso", "platform/billing"),
        ];

        var page = await Sut().BuildPageAsync(Tenant, PageRequest.First(), known, evidenceSourcesAvailable: true, message: null);

        var row = Assert.Single(page.Links);
        Assert.Equal(("Web", "Portal", "Contoso"), (row.ProjectName, row.ProgrammeName, row.CustomerName));
        Assert.Equal(["AzureDevOps: contoso/platform/billing", "GitHub: acme/web"], page.Unclaimed.Select(r => r.DisplayName));
        Assert.Equal("Portal › Web (Contoso)", Assert.Single(page.Projects).Label);
    }

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}

/// <summary>In-memory ICodeRepositoryLinkRepository with the same live-uniqueness and tenant rules as the SQL one.</summary>
public sealed class FakeCodeRepositoryLinkRepository : ICodeRepositoryLinkRepository
{
    public List<CodeRepositoryLink> Rows { get; } = [];

    public Task<ResultPage<CodeRepositoryLink>> GetLivePageAsync(Guid tenantId, PageRequest page) =>
        Task.FromResult(ResultPage<CodeRepositoryLink>.Of(Live(tenantId), page));

    public Task<IReadOnlyList<CodeRepositoryLink>> GetLiveAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<CodeRepositoryLink>>(Live(tenantId).ToList());

    public Task<CodeRepositoryLink?> GetLiveByKeyAsync(Guid linkKey, Guid tenantId) =>
        Task.FromResult(Live(tenantId).FirstOrDefault(l => l.LinkKey == linkKey));

    public Task<bool> TryCreateAsync(CodeRepositoryLink link)
    {
        if (Live(link.TenantId).Any(l => l.ProjectKey == link.ProjectKey && l.Repository.SameRepositoryAs(link.Repository)))
        {
            return Task.FromResult(false);
        }

        Rows.Add(link);
        return Task.FromResult(true);
    }

    public Task<bool> EndAsync(Guid linkKey, Guid tenantId, Guid? removedByStaffKey, DateTime nowUtc)
    {
        var index = Rows.FindIndex(l => l.LinkKey == linkKey && l.TenantId == tenantId && l.IsLive);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Rows[index] = Rows[index] with { RemovedAtUtc = nowUtc, RemovedByStaffKey = removedByStaffKey };
        return Task.FromResult(true);
    }

    private IEnumerable<CodeRepositoryLink> Live(Guid tenantId) =>
        Rows.Where(l => l.TenantId == tenantId && l.IsLive)
            .OrderBy(l => l.Repository.Provider).ThenBy(l => l.Repository.SourceAccountId).ThenBy(l => l.Repository.RepositoryKey);
}
