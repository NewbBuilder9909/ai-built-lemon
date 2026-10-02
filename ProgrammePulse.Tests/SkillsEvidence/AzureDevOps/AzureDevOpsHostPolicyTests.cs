using ProgrammePulse.Services.Integrations.AzureDevOps;

namespace ProgrammePulse.Tests.SkillsEvidence.AzureDevOps;

/// <summary>
/// The allow-list that decides where a tenant's Azure DevOps token may be
/// sent. Pure, so every exfiltration shape can be listed as data.
/// </summary>
public sealed class AzureDevOpsHostPolicyTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("Acme-Ltd")]
    [InlineData("a")]
    [InlineData("a1b2")]
    public void Plausible_organisation_names_are_accepted(string organisation) =>
        Assert.True(AzureDevOpsHostPolicy.IsValidOrganisation(organisation));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acme.ltd")]
    [InlineData("acme/other")]
    [InlineData("acme?x=1")]
    [InlineData("ThisOrganisationNameIsFarTooLongToBeAcceptedByAzure1")]
    public void Anything_that_could_change_the_url_shape_is_refused(string? organisation) =>
        Assert.False(AzureDevOpsHostPolicy.IsValidOrganisation(organisation));

    [Theory]
    [InlineData("https://dev.azure.com/acme", "https://dev.azure.com/acme")]
    [InlineData("https://DEV.azure.com/Acme/", "https://dev.azure.com/acme")]
    public void The_stored_base_is_canonicalised(string candidate, string expected) =>
        Assert.Equal(expected, AzureDevOpsHostPolicy.Canonicalize(candidate));

    [Theory]
    [InlineData("http://dev.azure.com/acme")]
    [InlineData("https://acme.visualstudio.com")]
    [InlineData("https://dev.azure.com.attacker.example/acme")]
    [InlineData("https://attacker.example/acme")]
    [InlineData("https://dev.azure.com/acme/project")]
    [InlineData("https://dev.azure.com/")]
    [InlineData("https://dev.azure.com/acme?x=1")]
    [InlineData("https://user:pass@dev.azure.com/acme")]
    [InlineData("https://dev.azure.com:8443/acme")]
    [InlineData("https://dev.azure.com/acme#frag")]
    [InlineData("not a url")]
    public void Any_other_host_or_shape_is_rejected(string candidate) =>
        Assert.Null(AzureDevOpsHostPolicy.Canonicalize(candidate));

    [Theory]
    [InlineData("Web/portal")]
    [InlineData("Web Platform/api (v2)")]
    [InlineData("Données/entrepôt")]
    public void Valid_repository_keys(string key) =>
        Assert.True(AzureDevOpsHostPolicy.IsValidRepositoryKey(key));

    [Theory]
    [InlineData("portal")]
    [InlineData("a/b/c")]
    [InlineData("../portal")]
    [InlineData("Web/..")]
    [InlineData("Web/portal.")]
    [InlineData("Web/por?tal")]
    [InlineData("Web/por%2Ftal")]
    [InlineData(" Web/portal")]
    public void Invalid_repository_keys(string key) =>
        Assert.False(AzureDevOpsHostPolicy.IsValidRepositoryKey(key));

    [Fact]
    public void A_selection_takes_its_spelling_from_what_the_token_can_read()
    {
        Assert.True(AzureDevOpsHostPolicy.IsValidSelection(
            ["web/PORTAL", "Web/portal", "Mobile/app"], ["Web/portal", "Mobile/app", "Ops/infra"], out var selection));

        Assert.Equal(["Web/portal", "Mobile/app"], selection);
    }

    [Fact]
    public void A_repository_the_token_cannot_read_invalidates_the_whole_selection()
    {
        Assert.False(AzureDevOpsHostPolicy.IsValidSelection(["Web/portal", "Finance/ledger"], ["Web/portal"], out _));
    }

    [Fact]
    public void An_empty_or_oversized_selection_is_refused()
    {
        Assert.False(AzureDevOpsHostPolicy.IsValidSelection([], ["Web/portal"], out _));
        Assert.False(AzureDevOpsHostPolicy.IsValidSelection(null, ["Web/portal"], out _));

        var many = Enumerable.Range(0, AzureDevOpsHostPolicy.MaxSelectedRepositories + 1).Select(i => $"Web/repo{i}").ToList();
        Assert.False(AzureDevOpsHostPolicy.IsValidSelection(many, many, out _));
    }
}
