using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Tests.ProgrammeOps;

namespace ProgrammePulse.Tests.Controllers;

/// <summary>
/// Regression coverage for the ManageStaff gate every action in
/// StaffAdminController declares. This is the highest-risk surface in the app
/// per the controller's own doc comment (cost/rate data). Also covers the
/// tenant boundary layered on top of it: an Admin whose tenant can't be
/// resolved is forbidden, and an Admin can't reach a StaffKey that belongs to
/// another tenant (NotFound, so the key's existence isn't confirmed either).
///
/// The refusals are the declared [RequireCapability] / [CurrentTenant] gate,
/// run through ActionGate. The cross-tenant test calls the action body
/// directly with the tenant the binder would supply, so it needs the staff
/// repository fake; every other dependency can be null!.
/// </summary>
public class StaffAdminControllerTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static StaffAdminController BuildSut(FakeStaffRepository staff) =>
        new(new FakeCurrentStaff(), new StaffAdminService(staff, null!, null!, null!, null!, null!, null!, null!, TimeProvider.System), null!);

    private static async Task<IActionResult> Invoke(StaffAdminController sut, string action, Guid tenantId, Guid staffKey) => action switch
    {
        "Detail" => await sut.Detail(tenantId, staffKey),
        "SetRate" => await sut.SetRate(tenantId, staffKey, 10m, "GBP"),
        "SetWorkHours" => await sut.SetWorkHours(tenantId, staffKey, 37.5m),
        "Export" => await sut.Export(tenantId, staffKey),
        "Erase" => await sut.Erase(tenantId, staffKey),
        "ResetMfa" => await sut.ResetMfa(tenantId, staffKey),
        "SetRoles" => await sut.SetRoles(tenantId, staffKey, [StaffRole.Staff]),
        "SetAccess" => await sut.SetAccess(tenantId, staffKey, false),
        "Unlock" => await sut.Unlock(tenantId, staffKey),
        "PasswordReset" => await sut.PasswordReset(tenantId, staffKey),
        "Delete" => await sut.Delete(tenantId, staffKey),
        _ => throw new InvalidOperationException($"Unhandled action '{action}'.")
    };

    [Theory]
    [InlineData("Index")]
    [InlineData("Create_Get")]
    [InlineData("Create_Post")]
    [InlineData("Detail")]
    [InlineData("SetRate")]
    [InlineData("SetWorkHours")]
    [InlineData("Export")]
    [InlineData("Erase")]
    [InlineData("ResetMfa")]
    [InlineData("SetRoles")]
    [InlineData("SetAccess")]
    [InlineData("Unlock")]
    [InlineData("PasswordReset")]
    [InlineData("Delete")]
    [InlineData("Audit")]
    public async Task Forbids_non_admin_callers(string action)
    {
        var result = await ActionGate.RunAsync<StaffAdminController>(action, FakeStaffAuthorizationService.Nobody());

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [InlineData("Index")]
    [InlineData("Create_Get")]
    [InlineData("Create_Post")]
    [InlineData("Detail")]
    [InlineData("SetRate")]
    [InlineData("SetWorkHours")]
    [InlineData("Export")]
    [InlineData("Erase")]
    [InlineData("ResetMfa")]
    [InlineData("SetRoles")]
    [InlineData("SetAccess")]
    [InlineData("Unlock")]
    [InlineData("PasswordReset")]
    [InlineData("Delete")]
    [InlineData("Audit")]
    public async Task Forbids_an_admin_whose_tenant_cannot_be_resolved(string action)
    {
        var result = await ActionGate.RunAsync<StaffAdminController>(
            action, FakeStaffAuthorizationService.ForRole(StaffRole.Admin), new FakeTenantContext { IsResolved = false });

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [InlineData("Detail")]
    [InlineData("SetRate")]
    [InlineData("SetWorkHours")]
    [InlineData("Export")]
    [InlineData("Erase")]
    [InlineData("ResetMfa")]
    [InlineData("SetRoles")]
    [InlineData("SetAccess")]
    [InlineData("Unlock")]
    [InlineData("PasswordReset")]
    [InlineData("Delete")]
    public async Task Per_staff_actions_return_not_found_for_a_staff_key_in_another_tenant(string action)
    {
        var staff = new FakeStaffRepository();
        var otherTenantsPerson = new StaffProfile
        {
            StaffKey = Guid.NewGuid(),
            MemberId = 7,
            FullName = "Other Org Person",
            Email = "other@example.com",
            TenantId = TenantB,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        staff.Staff.Add(otherTenantsPerson);

        var result = await Invoke(BuildSut(staff), action, TenantA, otherTenantsPerson.StaffKey);

        Assert.IsType<NotFoundResult>(result);
    }
}
