using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.ProgrammeOps;

public class PortfolioSearchServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly FakeProgrammeRepository _programmes = new();
    private readonly FakeStaffRepository _staff = new();

    private PortfolioSearchService Sut() => new(_programmes, _staff);

    private Guid Seed(Guid tenant, string programme, string item, string? externalId = null, Guid? assignee = null)
    {
        var programmeKey = Guid.NewGuid();
        var projectKey = Guid.NewGuid();
        var workstreamKey = Guid.NewGuid();
        _programmes.Programmes.Add(new Programme { ProgrammeKey = programmeKey, TenantId = tenant, Name = programme, CreatedAtUtc = Now, UpdatedAtUtc = Now });
        _programmes.Projects.Add(new Project { ProjectKey = projectKey, ProgrammeKey = programmeKey, TenantId = tenant, Name = "Build", CreatedAtUtc = Now, UpdatedAtUtc = Now });
        _programmes.Workstreams.Add(new Workstream { WorkstreamKey = workstreamKey, ProjectKey = projectKey, TenantId = tenant, Name = "API", CreatedAtUtc = Now, UpdatedAtUtc = Now });
        _programmes.WorkItems.Add(new WorkItem
        {
            WorkItemKey = Guid.NewGuid(), WorkstreamKey = workstreamKey, TenantId = tenant, Title = item, Stage = WorkItemLifecycleStage.InProgress,
            ExternalSource = "Jira", ExternalId = externalId, AssignedStaffKey = assignee, CreatedAtUtc = Now, UpdatedAtUtc = Now
        });
        return programmeKey;
    }

    [Fact]
    public async Task Finds_programmes_and_work_items_by_name_or_source_id_with_the_path_that_places_them()
    {
        var aisha = Guid.NewGuid();
        _staff.Staff.Add(new StaffProfile { StaffKey = aisha, MemberId = 1, FullName = "Aisha Rahman", Email = "a@x.test", TenantId = Tenant, CreatedAtUtc = Now, UpdatedAtUtc = Now });
        Seed(Tenant, "Patient Portal", "Migrate HL7 feeds", "PP-42", aisha);

        var byTitle = await Sut().SearchAsync(Tenant, "hl7", includePeople: false);
        var byId = await Sut().SearchAsync(Tenant, "pp-42", includePeople: false);
        var byProgramme = await Sut().SearchAsync(Tenant, "portal", includePeople: false);

        var hit = Assert.Single(byTitle.WorkItems);
        Assert.Equal("Patient Portal › Build › API", hit.Path);
        Assert.Equal("Aisha Rahman", hit.AssigneeName);
        Assert.Single(byId.WorkItems);
        Assert.Equal("Patient Portal", Assert.Single(byProgramme.Programmes).Name);
    }

    [Fact]
    public async Task Never_returns_another_tenants_data()
    {
        Seed(OtherTenant, "Secret Programme", "Secret item");
        _staff.Staff.Add(new StaffProfile { StaffKey = Guid.NewGuid(), MemberId = 9, FullName = "Secret Person", Email = "s@x.test", TenantId = OtherTenant, CreatedAtUtc = Now, UpdatedAtUtc = Now });

        var results = await Sut().SearchAsync(Tenant, "secret", includePeople: true);

        Assert.True(results.IsEmpty);
    }

    [Fact]
    public async Task People_are_searched_only_for_a_caller_who_manages_staff()
    {
        _staff.Staff.Add(new StaffProfile { StaffKey = Guid.NewGuid(), MemberId = 1, FullName = "Ben Carter", Email = "ben@x.test", TenantId = Tenant, CreatedAtUtc = Now, UpdatedAtUtc = Now });

        Assert.Empty((await Sut().SearchAsync(Tenant, "carter", includePeople: false)).People);
        Assert.Single((await Sut().SearchAsync(Tenant, "carter", includePeople: true)).People);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" a ")]
    public async Task A_term_shorter_than_two_characters_searches_nothing(string? term)
    {
        Seed(Tenant, "Alpha", "a");

        var results = await Sut().SearchAsync(Tenant, term, includePeople: true);

        Assert.True(results.TooShort);
        Assert.True(results.IsEmpty);
    }

    [Fact]
    public async Task Work_items_stop_at_the_limit_and_say_there_are_more()
    {
        for (var i = 0; i < PortfolioSearchService.WorkItemLimit + 3; i++)
        {
            Seed(Tenant, $"Programme {i}", $"Release task {i:00}");
        }

        var results = await Sut().SearchAsync(Tenant, "release", includePeople: false);

        Assert.Equal(PortfolioSearchService.WorkItemLimit, results.WorkItems.Count);
        Assert.True(results.MoreWorkItems);
    }

    [Theory]
    [InlineData("50%", @"50\%")]
    [InlineData("a_b", @"a\_b")]
    [InlineData("[x]", @"\[x]")]
    [InlineData(@"c:\d", @"c:\\d")]
    public void Like_escaping_makes_wildcards_match_themselves(string term, string expected)
    {
        Assert.Equal(expected, SqlLike.Escape(term));
    }
}
