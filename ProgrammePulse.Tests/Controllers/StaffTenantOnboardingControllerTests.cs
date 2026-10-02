using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.Tenancy;

namespace ProgrammePulse.Tests.Controllers;

public class StaffTenantOnboardingControllerTests
{
    [Theory]
    [InlineData("Index")]
    [InlineData("ProvisionAdministrator")]
    public async Task Anonymous_and_tenant_administrators_cannot_operate_onboarding(string action)
    {
        Assert.IsType<ForbidResult>(await ActionGate.RunAsync<StaffTenantOnboardingController>(action, new FakeStaffAuthorizationService()));
        foreach (var role in StaffRole.All)
            Assert.IsType<ForbidResult>(await ActionGate.RunAsync<StaffTenantOnboardingController>(action, FakeStaffAuthorizationService.ForRole(role)));
        Assert.Null(await ActionGate.RunAsync<StaffTenantOnboardingController>(action, FakeStaffAuthorizationService.ForRole(StaffRole.PlatformAdmin)));
    }

    [Fact]
    public void Provisioning_requires_antiforgery()
    {
        Assert.NotNull(Attribute.GetCustomAttribute(
            typeof(StaffTenantOnboardingController).GetMethod("ProvisionAdministrator")!, typeof(ValidateAntiForgeryTokenAttribute)));
    }

    [Theory]
    [InlineData("", "admin@example.test", "long-enough-password")]
    [InlineData("Admin", "invalid", "long-enough-password")]
    [InlineData("Admin", "admin@example.test", "short")]
    public void Invalid_administrator_input_is_rejected(string name, string email, string password)
    {
        var input = new TenantAdministratorInput { FullName = name, Email = email, Password = password };
        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), [], true));
    }
}
