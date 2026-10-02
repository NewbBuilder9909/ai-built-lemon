using System.Net;
using ProgrammePulse.Tests.Personas;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// While no Umbraco content is published, Umbraco serves the no-content view
/// (the public home page) for every URL nothing else matched. A mistyped
/// link, including a stale /staffops bookmark, therefore answered 200 with
/// the marketing page: a soft 404 that tells a person nothing and that a
/// security scanner reports. Only "/" is the home page now.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class PublicNotFoundIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the public home page and not-found response were never rendered.";

    [Fact]
    public async Task The_home_page_is_the_only_address_that_gets_it()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        using var client = PersonaSignIn.CreateClient(factory);

        using var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("A weekly delivery review built from the evidence you already have.", await home.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/no-such-page")]
    [InlineData("/staffops/no-such-page")]
    public async Task Any_other_unmatched_address_is_a_real_404(string path)
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        using var client = PersonaSignIn.CreateClient(factory);

        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", body, StringComparison.Ordinal);
        Assert.DoesNotContain("A weekly delivery review built from the evidence you already have.", body, StringComparison.Ordinal);
    }
}
