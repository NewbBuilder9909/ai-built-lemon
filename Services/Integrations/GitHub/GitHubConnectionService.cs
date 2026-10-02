using System.Text.Json;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.Integrations.GitHub;

/// <summary>
/// Completes a GitHub App installation once the callback has been bound to
/// the session that started it (the controller's job): verify the grant
/// with GitHub, refuse an account other than the one the admin named,
/// validate the granted repositories, and store the connection encrypted
/// and audited. Moved verbatim out of StaffEvidenceConnectionController.
/// </summary>
public interface IGitHubConnectionService
{
    Task<CommandResult> CompleteInstallationAsync(
        Guid tenantId, Guid staffKey, int actorMemberId,
        string apiBaseUrl, string intendedAccount, string code, string? installationId,
        CancellationToken cancellationToken);
}

public sealed class GitHubConnectionService(
    IEngineeringEvidenceRepository evidenceRepository,
    IEvidenceCredentialProtector protector,
    IGitHubEvidenceClient gitHubClient,
    ISkillsEvidenceAuditLogRepository auditLog,
    TimeProvider timeProvider) : IGitHubConnectionService
{
    public async Task<CommandResult> CompleteInstallationAsync(
        Guid tenantId, Guid staffKey, int actorMemberId,
        string apiBaseUrl, string intendedAccount, string code, string? installationId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // The token exchange is the one piece that needs a live GitHub App
        // registration to test, so it is behind the client interface and
        // the rest of the flow is exercisable without one.
        string? grantedAccount;
        try
        {
            grantedAccount = await gitHubClient.VerifyInstallationAsync(apiBaseUrl, code, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            return CommandResult.Refused("GitHub authorisation could not be completed. Check the app installation and try again.");
        }

        if (grantedAccount is null)
        {
            return CommandResult.Refused("That authorisation grants no readable repositories. Select at least one repository during installation.");
        }

        // The admin said one organisation; GitHub granted another. Refuse
        // rather than connect something they did not intend.
        if (!string.Equals(grantedAccount, intendedAccount, StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Refused($"You authorised '{grantedAccount}', not '{intendedAccount}'. Start again and choose the intended organisation.");
        }

        var accessible = await gitHubClient.GetAccessibleRepositoriesAsync(apiBaseUrl, code, cancellationToken);
        if (!EvidenceHostPolicy.IsValidSelection(accessible, grantedAccount, out var selection))
        {
            return CommandResult.Refused("The granted repositories could not be validated. Check the installation covers repositories in that organisation only.");
        }

        var existing = await evidenceRepository.GetConnectionByAccountAsync(
            GitHubEvidenceMapper.ProviderName, grantedAccount, tenantId);

        var connection = await evidenceRepository.UpsertConnectionAsync(new EvidenceConnection
        {
            ConnectionKey = existing?.ConnectionKey ?? Guid.NewGuid(),
            TenantId = tenantId,
            Provider = GitHubEvidenceMapper.ProviderName,
            SourceAccountId = grantedAccount,
            DisplayName = $"{new Uri(apiBaseUrl).Host}/{grantedAccount}",
            InstallationId = installationId,
            ApiBaseUrl = apiBaseUrl,
            SelectedRepositories = selection,
            Status = EvidenceConnectionStatus.Active,
            ProtectedCredentialJson = protector.Protect(new EvidenceCredential(code, InstallationId: installationId)),
            ConnectedByStaffKey = staffKey,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now
        });

        await auditLog.LogAsync(SkillsEvidenceAuditAction.EntityTypeConnection, connection.ConnectionKey.ToString(),
            existing is null ? SkillsEvidenceAuditAction.ConnectionCreated : SkillsEvidenceAuditAction.ConnectionUpdated,
            actorMemberId, JsonSerializer.Serialize(new { account = grantedAccount, repositories = selection.Count }),
            timeProvider.GetUtcNow().UtcDateTime, tenantId);

        return CommandResult.Succeeded($"Connected to {grantedAccount} with {selection.Count} repositories. Run a sync to collect evidence.", connection.ConnectionKey);
    }
}
