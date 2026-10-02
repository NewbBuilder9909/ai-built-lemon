using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using ProgrammePulse.Middleware;

namespace ProgrammePulse.Tests.Middleware;

public class SecurityHeadersMiddlewareTests
{
    [Theory]
    [InlineData("/umbraco", false)]
    [InlineData("/staffops/account/mfa/enroll", true)]
    public async Task Backoffice_has_baseline_csp_and_mfa_pages_cannot_be_cached(string path, bool account)
    {
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().Configure(app =>
        {
            app.UseMiddleware<SecurityHeadersMiddleware>();
            app.Run(context => context.Response.WriteAsync("test"));
        })).StartAsync();
        using var client = host.GetTestClient();
        using var response = await client.GetAsync(path);
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("frame-ancestors 'self'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("base-uri 'self'", csp);
        if (account)
        {
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Contains("script-src 'self'", csp);
        }
    }
}
