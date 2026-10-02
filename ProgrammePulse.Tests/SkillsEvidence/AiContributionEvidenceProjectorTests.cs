using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;

namespace ProgrammePulse.Tests.SkillsEvidence;

public sealed class AiContributionEvidenceProjectorTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid Connection = Guid.NewGuid();
    private static readonly DateTime From = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = From.AddMonths(1);

    [Fact]
    public void Missing_signal_is_unknown_and_preserves_the_contribution()
    {
        var result = Build([Evidence()], []);
        var row = Assert.Single(result.Contributions);
        Assert.False(row.HasReportedAssistance);
        Assert.Empty(row.Assistance);
        Assert.Equal(Staff, result.StaffKey);
    }

    [Fact]
    public void Multiple_roles_and_tools_do_not_multiply_the_artefact()
    {
        var author = Evidence();
        var committer = author with { EvidenceKey = Guid.NewGuid(), Role = EvidenceRole.CommitCommitter };
        var signal = Observation(author);
        var otherTool = signal with { ObservationKey = Guid.NewGuid(), ToolName = "Copilot", SourceRecordId = "report-2" };
        var row = Assert.Single(Build([author, committer, author], [signal, signal, otherTool]).Contributions);
        Assert.Equal(2, row.Roles.Count);
        Assert.Equal(2, row.EvidenceKeys.Count);
        Assert.Equal(2, row.Assistance.Count);
        Assert.Single(row.SourceUrls);
    }

    [Fact]
    public void Bot_unmapped_and_other_subject_evidence_never_reaches_the_person()
    {
        var entry = Evidence();
        var result = Build([
            entry with { ActorIsBot = true },
            entry with { AttributionStatus = EvidenceAttributionStatus.Unmapped },
            entry with { AttributionStatus = EvidenceAttributionStatus.Ambiguous },
            entry with { StaffKey = null },
            entry with { StaffKey = Guid.NewGuid() },
            entry with { TenantId = Guid.NewGuid() }
        ], [Observation(entry)]);
        Assert.Empty(result.Contributions);
    }

    [Fact]
    public void Same_external_id_in_another_scope_does_not_attach_assistance()
    {
        var entry = Evidence();
        var signal = Observation(entry);
        var key = signal.Artifact;
        var differentKeys = new[]
        {
            key with { TenantId = Guid.NewGuid() },
            key with { ConnectionKey = Guid.NewGuid() },
            key with { Provider = "OtherForge" },
            key with { SourceAccountId = "other-org" },
            key with { RepositoryKey = "org/other-repo" },
            key with { SourceType = EvidenceSourceType.PullRequest },
            key with { ExternalId = "different-sha" }
        };
        foreach (var differentKey in differentKeys)
            Assert.Empty(Assert.Single(Build([entry], [signal with { Artifact = differentKey }]).Contributions).Assistance);
    }

    [Fact]
    public void Window_uses_contribution_date_includes_start_and_excludes_end()
    {
        var start = Evidence() with { OccurredAtUtc = From };
        var before = start with { ExternalId = "before", OccurredAtUtc = From.AddTicks(-1) };
        var end = start with { ExternalId = "end", OccurredAtUtc = To };
        // Late discovery of an old contribution still annotates that contribution.
        var signal = Observation(start) with { ObservedAtUtc = new DateTimeOffset(To.AddDays(1)) };
        var row = Assert.Single(Build([start, before, end], [signal]).Contributions);
        Assert.Equal(start.ExternalId, row.Artifact.ExternalId);
        Assert.True(row.HasReportedAssistance);
    }

    [Fact]
    public void Reviewing_an_AI_assisted_PR_preserves_reviewer_role_and_source_claim()
    {
        var reviewer = Evidence() with { Role = EvidenceRole.Reviewer, SourceType = EvidenceSourceType.PullRequest };
        var signal = Observation(reviewer) with { SignalKind = AiAssistanceSignalKind.ProviderReported };
        var row = Assert.Single(Build([reviewer], [signal]).Contributions);
        Assert.Equal(EvidenceRole.Reviewer, Assert.Single(row.Roles));
        Assert.Equal(AiAssistanceSignalKind.ProviderReported, Assert.Single(row.Assistance).SignalKind);
        Assert.Equal(AiAssistanceWorkflow.CodeGeneration, row.Assistance[0].Workflow);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.com/commit/1")]
    [InlineData("/relative/commit/1")]
    public void Unsafe_links_are_omitted_without_dropping_participation(string url)
    {
        var row = Assert.Single(Build([Evidence() with { SourceUrl = url }], []).Contributions);
        Assert.Empty(row.SourceUrls);
        Assert.Single(row.Roles);
    }

    [Fact]
    public void Incomplete_or_unknown_signal_contract_is_not_displayed_as_assistance()
    {
        var entry = Evidence();
        var signal = Observation(entry);
        var row = Assert.Single(Build([entry], [
            signal with { ToolName = " " },
            signal with { SourceRecordId = "" },
            signal with { SignalKind = (AiAssistanceSignalKind)99 },
            signal with { Workflow = (AiAssistanceWorkflow)99 }
        ]).Contributions);
        Assert.False(row.HasReportedAssistance);
    }

    [Fact]
    public void Invalid_windows_and_unresolved_subjects_are_refused()
    {
        Assert.Throws<ArgumentException>(() => AiContributionEvidenceProjector.Build(Tenant, Staff, To, From, [], []));
        Assert.Throws<ArgumentException>(() => AiContributionEvidenceProjector.Build(Tenant, Staff, From, From, [], []));
        Assert.Throws<ArgumentException>(() => AiContributionEvidenceProjector.Build(Tenant, Staff, DateTime.SpecifyKind(From, DateTimeKind.Unspecified), To, [], []));
        Assert.Throws<ArgumentException>(() => AiContributionEvidenceProjector.Build(Guid.Empty, Staff, From, To, [], []));
        Assert.Throws<ArgumentException>(() => AiContributionEvidenceProjector.Build(Tenant, Guid.Empty, From, To, [], []));
    }

    private static AiContributionEvidencePortfolio Build(IReadOnlyList<EngineeringEvidence> evidence, IReadOnlyList<AiAssistanceObservation> signals) =>
        AiContributionEvidenceProjector.Build(Tenant, Staff, From, To, evidence, signals);

    private static EngineeringEvidence Evidence() => new()
    {
        EvidenceKey = Guid.NewGuid(), TenantId = Tenant, ConnectionKey = Connection,
        Provider = "GitHub", SourceAccountId = "org", RepositoryKey = "org/repo",
        SourceType = EvidenceSourceType.Commit, ExternalId = "sha-1", Role = EvidenceRole.CommitAuthor,
        ActorIsBot = false, StaffKey = Staff, AttributionStatus = EvidenceAttributionStatus.Mapped,
        SourceUrl = "https://github.com/org/repo/commit/sha-1", OccurredAtUtc = From.AddDays(2),
        SchemaVersion = 1, FirstIngestedAtUtc = From.AddDays(3), UpdatedAtUtc = From.AddDays(3)
    };

    private static AiAssistanceObservation Observation(EngineeringEvidence evidence) => new()
    {
        ObservationKey = Guid.NewGuid(), Artifact = ContributionArtifactKey.From(evidence),
        ToolName = "Claude", SignalKind = AiAssistanceSignalKind.CommitTrailer,
        Workflow = AiAssistanceWorkflow.CodeGeneration, SourceRecordId = "commit:sha-1:co-author",
        ObservedAtUtc = new DateTimeOffset(From.AddDays(3))
    };
}
