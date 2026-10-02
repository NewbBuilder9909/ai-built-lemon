using System.Text.Json;
using ProgrammePulse.Models.Integrations.AzureDevOps.Raw;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.ViewModels.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.Integrations.AzureDevOps;

/// <summary>
/// Connecting an Azure DevOps organisation as an evidence source: verify a
/// read-only personal access token, store it encrypted, and choose
/// repositories from those it can actually read. Moved verbatim out of
/// StaffAzureDevOpsEvidenceController. Every refusal is explained, and every
/// change is audited against the connection.
/// </summary>
public interface IAzureDevOpsConnectionService
{
    /// <summary>Succeeded carries the connection key, so the caller can go straight to the repository picker.</summary>
    Task<CommandResult> ConnectAsync(Guid tenantId, Guid staffKey, int actorMemberId, string? organisation, string? accessToken, DateOnly? expiresOn, CancellationToken cancellationToken);

    /// <summary>The picker, or a refusal/NotFound instead.</summary>
    Task<(AzureDevOpsRepositorySelectionViewModel? Model, CommandResult? Problem)> BuildRepositoryChoiceAsync(Guid tenantId, Guid connectionKey, CancellationToken cancellationToken);

    Task<CommandResult> SaveRepositoriesAsync(Guid tenantId, Guid connectionKey, string[]? repositories, int actorMemberId, CancellationToken cancellationToken);
}

public sealed class AzureDevOpsConnectionService(
    IEngineeringEvidenceRepository evidenceRepository,
    IEvidenceCredentialProtector protector,
    IAzureDevOpsEvidenceClient client,
    ISkillsEvidenceAuditLogRepository auditLog,
    TimeProvider timeProvider) : IAzureDevOpsConnectionService
{
    public async Task<CommandResult> ConnectAsync(Guid tenantId, Guid staffKey, int actorMemberId, string? organisation, string? accessToken, DateOnly? expiresOn, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var account = organisation?.Trim().ToLowerInvariant();
        var apiBaseUrl = AzureDevOpsHostPolicy.BaseUrlFor(account);
        if (apiBaseUrl is null)
        {
            return CommandResult.Refused("Enter the Azure DevOps organisation name — the part after dev.azure.com/.");
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return CommandResult.Refused("Enter a personal access token with the Code (Read) scope only.");
        }

        // End of the stated day, so a token that expires "on the 30th" is
        // still usable on the 30th.
        DateTimeOffset? expiresAtUtc = expiresOn is { } day
            ? new DateTimeOffset(day.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero)
            : null;
        if (expiresAtUtc <= now)
        {
            return CommandResult.Refused("That expiry date has already passed. Create a new token.");
        }

        AzureDevOpsVerification verification;
        try
        {
            verification = await client.VerifyOrganisationAsync(apiBaseUrl, accessToken.Trim(), cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            return CommandResult.Refused("Azure DevOps could not be reached. Check the organisation name and try again.");
        }

        switch (verification)
        {
            case AzureDevOpsVerification.OrganisationNotFound:
                return CommandResult.Refused($"Azure DevOps has no organisation '{account}' that this token can see.");
            case AzureDevOpsVerification.TokenRejected:
                return CommandResult.Refused("Azure DevOps refused that token. Check it is current, belongs to this organisation and has Code (Read).");
        }

        var existing = await evidenceRepository.GetConnectionByAccountAsync(
            AzureDevOpsEvidenceMapper.ProviderName, account!, tenantId);

        var connection = await evidenceRepository.UpsertConnectionAsync(new EvidenceConnection
        {
            ConnectionKey = existing?.ConnectionKey ?? Guid.NewGuid(),
            TenantId = tenantId,
            Provider = AzureDevOpsEvidenceMapper.ProviderName,
            SourceAccountId = account!,
            DisplayName = $"dev.azure.com/{account}",
            ApiBaseUrl = apiBaseUrl,

            // A replaced token keeps the existing selection; the next visit
            // to the picker re-checks it against what the new token reads.
            SelectedRepositories = existing?.SelectedRepositories ?? [],
            Status = EvidenceConnectionStatus.Active,
            ProtectedCredentialJson = protector.Protect(new EvidenceCredential(accessToken.Trim(), expiresAtUtc)),
            ConnectedByStaffKey = staffKey,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now.UtcDateTime,
            UpdatedAtUtc = now.UtcDateTime
        });

        await LogAsync(connection.ConnectionKey,
            existing is null ? SkillsEvidenceAuditAction.ConnectionCreated : SkillsEvidenceAuditAction.ConnectionUpdated,
            JsonSerializer.Serialize(new { provider = connection.Provider, account, expiresOn }), tenantId, actorMemberId);

        return CommandResult.Succeeded(connectionKey: connection.ConnectionKey);
    }

    public async Task<(AzureDevOpsRepositorySelectionViewModel? Model, CommandResult? Problem)> BuildRepositoryChoiceAsync(Guid tenantId, Guid connectionKey, CancellationToken cancellationToken)
    {
        var (connection, credential, problem) = await LoadUsableAsync(connectionKey, tenantId);
        if (problem is not null)
        {
            return (null, problem);
        }

        IReadOnlyList<AzureDevOpsRepository> accessible;
        try
        {
            accessible = await client.GetAccessibleRepositoriesAsync(connection!.ApiBaseUrl, credential!.AccessToken, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            return (null, CommandResult.Refused("The repository list could not be read from Azure DevOps. Try again shortly."));
        }

        if (accessible.Count == 0)
        {
            return (null, CommandResult.Refused("This token can read no repositories with any commits. Check its scope and the owner's project access."));
        }

        var selected = new HashSet<string>(connection.SelectedRepositories, StringComparer.OrdinalIgnoreCase);
        var options = accessible
            .OrderBy(r => r.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => new AzureDevOpsRepositoryOption(r.Key, r.ProjectName, r.Name, selected.Contains(r.Key)))
            .ToList();

        return (new AzureDevOpsRepositorySelectionViewModel(
            connection.ConnectionKey, connection.SourceAccountId, options, AzureDevOpsHostPolicy.MaxSelectedRepositories), null);
    }

    public async Task<CommandResult> SaveRepositoriesAsync(Guid tenantId, Guid connectionKey, string[]? repositories, int actorMemberId, CancellationToken cancellationToken)
    {
        var (connection, credential, problem) = await LoadUsableAsync(connectionKey, tenantId);
        if (problem is not null)
        {
            return problem;
        }

        // Re-read, never trusted from the form: the posted list is checked
        // against what the token can read at the moment of saving.
        IReadOnlyList<AzureDevOpsRepository> accessible;
        try
        {
            accessible = await client.GetAccessibleRepositoriesAsync(connection!.ApiBaseUrl, credential!.AccessToken, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            return CommandResult.Refused("The repository list could not be read from Azure DevOps. Try again shortly.");
        }

        if (!AzureDevOpsHostPolicy.IsValidSelection(repositories, accessible.Select(r => r.Key).ToList(), out var selection))
        {
            return CommandResult.Refused($"Choose between 1 and {AzureDevOpsHostPolicy.MaxSelectedRepositories} repositories that this token can read.");
        }

        await evidenceRepository.UpsertConnectionAsync(connection with
        {
            SelectedRepositories = selection,
            UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        });

        await LogAsync(connection.ConnectionKey, SkillsEvidenceAuditAction.ConnectionUpdated,
            JsonSerializer.Serialize(new { provider = connection.Provider, repositories = selection.Count }), tenantId, actorMemberId);

        return CommandResult.Succeeded($"Connected to {connection.SourceAccountId} with {selection.Count} repositories. Run a sync to collect evidence.");
    }

    /// <summary>
    /// The connection, if it belongs to this tenant, is Azure DevOps and has
    /// a readable credential. Another tenant's key reads as not found.
    /// </summary>
    private async Task<(EvidenceConnection? Connection, EvidenceCredential? Credential, CommandResult? Problem)> LoadUsableAsync(
        Guid connectionKey, Guid tenantId)
    {
        var connection = await evidenceRepository.GetConnectionAsync(connectionKey, tenantId);
        if (connection is null || !string.Equals(connection.Provider, AzureDevOpsEvidenceMapper.ProviderName, StringComparison.Ordinal))
        {
            return (null, null, CommandResult.NotFound);
        }

        if (connection.Status != EvidenceConnectionStatus.Active)
        {
            return (null, null, CommandResult.Refused("This connection is not active. Reconnect it with a current token first."));
        }

        var credential = protector.Unprotect(connection.ProtectedCredentialJson);
        return credential is null
            ? (null, null, CommandResult.Refused("This connection has no readable credential. Reconnect it with a current token."))
            : (connection, credential, null);
    }

    private Task LogAsync(Guid connectionKey, string action, string? detailJson, Guid tenantId, int? actorMemberId) =>
        auditLog.LogAsync(SkillsEvidenceAuditAction.EntityTypeConnection, connectionKey.ToString(), action,
            actorMemberId, detailJson, timeProvider.GetUtcNow().UtcDateTime, tenantId);
}
