using ProgrammePulse.Models.SecurityAssurance;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Services.SecurityAssurance;

/// <summary>
/// What the scanning tool currently says about one tenant, as other areas
/// (Contract Ops' assurance page) read it. Empty, with
/// <see cref="Connected"/> false, when the plan doesn't include the feature
/// or no tool is connected; the reader then relies on attestations alone.
/// </summary>
public sealed record SecurityAssuranceSnapshot(
    bool Connected,
    DateTime? LastSyncSucceededAtUtc,
    IReadOnlyList<RepositoryGateObservation> Observations,
    IReadOnlyList<SecurityFinding> OutstandingFindings,
    IReadOnlyList<SecurityCheckRun> RecentCheckRuns)
{
    public static SecurityAssuranceSnapshot None { get; } = new(false, null, [], [], []);

    /// <summary>
    /// Check-run fields have not yet been read from a live, gated repository
    /// (docs/delivery-evidence-and-contract-assurance.md), so pages label them
    /// unverified. Flip this once a probe run confirms them.
    /// </summary>
    public bool CheckRunsVerified => false;
}

/// <summary>A tool connection as the admin page shows it: never the credential, only whether one is stored.</summary>
public sealed record SecurityConnectionSummary(
    string Tool,
    string Region,
    string ClientId,
    SecurityConnectionStatus Status,
    bool HasCredential,
    DateTime? LastSyncStartedAtUtc,
    DateTime? LastSyncSucceededAtUtc,
    string? LastSyncError);

public interface ISecurityAssuranceQueryService
{
    /// <param name="checkRunsSinceUtc">How far back to read pull-request check runs.</param>
    Task<SecurityAssuranceSnapshot> GetSnapshotAsync(Guid tenantId, DateTime checkRunsSinceUtc);

    Task<SecurityConnectionSummary?> GetConnectionAsync(Guid tenantId, string tool);
}

public sealed class SecurityAssuranceQueryService(
    ISecurityAssuranceRepository repository,
    IFeatureGate featureGate) : ISecurityAssuranceQueryService
{
    public async Task<SecurityAssuranceSnapshot> GetSnapshotAsync(Guid tenantId, DateTime checkRunsSinceUtc)
    {
        if (!await featureGate.IsEnabledAsync(ProductFeature.SecurityAssurance))
        {
            return SecurityAssuranceSnapshot.None;
        }

        var connection = await repository.GetConnectionAsync(tenantId, SecurityTools.Aikido);
        if (connection is null || connection.Status == SecurityConnectionStatus.Disconnected || connection.LastSyncSucceededAtUtc is null)
        {
            return SecurityAssuranceSnapshot.None;
        }

        // Contract gates are High or Critical, so only those findings are read.
        // Findings of a repository the tool no longer lists are left out: their
        // status stopped updating, so neither "open" nor "closed" can be claimed.
        var observations = await repository.GetObservationsAsync(tenantId);
        var findings = (await repository.GetOutstandingFindingsAsync(tenantId, SecuritySeverity.High))
            .Where(f => observations.Any(o => o.Repository.SameRepositoryAs(f.Repository)))
            .ToList();

        return new SecurityAssuranceSnapshot(
            true,
            connection.LastSyncSucceededAtUtc,
            observations,
            findings,
            await repository.GetCheckRunsSinceAsync(tenantId, checkRunsSinceUtc));
    }

    public async Task<SecurityConnectionSummary?> GetConnectionAsync(Guid tenantId, string tool) =>
        await repository.GetConnectionAsync(tenantId, tool) is { } c
            ? new SecurityConnectionSummary(c.Tool, c.Region, c.ClientId, c.Status, !string.IsNullOrEmpty(c.ProtectedCredentialJson),
                c.LastSyncStartedAtUtc, c.LastSyncSucceededAtUtc, c.LastSyncError)
            : null;
}

/// <summary>The scanning tools this area knows, as the plain strings stored with each row.</summary>
public static class SecurityTools
{
    public const string Aikido = "Aikido";
}
