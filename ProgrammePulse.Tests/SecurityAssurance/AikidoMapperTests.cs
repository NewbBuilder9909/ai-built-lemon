using System.Text.Json;
using ProgrammePulse.Models.SecurityAssurance;
using ProgrammePulse.Services.Integrations.Aikido;

namespace ProgrammePulse.Tests.SecurityAssurance;

/// <summary>
/// The Aikido mapping, against payloads in the shapes the live workspace
/// returned on 26 September 2026 (docs/delivery-evidence-and-contract-assurance.md).
/// </summary>
public sealed class AikidoMapperTests
{
    private static readonly Guid Tenant = Guid.Parse("c0a70000-0000-0000-0000-000000000004");
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("https://api.github.com/repos/Acme/Api", "acme", "acme/api")]
    [InlineData("https://api.github.com/repos/acme/web.site/", "acme", "acme/web.site")]
    [InlineData("https://github.com/acme/api.git", "acme", "acme/api")]
    public void GitHub_owner_and_name_come_from_the_API_or_web_url(string url, string owner, string key)
    {
        var repository = AikidoMapper.GitHubRepository(url);

        Assert.NotNull(repository);
        Assert.Equal(("GitHub", owner, key), (repository!.Provider, repository.SourceAccountId, repository.RepositoryKey));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/newbbuilder9909")]
    [InlineData("https://api.github.com/repos/acme/api/pulls")]
    [InlineData("https://evil.example/repos/acme/api")]
    [InlineData("http://api.github.com/repos/acme/api")]
    [InlineData("")]
    [InlineData(null)]
    public void A_url_that_is_not_exactly_one_repository_yields_nothing(string? url)
    {
        Assert.Null(AikidoMapper.GitHubRepository(url));
    }

    [Fact]
    public void A_repository_with_no_check_configuration_is_observed_as_not_gated()
    {
        var observations = AikidoMapper.Observations(Tenant,
            Items("""[{"id":1,"provider":"github","url":"https://api.github.com/repos/acme/api","external_repo_id":"R_kgDO1","last_scanned_at":1758800000}]"""),
            [], Now);

        var observation = Assert.Single(observations);
        Assert.False(observation.GateConfigured);
        Assert.Null(observation.GateMinimumSeverity);
        Assert.Equal(("1", "R_kgDO1"), (observation.ToolRepositoryId, observation.ExternalRepoId));
        Assert.NotNull(observation.LastScannedAtUtc);
    }

    [Fact]
    public void Check_configuration_maps_threshold_and_flags_and_a_disabled_one_is_no_gate()
    {
        var observations = AikidoMapper.Observations(Tenant,
            Items("""
                [{"id":1,"provider":"github","url":"https://api.github.com/repos/acme/api"},
                 {"id":2,"provider":"github","url":"https://api.github.com/repos/acme/web"},
                 {"id":3,"provider":"github","url":"https://api.github.com/repos/acme/old"}]
                """),
            Items("""
                [{"code_repo_id":1,"minimum_severity":"high","fail_on_dependency_scan":true,"fail_on_sast_scan":true,"fail_on_secrets_scan":true},
                 {"code_repo_id":2,"minimum_severity":"always_pass_check","fail_on_dependency_scan":true,"fail_on_sast_scan":true,"fail_on_secrets_scan":true},
                 {"code_repo_id":3,"is_enabled":false,"minimum_severity":"low","fail_on_dependency_scan":true,"fail_on_sast_scan":true,"fail_on_secrets_scan":true}]
                """),
            Now).ToDictionary(o => o.ToolRepositoryId);

        Assert.Equal((true, SecuritySeverity.High, true, true, true),
            (observations["1"].GateConfigured, observations["1"].GateMinimumSeverity, observations["1"].FailsOnDependencies, observations["1"].FailsOnCode, observations["1"].FailsOnSecrets));
        Assert.Equal((true, (SecuritySeverity?)null), (observations["2"].GateConfigured, observations["2"].GateMinimumSeverity));
        Assert.False(observations["3"].GateConfigured);
        Assert.Null(observations["1"].LastScannedAtUtc);
    }

    [Fact]
    public void Non_GitHub_unparseable_and_colliding_repositories_are_left_out()
    {
        var observations = AikidoMapper.Observations(Tenant,
            Items("""
                [{"id":1,"provider":"gitlab","url":"https://gitlab.com/acme/api"},
                 {"id":2,"provider":"github","url":"https://api.github.com/repos/acme"},
                 {"id":3,"provider":"github","url":"https://api.github.com/repos/acme/dup"},
                 {"id":4,"provider":"github","url":"https://github.com/ACME/dup"},
                 {"id":5,"provider":"github","url":"https://api.github.com/repos/acme/ok"}]
                """),
            [], Now);

        Assert.Equal(["5"], observations.Select(o => o.ToolRepositoryId));
    }

    [Fact]
    public void Findings_keep_metadata_only_and_join_through_the_tool_repository_id()
    {
        var observations = AikidoMapper.Observations(Tenant,
            Items("""[{"id":7,"provider":"github","url":"https://api.github.com/repos/acme/api"}]"""), [], Now);

        var findings = AikidoMapper.Findings(Tenant, Items("""
            [{"id":11,"type":"open_source","severity":"critical","status":"ignored","code_repo_id":7,"cve_id":"CVE-2026-1",
              "affected_package":"lodash","affected_file":"src/secret/path.js","start_line":10,"first_detected_at":1758000000},
             {"id":12,"type":"sast","severity":"high","status":"closed","code_repo_id":7,"rule_id":"sql-injection",
              "first_detected_at":1758000000,"closed_at":1758500000},
             {"id":13,"type":"cloud","severity":"high","status":"open","first_detected_at":1758000000},
             {"id":14,"type":"open_source","severity":"high","status":"open","code_repo_id":99,"first_detected_at":1758000000},
             {"id":15,"type":"open_source","severity":"urgent","status":"open","code_repo_id":7,"first_detected_at":1758000000}]
            """), observations, Now).ToDictionary(f => f.ExternalId);

        Assert.Equal(["11", "12"], findings.Keys.Order());
        Assert.Equal((SecuritySeverity.Critical, SecurityFindingStatus.Ignored, "CVE-2026-1", "lodash", true),
            (findings["11"].Severity, findings["11"].Status, findings["11"].CveId, findings["11"].AffectedPackage, findings["11"].IsOutstanding));
        Assert.Equal(("sql-injection", false), (findings["12"].RuleId, findings["12"].IsOutstanding));
        Assert.NotNull(findings["12"].ClosedAtUtc);
        Assert.Equal("acme/api", findings["11"].Repository.RepositoryKey);
    }

    [Fact]
    public void Check_runs_map_outcomes_and_unknown_values_stay_unknown()
    {
        var observations = AikidoMapper.Observations(Tenant,
            Items("""[{"id":7,"provider":"github","url":"https://api.github.com/repos/acme/api"}]"""), [], Now);

        var runs = AikidoMapper.CheckRuns(Tenant, Items("""
            [{"scan_id":1,"code_repo_id":7,"gate_status":"bypassed","started_at":1758000000,"related_commit_sha":"abc"},
             {"scan_id":2,"code_repo_id":7,"gate_status":"exploded","started_at":1758000000},
             {"scan_id":3,"code_repo_id":8,"gate_status":"failed","started_at":1758000000}]
            """), observations);

        Assert.Equal([CheckRunOutcome.Bypassed, CheckRunOutcome.Unknown], runs.Select(r => r.Outcome));
    }

    [Fact]
    public void List_items_are_read_from_a_bare_array_or_a_wrapping_object()
    {
        using var bare = JsonDocument.Parse("""[{"id":1},{"id":2}]""");
        using var wrapped = JsonDocument.Parse("""{"total":2,"issues":[{"id":1},{"id":2}]}""");

        Assert.Equal(2, AikidoMapper.Items(bare.RootElement).Count);
        Assert.Equal(2, AikidoMapper.Items(wrapped.RootElement).Count);
    }

    [Fact]
    public void Redaction_keeps_only_allow_listed_fields()
    {
        using var document = JsonDocument.Parse("""{"id":1,"severity":"high","affected_file":"src/a.js","start_line":4,"title":"Hard-coded key in billing"}""");

        var redacted = AikidoClient.Redact(document.RootElement, AikidoClient.IssueFields).GetRawText();

        Assert.Contains("\"severity\"", redacted);
        Assert.DoesNotContain("affected_file", redacted);
        Assert.DoesNotContain("start_line", redacted);
        Assert.DoesNotContain("billing", redacted);
    }

    [Fact]
    public void No_allow_list_admits_a_path_line_title_or_code_field()
    {
        var forbidden = new[] { "affected_file", "file", "path", "start_line", "end_line", "title", "description", "snippet", "code", "branch_name" };
        var lists = AikidoClient.IssueFields.Concat(AikidoClient.RepositoryFields).Concat(AikidoClient.CheckConfigurationFields).Concat(AikidoClient.CheckRunFields);

        Assert.Empty(lists.Intersect(forbidden));
    }

    private static IReadOnlyList<JsonElement> Items(string json)
    {
        using var document = JsonDocument.Parse(json);
        return AikidoMapper.Items(document.RootElement);
    }
}
