using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ProgrammePulse.Controllers;
using ProgrammePulse.Startup;

namespace ProgrammePulse.Tests.Startup;

/// <summary>
/// Unhandled exceptions outside Development become a friendly page carrying
/// the request's correlation id, rather than a bare 500 with nothing a
/// person can quote to support.
/// </summary>
public class ErrorPagePolicyTests
{
    [Theory]
    [InlineData("Production", null, true)]
    [InlineData("Staging", null, true)]
    [InlineData("Development", null, false)]
    [InlineData("Development", "true", true)]
    [InlineData("Production", "false", false)]
    public void The_error_page_is_on_outside_development_unless_configured_otherwise(string environment, string? setting, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(setting is null ? [] : new Dictionary<string, string?> { [ErrorPagePolicy.SettingKey] = setting })
            .Build();

        Assert.Equal(expected, ErrorPagePolicy.UseErrorPage(configuration, environment));
    }

    [Fact]
    public void The_error_page_is_a_500_that_shows_the_correlation_id_and_nothing_else()
    {
        var http = new DefaultHttpContext { TraceIdentifier = "corr-123" };
        var sut = new ErrorController { ControllerContext = new ControllerContext { HttpContext = http } };

        var result = Assert.IsType<ViewResult>(sut.ServerError());

        Assert.Equal(StatusCodes.Status500InternalServerError, http.Response.StatusCode);
        Assert.Equal("corr-123", result.Model);
        Assert.Equal("~/Views/Errors/ServerError.cshtml", result.ViewName);
    }
}
