using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ProgrammePulse.Tests.Integrations;

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Tests";
    public string ContentRootPath { get; set; } = ".";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
