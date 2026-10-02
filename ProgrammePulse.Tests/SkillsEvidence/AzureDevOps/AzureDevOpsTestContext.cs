using Microsoft.Extensions.Options;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Integrations.AzureDevOps;
using ProgrammePulse.Services.SkillsEvidence;

namespace ProgrammePulse.Tests.SkillsEvidence.AzureDevOps;

/// <summary>
/// The engineering-evidence fixture with an Azure DevOps organisation
/// connected in each of its two tenants. Built on
/// <see cref="EvidenceTestContext"/> rather than beside it, so both forges
/// write into the very same fake repository, resolver and gate — which is
/// the claim being tested: a second forge needs nothing new above Bronze.
/// </summary>
public sealed class AzureDevOpsTestContext
{
    public const string RepoA = "Web/portal";
    public const string RepoB = "Mobile/app";

    public EvidenceTestContext Evidence { get; } = new();

    public FakeAzureDevOpsEvidenceClient Client { get; } = new();

    public ExpiringCredentialProtector Protector { get; } = new();

    public AzureDevOpsEvidenceIngestionService Ingestion { get; }

    public EvidenceConnection ConnectionA { get; }

    public EvidenceConnection ConnectionB { get; }

    public AzureDevOpsTestContext()
    {
        Ingestion = new AzureDevOpsEvidenceIngestionService(
            Client, Evidence.Repository, Protector, Evidence.Resolver, Evidence.AuditLog, Evidence.Continuity,
            Evidence.SyncRuns, Options.Create(new AzureDevOpsEvidenceOptions { RawPayloadRetentionDays = 30 }),
            Evidence.Coordinator,
            Evidence.Time);

        ConnectionA = Connect(EvidenceTestContext.TenantA, "acme", RepoA);
        ConnectionB = Connect(EvidenceTestContext.TenantB, "rival", RepoB);
    }

    public EvidenceConnection Connect(Guid tenantId, string organisation, params string[] repositories)
    {
        var connection = new EvidenceConnection
        {
            ConnectionKey = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = AzureDevOpsEvidenceMapper.ProviderName,
            SourceAccountId = organisation,
            DisplayName = $"dev.azure.com/{organisation}",
            ApiBaseUrl = AzureDevOpsHostPolicy.BaseUrlFor(organisation)!,
            SelectedRepositories = repositories,
            Status = EvidenceConnectionStatus.Active,
            ProtectedCredentialJson = StubEvidenceCredentialProtector.Ciphertext,
            CreatedAtUtc = EvidenceTestContext.Start.UtcDateTime,
            UpdatedAtUtc = EvidenceTestContext.Start.UtcDateTime
        };

        Evidence.Repository.Connections.Add(connection);
        return connection;
    }

    public Task<AzureDevOpsIngestionResult> RunAsync(EvidenceConnection connection) =>
        Ingestion.RunAsync(connection.ConnectionKey, connection.TenantId, triggeredByMemberId: 1);

    public IReadOnlyList<EngineeringEvidence> EvidenceFor(EvidenceConnection connection) =>
        Evidence.Repository.Evidence.Where(e => e.ConnectionKey == connection.ConnectionKey).ToList();

    public EvidenceCoverage? Coverage(EvidenceConnection connection, EvidenceStream stream, string repositoryKey = RepoA) =>
        Evidence.Repository.Coverage.SingleOrDefault(c =>
            c.ConnectionKey == connection.ConnectionKey && c.Stream == stream
            && string.Equals(c.RepositoryKey, repositoryKey, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The stub protector plus an optional token expiry — the one credential
/// property Azure DevOps adds over GitHub.
/// </summary>
public sealed class ExpiringCredentialProtector : IEvidenceCredentialProtector
{
    public bool Readable { get; set; } = true;

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public string Protect(EvidenceCredential credential) => StubEvidenceCredentialProtector.Ciphertext;

    public EvidenceCredential? Unprotect(string? protectedCredentialJson) =>
        protectedCredentialJson is null || !Readable ? null : new EvidenceCredential("pat", ExpiresAtUtc);
}
