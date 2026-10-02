using Microsoft.Extensions.Configuration;
using ProgrammePulse.Startup;

namespace ProgrammePulse.Tests.Startup;

/// <summary>
/// Making the login limit configurable (so the persona harness can raise it
/// in its own test host) must not weaken it anywhere else. The first test is
/// the one that matters: an appsettings file that says nothing about rate
/// limiting still gets the original hardcoded 5/minute login and 3/minute
/// sync limits.
/// </summary>
public class RateLimitSettingsTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Absent_configuration_keeps_the_previous_hardcoded_limits()
    {
        var settings = RateLimitSettings.FromConfiguration(Configuration());

        Assert.Equal(5, settings.LoginPermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.LoginWindow);
        Assert.Equal(3, settings.SyncPermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.SyncWindow);
        Assert.Equal(RateLimitSettings.Defaults, settings);
    }

    [Fact]
    public void Configured_values_override_each_policy_independently()
    {
        var settings = RateLimitSettings.FromConfiguration(Configuration(
            ("RateLimiting:Login:PermitLimit", "50"),
            ("RateLimiting:Login:WindowSeconds", "30")));

        Assert.Equal(50, settings.LoginPermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.LoginWindow);

        // Sync said nothing, so it keeps its default rather than inheriting Login's.
        Assert.Equal(3, settings.SyncPermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.SyncWindow);
    }

    /// <summary>
    /// A typo in a rate limit must not take the site down, and a zero or
    /// negative permit limit would refuse every login attempt — fall back to
    /// the default instead of honouring it.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData("five")]
    public void Invalid_permit_limits_fall_back_to_the_default(string configured)
    {
        var settings = RateLimitSettings.FromConfiguration(Configuration(
            ("RateLimiting:Login:PermitLimit", configured)));

        Assert.Equal(5, settings.LoginPermitLimit);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-60")]
    [InlineData("a minute")]
    public void Invalid_windows_fall_back_to_the_default(string configured)
    {
        var settings = RateLimitSettings.FromConfiguration(Configuration(
            ("RateLimiting:Login:WindowSeconds", configured)));

        Assert.Equal(TimeSpan.FromMinutes(1), settings.LoginWindow);
    }
}
