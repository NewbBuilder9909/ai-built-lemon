using ProgrammePulse.Models.SkillsEvidence;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// Language hints from changed paths.
///
/// The exclusions matter more than the classifications. A lockfile
/// refresh must not make someone look like a JSON expert, and checking in
/// a vendored dependency must not credit them with tens of thousands of
/// lines of someone else's language — those are the ways a
/// well-intentioned "languages you work in" feature quietly becomes
/// nonsense.
/// </summary>
public class ChangedPathClassifierTests
{
    [Theory]
    [InlineData("src/Billing.cs", "csharp")]
    [InlineData("web/app.tsx", "typescript")]
    [InlineData("infra/main.tf", "terraform")]
    [InlineData("Views/Index.cshtml", "razor")]
    [InlineData("scripts/deploy.ps1", "powershell")]
    [InlineData("Dockerfile", "docker")]
    [InlineData("docs/readme.md", "documentation")]
    public void A_source_path_is_classified_by_its_extension(string path, string expected) =>
        Assert.Equal(expected, ChangedPathClassifier.Classify(path));

    [Theory]
    [InlineData("node_modules/left-pad/index.js")]
    [InlineData("vendor/github.com/pkg/errors/errors.go")]
    [InlineData("src/bin/Debug/App.dll")]
    [InlineData("obj/Release/App.cs")]
    [InlineData("dist/bundle.js")]
    [InlineData("web/app.min.js")]
    [InlineData("Data/Model.g.cs")]
    [InlineData("Forms/Main.Designer.cs")]
    [InlineData("package-lock.json")]
    [InlineData("yarn.lock")]
    [InlineData("go.sum")]
    [InlineData("api/service.pb.go")]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData("__pycache__/thing.py")]
    public void Generated_and_vendored_paths_contribute_nothing(string path)
    {
        Assert.True(ChangedPathClassifier.IsExcluded(path));
        Assert.Null(ChangedPathClassifier.Classify(path));
    }

    [Fact]
    public void Exclusion_is_by_path_segment_so_a_real_directory_is_not_caught_by_a_substring()
    {
        // "vendor-portal" is somebody's actual work; "vendor" is not.
        Assert.False(ChangedPathClassifier.IsExcluded("vendor-portal/Program.cs"));
        Assert.Equal("csharp", ChangedPathClassifier.Classify("vendor-portal/Program.cs"));

        Assert.True(ChangedPathClassifier.IsExcluded("vendor/Program.cs"));
    }

    [Fact]
    public void A_language_needs_more_than_one_file_before_it_becomes_a_hint()
    {
        // A single incidental file is noise — a typo fix in a README must
        // not put "documentation" on somebody's evidence.
        Assert.Empty(ChangedPathClassifier.Hints(["docs/readme.md"]));
        Assert.Equal(["documentation"], ChangedPathClassifier.Hints(["docs/readme.md", "docs/setup.md"]));
    }

    [Fact]
    public void Hints_are_distinct_alphabetical_and_exclude_the_noise()
    {
        var hints = ChangedPathClassifier.Hints(
        [
            "src/Billing.cs", "src/Invoice.cs", "src/Ledger.cs",
            "web/app.ts", "web/api.ts",
            "package-lock.json", "node_modules/x/index.js", "dist/bundle.js",
            "README.md"
        ]);

        Assert.Equal(["csharp", "typescript"], hints);
    }

    [Fact]
    public void The_same_file_listed_twice_counts_once()
    {
        // Otherwise a rename or a repeated path would fabricate a hint.
        Assert.Empty(ChangedPathClassifier.Hints(["src/Billing.cs", "src/Billing.cs"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("LICENSE")]
    [InlineData("src/data.")]
    public void Paths_that_say_nothing_useful_produce_nothing(string? path) =>
        Assert.Null(ChangedPathClassifier.Classify(path));

    [Fact]
    public void A_null_or_empty_changeset_produces_no_hints()
    {
        Assert.Empty(ChangedPathClassifier.Hints(null));
        Assert.Empty(ChangedPathClassifier.Hints([]));
    }

    [Fact]
    public void Windows_separators_are_handled_the_same_as_forward_slashes()
    {
        Assert.Equal("csharp", ChangedPathClassifier.Classify(@"src\Billing.cs"));
        Assert.True(ChangedPathClassifier.IsExcluded(@"node_modules\x\index.js"));
    }
}
