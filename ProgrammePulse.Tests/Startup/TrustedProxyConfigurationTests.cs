using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Startup;

namespace ProgrammePulse.Tests.Startup;

public class TrustedProxyConfigurationTests
{
    [Theory]
    [InlineData("10.0.0.10", "203.0.113.7", "https")]
    [InlineData("10.0.0.11", "10.0.0.11", "http")]
    public async Task Only_the_configured_proxy_can_change_client_address_and_scheme(string remote, string expectedIp, string expectedScheme)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReverseProxy:Enabled"] = "true", ["ReverseProxy:KnownProxies:0"] = "10.0.0.10"
        }).Build();
        var services = new ServiceCollection().AddLogging();
        TrustedProxyConfiguration.Configure(services, configuration);
        using var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        app.UseForwardedHeaders();
        app.Run(_ => Task.CompletedTask);
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        await app.Build()(context);
        Assert.Equal(expectedIp, context.Connection.RemoteIpAddress!.ToString());
        Assert.Equal(expectedScheme, context.Request.Scheme);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("true", null)]
    [InlineData("true", "not-an-ip")]
    public void Missing_or_invalid_proxy_configuration_fails_closed(string? enabled, string? ip)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ReverseProxy:Enabled"] = enabled, ["ReverseProxy:KnownProxies:0"] = ip }).Build();
        var errors = new List<string>();
        TrustedProxyConfiguration.Validate(configuration, errors);
        Assert.NotEmpty(errors);
    }
}
