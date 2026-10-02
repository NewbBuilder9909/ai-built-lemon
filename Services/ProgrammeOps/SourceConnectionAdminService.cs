using System.Text.Json;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// A tenant's own source credentials (ClickUp, Hub Planner, Jira, Tempo):
/// reading whether one is configured, storing a new one, clearing one.
/// Protection, persistence and the audit entry happen together here, so
/// every source and every entry point audits the same way.
///
/// That was not true before this moved out of the controllers. ClickUp and
/// Hub Planner credential changes were audited, but a Jira or Tempo
/// credential stored by the OAuth callback was not.
///
/// The credential itself is never audited or returned to a page, only
/// whether one exists.
/// </summary>
public interface ISourceConnectionAdminService
{
    Task<SourceConnectionRowViewModel> GetRowAsync(Guid tenantId, string source, string displayName);

    /// <summary>A person-readable state for an OAuth connection (stored, expired, unreadable, none).</summary>
    Task<string> DescribeOAuthConnectionAsync(Guid tenantId, string source);

    Task<SourceCredential?> GetCredentialAsync(Guid tenantId, string source);

    Task SaveCredentialAsync(Guid tenantId, string source, SourceCredential credential, int? actorMemberId);

    Task ClearCredentialAsync(Guid tenantId, string source, int? actorMemberId);
}

public sealed class SourceConnectionAdminService(
    ISourceConnectionRepository connections,
    ISourceCredentialProtector protector,
    IAuditLogRepository audit,
    TimeProvider timeProvider) : ISourceConnectionAdminService
{
    public const string AuditEntityType = "SourceConnectionCredential";

    public async Task<SourceConnectionRowViewModel> GetRowAsync(Guid tenantId, string source, string displayName)
    {
        var connection = await connections.GetActiveForTenantAsync(tenantId, source);
        var hasOwnCredential = connection?.ProtectedCredentialJson is not null;
        return new SourceConnectionRowViewModel(displayName, hasOwnCredential, hasOwnCredential ? connection!.ExternalAccountId : null);
    }

    public async Task<string> DescribeOAuthConnectionAsync(Guid tenantId, string source)
    {
        var protectedCredential = (await connections.GetActiveForTenantAsync(tenantId, source))?.ProtectedCredentialJson;
        if (protectedCredential is null) return "No credential stored";
        var credential = protector.Unprotect(protectedCredential);
        if (credential is null) return "Stored credential cannot be read; reconnect required";
        if (credential.ExpiresAtUtc is { } expiry && expiry <= timeProvider.GetUtcNow()
            && string.IsNullOrWhiteSpace(credential.RefreshToken))
            return "Authorization expired; reconnect required";
        return "OAuth credential stored; source access has not been verified here";
    }

    public async Task<SourceCredential?> GetCredentialAsync(Guid tenantId, string source) =>
        protector.Unprotect((await connections.GetActiveForTenantAsync(tenantId, source))?.ProtectedCredentialJson);

    public async Task SaveCredentialAsync(Guid tenantId, string source, SourceCredential credential, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await connections.SetCredentialAsync(tenantId, source, protector.Protect(credential), now);
        await audit.LogAsync(AuditEntityType, source, "SourceConnectionCredentialSet", actorMemberId, JsonSerializer.Serialize(new { source }), now, tenantId);
    }

    public async Task ClearCredentialAsync(Guid tenantId, string source, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await connections.SetCredentialAsync(tenantId, source, protectedCredentialJson: null, now);
        await audit.LogAsync(AuditEntityType, source, "SourceConnectionCredentialCleared", actorMemberId, JsonSerializer.Serialize(new { source }), now, tenantId);
    }
}
