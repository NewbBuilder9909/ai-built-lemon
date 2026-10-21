using Microsoft.AspNetCore.Http;
using ProgrammePulse.Services.Security;

namespace ProgrammePulse.Tests.Security;

public class MfaChallengeCookieTests
{
    [Theory]
    [InlineData(false, CookieSecurePolicy.None, false, true)]
    [InlineData(false, CookieSecurePolicy.SameAsRequest, false, true)]
    [InlineData(true, CookieSecurePolicy.Always, false, true)]
    [InlineData(true, CookieSecurePolicy.SameAsRequest, true, true)]
    [InlineData(true, CookieSecurePolicy.SameAsRequest, false, false)]
    public void Secure_cookie_policy_is_always_secure_outside_development(
        bool isDevelopment, CookieSecurePolicy policy, bool requestIsHttps, bool expected)
    {
        Assert.Equal(expected, MfaChallengeStore.ShouldUseSecureCookie(isDevelopment, policy, requestIsHttps));
    }
}
