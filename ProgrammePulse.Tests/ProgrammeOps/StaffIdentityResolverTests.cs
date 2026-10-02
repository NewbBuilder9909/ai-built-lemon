using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.ProgrammeOps;

public class StaffIdentityResolverTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static StaffProfile Person(string email, bool isActive = true) => new()
    {
        StaffKey = Guid.NewGuid(), MemberId = 1, FullName = "Person", Email = email, IsActive = isActive,
        TenantId = TestTenantId, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime
    };

    private static StaffIdentityResolver Build(FakeIdentityResolutionRepository identity, IStaffRepository staff) =>
        new(identity, staff, new FixedTimeProvider(Now));

    [Fact]
    public async Task An_email_match_is_queued_as_a_suggestion_and_attributes_nothing()
    {
        // Anyone who can set an email in the source tool could otherwise put
        // their work under a colleague's name (Aikido: business logic bypass).
        var staff = new FakeStaffRepository();
        var jamie = Person("jamie@example.com");
        staff.Staff.Add(jamie);
        var identity = new FakeIdentityResolutionRepository();
        var sut = Build(identity, staff);

        var resolved = await sut.ResolveAsync("ClickUp", "42", "JAMIE@Example.com", "jamie", "task assignee", TestTenantId);

        Assert.Null(resolved);
        var row = Assert.Single(identity.Unresolved);
        Assert.Equal(jamie.StaffKey, row.SuggestedStaffKey);
        Assert.Equal("jamie@example.com", row.Email);
        Assert.Equal(1, sut.UnresolvedCount);
        Assert.Equal(1, sut.SuggestedCount);
        Assert.Equal(0, sut.AmbiguousCount);
    }

    [Fact]
    public async Task Once_approved_as_a_link_the_same_person_resolves()
    {
        var staff = new FakeStaffRepository();
        var jamie = Person("jamie@example.com");
        staff.Staff.Add(jamie);
        var identity = new FakeIdentityResolutionRepository();
        identity.Links.Add(new ExternalIdentityLink { LinkKey = Guid.NewGuid(), TenantId = TestTenantId, ExternalSource = "ClickUp", ExternalUserId = "42", Email = "jamie@example.com", StaffKey = jamie.StaffKey, CreatedAtUtc = Now.UtcDateTime });

        var sut = Build(identity, staff);

        Assert.Equal(jamie.StaffKey, await sut.ResolveAsync("ClickUp", "42", "jamie@example.com", "jamie", "task assignee", TestTenantId));
        Assert.Empty(identity.Unresolved);
        Assert.Equal(0, sut.SuggestedCount);
    }

    [Fact]
    public async Task An_explicit_link_by_user_id_beats_the_email_heuristic()
    {
        var staff = new FakeStaffRepository();
        var byEmail = Person("shared@example.com");
        var linked = Person("other@example.com");
        staff.Staff.Add(byEmail);
        staff.Staff.Add(linked);
        var identity = new FakeIdentityResolutionRepository();
        identity.Links.Add(new ExternalIdentityLink { LinkKey = Guid.NewGuid(), TenantId = TestTenantId, ExternalSource = "ClickUp", ExternalUserId = "42", StaffKey = linked.StaffKey, CreatedAtUtc = Now.UtcDateTime });

        var resolved = await Build(identity, staff).ResolveAsync("ClickUp", "42", "shared@example.com", null, "task assignee", TestTenantId);

        Assert.Equal(linked.StaffKey, resolved);
    }

    [Fact]
    public async Task An_explicit_link_by_email_applies_when_the_user_id_is_unknown()
    {
        var staff = new FakeStaffRepository();
        var linked = Person("staff-record@example.com");
        staff.Staff.Add(linked);
        var identity = new FakeIdentityResolutionRepository();
        identity.Links.Add(new ExternalIdentityLink { LinkKey = Guid.NewGuid(), TenantId = TestTenantId, ExternalSource = "HubPlanner", Email = "personal@gmail.example", StaffKey = linked.StaffKey, CreatedAtUtc = Now.UtcDateTime });

        var resolved = await Build(identity, staff).ResolveAsync("HubPlanner", "res-9", "Personal@Gmail.example", null, "booking resource", TestTenantId);

        Assert.Equal(linked.StaffKey, resolved);
    }

    [Fact]
    public async Task Links_are_scoped_to_their_source()
    {
        var staff = new FakeStaffRepository();
        var linked = Person("x@example.com");
        staff.Staff.Add(linked);
        var identity = new FakeIdentityResolutionRepository();
        identity.Links.Add(new ExternalIdentityLink { LinkKey = Guid.NewGuid(), TenantId = TestTenantId, ExternalSource = "ClickUp", ExternalUserId = "42", StaffKey = linked.StaffKey, CreatedAtUtc = Now.UtcDateTime });

        // Same id "42" in Hub Planner is a different namespace entirely.
        var resolved = await Build(identity, staff).ResolveAsync("HubPlanner", "42", null, null, "booking resource", TestTenantId);

        Assert.Null(resolved);
        Assert.Single(identity.Unresolved);
    }

    [Fact]
    public async Task An_unmatched_person_is_recorded_once_with_a_sighting_count_and_counted_once()
    {
        var identity = new FakeIdentityResolutionRepository();
        var sut = Build(identity, new FakeStaffRepository());

        await sut.ResolveAsync("ClickUp", "7", "nobody@example.com", "nobody", "task assignee", TestTenantId);
        await sut.ResolveAsync("ClickUp", "7", "nobody@example.com", "nobody", "task assignee", TestTenantId);
        await sut.ResolveAsync("ClickUp", "7", "nobody@example.com", "nobody", "time entry user", TestTenantId);

        var row = Assert.Single(identity.Unresolved);
        Assert.Equal(3, row.OccurrenceCount);
        Assert.Equal("time entry user", row.Context);
        Assert.Equal("nobody@example.com", row.Email);
        Assert.Equal(1, sut.UnresolvedCount);
    }

    [Fact]
    public async Task Nothing_is_recorded_when_there_is_neither_an_id_nor_an_email()
    {
        var identity = new FakeIdentityResolutionRepository();
        var sut = Build(identity, new FakeStaffRepository());

        var resolved = await sut.ResolveAsync("ClickUp", null, " ", "ghost", "task assignee", TestTenantId);

        Assert.Null(resolved);
        Assert.Empty(identity.Unresolved);
        Assert.Equal(0, sut.UnresolvedCount);
        // ...but it is not silently absorbed either: the sync result reports it.
        Assert.Equal(1, sut.UnidentifiableSightings);
    }

    // ---- Ambiguity is never resolved by taking the first match. ----

    [Fact]
    public async Task Two_active_staff_sharing_an_email_is_ambiguous_and_is_queued_with_the_reason()
    {
        var staff = new FakeStaffRepository();
        staff.Staff.Add(Person("shared@example.com"));
        staff.Staff.Add(Person("shared@example.com"));
        var identity = new FakeIdentityResolutionRepository();
        var sut = Build(identity, staff);

        var resolved = await sut.ResolveAsync("ClickUp", "42", "shared@example.com", "Sam", "task assignee", TestTenantId);

        Assert.Null(resolved);
        var row = Assert.Single(identity.Unresolved);
        Assert.Equal("42", row.ExternalUserId);
        Assert.Contains("ambiguous: 2 staff profiles share this email", row.Context);
        Assert.Equal(1, sut.UnresolvedCount);
        Assert.Equal(1, sut.AmbiguousCount);
    }

    [Fact]
    public async Task A_single_active_profile_among_duplicate_emails_is_the_suggestion_over_the_inactive_ones()
    {
        var staff = new FakeStaffRepository();
        var leaver = Person("rehired@example.com", isActive: false);
        var rehired = Person("rehired@example.com");
        staff.Staff.Add(leaver);
        staff.Staff.Add(rehired);
        var identity = new FakeIdentityResolutionRepository();
        var sut = Build(identity, staff);

        var resolved = await sut.ResolveAsync("HubPlanner", "r-1", "rehired@example.com", null, "booking resource", TestTenantId);

        Assert.Null(resolved);
        Assert.Equal(rehired.StaffKey, Assert.Single(identity.Unresolved).SuggestedStaffKey);
        Assert.Equal(0, sut.AmbiguousCount);
    }

    [Fact]
    public async Task Only_inactive_duplicates_are_still_ambiguous()
    {
        var staff = new FakeStaffRepository();
        staff.Staff.Add(Person("gone@example.com", isActive: false));
        staff.Staff.Add(Person("gone@example.com", isActive: false));
        var sut = Build(new FakeIdentityResolutionRepository(), staff);

        Assert.Null(await sut.ResolveAsync("ClickUp", "7", "gone@example.com", null, "task assignee", TestTenantId));
        Assert.Equal(1, sut.AmbiguousCount);
    }

    [Fact]
    public async Task Conflicting_explicit_links_on_the_same_source_user_id_are_ambiguous_rather_than_first_wins()
    {
        var staff = new FakeStaffRepository();
        var a = Person("a@example.com");
        var b = Person("b@example.com");
        staff.Staff.Add(a);
        staff.Staff.Add(b);
        var identity = new FakeIdentityResolutionRepository();
        // Two rows pointing at different people — only possible in data that
        // predates AddIdentityLinkUniqueness, which is why the resolver
        // still has to refuse rather than trust list order.
        identity.Links.Add(new ExternalIdentityLink { LinkKey = Guid.NewGuid(), TenantId = TestTenantId, ExternalSource = "ClickUp", ExternalUserId = "42", StaffKey = a.StaffKey, CreatedAtUtc = Now.UtcDateTime });
        identity.Links.Add(new ExternalIdentityLink { LinkKey = Guid.NewGuid(), TenantId = TestTenantId, ExternalSource = "ClickUp", ExternalUserId = "42", StaffKey = b.StaffKey, CreatedAtUtc = Now.UtcDateTime });
        var sut = Build(identity, staff);

        var resolved = await sut.ResolveAsync("ClickUp", "42", "a@example.com", null, "task assignee", TestTenantId);

        Assert.Null(resolved);
        Assert.Contains("ambiguous: 2 explicit links on this source user id", Assert.Single(identity.Unresolved).Context);
        Assert.Equal(1, sut.AmbiguousCount);
    }

    [Fact]
    public async Task Two_links_for_the_same_person_are_not_ambiguous()
    {
        var staff = new FakeStaffRepository();
        var a = Person("a@example.com");
        staff.Staff.Add(a);
        var identity = new FakeIdentityResolutionRepository();
        identity.Links.Add(new ExternalIdentityLink { LinkKey = Guid.NewGuid(), TenantId = TestTenantId, ExternalSource = "ClickUp", ExternalUserId = "42", StaffKey = a.StaffKey, CreatedAtUtc = Now.UtcDateTime });
        identity.Links.Add(new ExternalIdentityLink { LinkKey = Guid.NewGuid(), TenantId = TestTenantId, ExternalSource = "ClickUp", Email = "old-alias@example.com", StaffKey = a.StaffKey, CreatedAtUtc = Now.UtcDateTime });

        Assert.Equal(a.StaffKey, await Build(identity, staff).ResolveAsync("ClickUp", "42", "old-alias@example.com", null, "task assignee", TestTenantId));
    }

    [Fact]
    public async Task A_second_link_for_the_same_external_identity_is_refused()
    {
        var identity = new FakeIdentityResolutionRepository();
        var first = new ExternalIdentityLink { LinkKey = Guid.NewGuid(), ExternalSource = "ClickUp", ExternalUserId = "42", Email = "x@example.com", StaffKey = Guid.NewGuid(), CreatedAtUtc = Now.UtcDateTime };
        await identity.CreateLinkAsync(first, TestTenantId);

        var byUserId = await Assert.ThrowsAsync<DuplicateIdentityLinkException>(() => identity.CreateLinkAsync(first with { LinkKey = Guid.NewGuid(), Email = null, StaffKey = Guid.NewGuid() }, TestTenantId));
        var byEmail = await Assert.ThrowsAsync<DuplicateIdentityLinkException>(() => identity.CreateLinkAsync(first with { LinkKey = Guid.NewGuid(), ExternalUserId = null, Email = "X@Example.com", StaffKey = Guid.NewGuid() }, TestTenantId));

        Assert.Equal(first.StaffKey, byUserId.ExistingStaffKey);
        Assert.Equal(first.StaffKey, byEmail.ExistingStaffKey);
        Assert.Contains("already linked", byUserId.Message);
        Assert.Single(identity.Links);
    }

    [Fact]
    public async Task Staff_and_links_are_loaded_once_per_resolver_instance()
    {
        var staff = new CountingStaffRepository();
        staff.Staff.Add(Person("a@example.com"));
        var sut = Build(new FakeIdentityResolutionRepository(), staff);

        for (var i = 0; i < 5; i++)
        {
            await sut.ResolveAsync("ClickUp", i.ToString(), "a@example.com", null, "task assignee", TestTenantId);
        }

        Assert.Equal(1, staff.GetAllCalls);
    }

    /// <summary>
    /// Wraps rather than subclasses FakeStaffRepository: StaffIdentityResolver's
    /// caching now sits on top of GetByTenantAsync (not GetAllAsync, which is
    /// not virtual on the fake), so this counts calls to the method the
    /// resolver actually calls.
    /// </summary>
    private sealed class CountingStaffRepository : IStaffRepository
    {
        private readonly FakeStaffRepository _inner = new();

        public List<StaffProfile> Staff => _inner.Staff;

        public int GetAllCalls { get; private set; }

        public Task<StaffProfile?> GetByStaffKeyAsync(Guid staffKey) => _inner.GetByStaffKeyAsync(staffKey);

        public Task<StaffProfile?> GetByMemberIdAsync(int memberId) => _inner.GetByMemberIdAsync(memberId);

        public Task<IReadOnlyList<StaffProfile>> GetAllAsync() => _inner.GetAllAsync();

        public Task<IReadOnlyList<StaffProfile>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            GetAllCalls++;
            return _inner.GetByTenantAsync(tenantId, cancellationToken);
        }

        public Task<StaffProfile> CreateAsync(StaffProfile staff) => _inner.CreateAsync(staff);

        public Task UpdateDefaultWorkHoursAsync(Guid staffKey, decimal defaultWorkHoursPerWeek, DateTime nowUtc) =>
            _inner.UpdateDefaultWorkHoursAsync(staffKey, defaultWorkHoursPerWeek, nowUtc);

        public Task SetActiveAsync(Guid staffKey, Guid tenantId, bool isActive, DateTime nowUtc) =>
            _inner.SetActiveAsync(staffKey, tenantId, isActive, nowUtc);

        public Task AnonymizeAsync(Guid staffKey, DateTime nowUtc) => _inner.AnonymizeAsync(staffKey, nowUtc);
    }
}
