using ProgrammePulse.Controllers;

namespace ProgrammePulse.Tests.Controllers;

public class StaffAccountControllerTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Privileged_roles_require_mfa(bool isAdmin, bool isPlatformAdmin, bool expected)
    {
        Assert.Equal(expected, StaffAccountController.RequiresMfa(isAdmin, isPlatformAdmin));
    }
}
