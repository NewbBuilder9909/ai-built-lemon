using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Tests.ProgrammeOps;

namespace ProgrammePulse.Tests.Staff;

/// <summary>
/// The account rules an Admin works under in Settings → People: suspend and
/// restore, roles, unlock, password reset and delete. The two that protect an
/// organisation from locking itself out: nobody can act on their own account,
/// and the last active Admin can never be suspended, deleted or demoted.
/// </summary>
public class StaffAccessAdministrationTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private const int AdminId = 1;
    private const int OtherAdminId = 2;
    private const int StaffId = 3;

    private readonly FakeStaffRepository _staff = new();
    private readonly FakeMemberAccountAdministration _accounts = new();
    private readonly FakeStaffAuditLogRepository _audit = new();
    private readonly RecordingGdpr _gdpr = new();

    private StaffAdminService Sut() =>
        new(_staff, null!, null!, _gdpr, null!, _audit, null!, _accounts, TimeProvider.System);

    private Guid Add(int memberId, string role, bool active = true, Guid? tenant = null)
    {
        var key = Guid.NewGuid();
        _staff.Staff.Add(new StaffProfile
        {
            StaffKey = key,
            MemberId = memberId,
            FullName = $"Person {memberId}",
            Email = $"p{memberId}@example.test",
            TenantId = tenant ?? Tenant,
            IsActive = active,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _accounts.With(memberId, role);
        return key;
    }

    [Fact]
    public async Task Suspending_marks_the_profile_inactive_and_the_login_unapproved_and_is_audited()
    {
        Add(AdminId, StaffRole.Admin);
        var person = Add(StaffId, StaffRole.Staff);

        var result = await Sut().SetAccessAsync(Tenant, person, active: false, actorMemberId: AdminId);

        Assert.Equal(StaffAdminStatus.Done, result.Status);
        Assert.False(_staff.Staff.Single(s => s.StaffKey == person).IsActive);
        Assert.False(_accounts.Accounts[StaffId].Approved);
        Assert.Contains(_audit.Entries, e => e.Action == "AccessSuspended" && e.EntityId == person.ToString());
    }

    [Fact]
    public async Task Restoring_reverses_a_suspension()
    {
        Add(AdminId, StaffRole.Admin);
        var person = Add(StaffId, StaffRole.Staff, active: false);
        await _accounts.SetApprovedAsync(StaffId, false);

        await Sut().SetAccessAsync(Tenant, person, active: true, actorMemberId: AdminId);

        Assert.True(_staff.Staff.Single(s => s.StaffKey == person).IsActive);
        Assert.True(_accounts.Accounts[StaffId].Approved);
    }

    [Fact]
    public async Task An_admin_cannot_suspend_or_delete_themselves()
    {
        var self = Add(AdminId, StaffRole.Admin);
        Add(OtherAdminId, StaffRole.Admin);

        Assert.Equal(StaffAdminService.NotYourself, (await Sut().SetAccessAsync(Tenant, self, false, AdminId)).Message);
        Assert.Equal(StaffAdminService.NotYourself, (await Sut().DeleteAsync(Tenant, self, AdminId)).Message);
        Assert.True(_staff.Staff.Single(s => s.StaffKey == self).IsActive);
    }

    [Fact]
    public async Task An_admin_cannot_remove_their_own_admin_role()
    {
        var self = Add(AdminId, StaffRole.Admin);
        Add(OtherAdminId, StaffRole.Admin);

        var result = await Sut().SetRolesAsync(Tenant, self, [StaffRole.TeamLead], AdminId);

        Assert.Equal(StaffAdminService.NotYourself, result.Message);
        Assert.Contains(StaffRole.Admin, _accounts.Accounts[AdminId].Roles);
    }

    [Fact]
    public async Task The_last_active_admin_cannot_be_suspended_deleted_or_demoted()
    {
        var lastAdmin = Add(OtherAdminId, StaffRole.Admin);
        Add(AdminId, StaffRole.Admin, active: false);   // suspended, so it doesn't count
        Add(StaffId, StaffRole.Staff);

        Assert.Equal(StaffAdminService.LastAdmin, (await Sut().SetAccessAsync(Tenant, lastAdmin, false, StaffId)).Message);
        Assert.Equal(StaffAdminService.LastAdmin, (await Sut().DeleteAsync(Tenant, lastAdmin, StaffId)).Message);
        Assert.Equal(StaffAdminService.LastAdmin, (await Sut().SetRolesAsync(Tenant, lastAdmin, [StaffRole.Staff], StaffId)).Message);
    }

    [Fact]
    public async Task An_admin_in_another_tenant_does_not_count_towards_keeping_this_one_administered()
    {
        var lastAdmin = Add(OtherAdminId, StaffRole.Admin);
        Add(99, StaffRole.Admin, tenant: Guid.NewGuid());

        var result = await Sut().SetAccessAsync(Tenant, lastAdmin, false, actorMemberId: AdminId);

        Assert.Equal(StaffAdminService.LastAdmin, result.Message);
    }

    [Fact]
    public async Task Roles_are_replaced_and_need_at_least_one_tenant_role()
    {
        Add(AdminId, StaffRole.Admin);
        var person = Add(StaffId, StaffRole.Staff);

        Assert.Equal(StaffAdminService.NoRoles, (await Sut().SetRolesAsync(Tenant, person, [StaffRole.PlatformAdmin], AdminId)).Message);

        var result = await Sut().SetRolesAsync(Tenant, person, [StaffRole.TeamLead, StaffRole.HolidayApprover], AdminId);

        Assert.Equal(StaffAdminStatus.Done, result.Status);
        Assert.Equal([StaffRole.HolidayApprover, StaffRole.TeamLead], _accounts.Accounts[StaffId].Roles.Order(StringComparer.Ordinal));
        Assert.Contains(_audit.Entries, e => e.Action == "RolesChanged");
    }

    [Fact]
    public async Task Deleting_erases_personal_data_then_removes_the_login()
    {
        Add(AdminId, StaffRole.Admin);
        var person = Add(StaffId, StaffRole.Staff);

        var result = await Sut().DeleteAsync(Tenant, person, AdminId);

        Assert.Equal(StaffAdminStatus.Done, result.Status);
        Assert.Equal([person], _gdpr.Erased);
        Assert.Equal([StaffId], _accounts.Deleted);
        Assert.Contains(_audit.Entries, e => e.Action == "Deleted");
    }

    [Fact]
    public async Task A_reset_link_is_issued_only_for_an_active_person_in_this_tenant()
    {
        Add(AdminId, StaffRole.Admin);
        var active = Add(StaffId, StaffRole.Staff);
        var suspended = Add(4, StaffRole.Staff, active: false);
        var elsewhere = Add(5, StaffRole.Staff, tenant: Guid.NewGuid());

        var issue = await Sut().IssuePasswordResetAsync(Tenant, active, AdminId);

        Assert.NotNull(issue);
        Assert.Equal(StaffId, issue.MemberId);
        Assert.Null(await Sut().IssuePasswordResetAsync(Tenant, suspended, AdminId));
        Assert.Null(await Sut().IssuePasswordResetAsync(Tenant, elsewhere, AdminId));
        Assert.Single(_audit.Entries, e => e.Action == "PasswordResetIssued");
    }

    [Fact]
    public async Task Unlock_clears_a_lockout()
    {
        Add(AdminId, StaffRole.Admin);
        var person = Add(StaffId, StaffRole.Staff);
        _accounts.Accounts[StaffId] = (true, DateTimeOffset.UtcNow.AddHours(1), [StaffRole.Staff]);

        await Sut().UnlockAsync(Tenant, person, AdminId);

        Assert.Null(_accounts.Accounts[StaffId].LockedUntil);
    }

    private sealed class RecordingGdpr : IGdprService
    {
        public readonly List<Guid> Erased = [];

        public Task<GdprExport?> BuildExportAsync(Guid staffKey, Guid tenantId) => Task.FromResult<GdprExport?>(null);

        public Task EraseAsync(Guid staffKey)
        {
            Erased.Add(staffKey);
            return Task.CompletedTask;
        }
    }
}
