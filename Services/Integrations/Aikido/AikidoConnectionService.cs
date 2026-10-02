using System.Text.Json;
using ProgrammePulse.Models.SecurityAssurance;
using ProgrammePulse.Services.SecurityAssurance;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.Integrations.Aikido;

/// <summary>
/// Connects and disconnects a tenant's Aikido workspace. The credential is
/// verified against Aikido before it is stored, so a mistyped secret or the
/// wrong region is reported at once rather than on the first sync. Stored
/// encrypted under its own purpose, with no deployment-wide fallback.
/// </summary>
public interface IAikidoConnectionService
{
    Task<CommandResult> ConnectAsync(Guid tenantId, Guid staffKey, int actorMemberId, string? region, string? clientId, string? clientSecret, CancellationToken cancellationToken);

    Task<CommandResult> DisconnectAsync(Guid tenantId, int actorMemberId, CancellationToken cancellationToken);
}

public sealed class AikidoConnectionService(
    ISecurityAssuranceRepository repository,
    ISecurityCredentialProtector protector,
    IAikidoClient client,
    TimeProvider timeProvider) : IAikidoConnectionService
{
    public async Task<CommandResult> ConnectAsync(Guid tenantId, Guid staffKey, int actorMemberId, string? region, string? clientId, string? clientSecret, CancellationToken cancellationToken)
    {
        var normalisedRegion = AikidoRegions.Normalise(region);
        if (normalisedRegion is null)
        {
            return CommandResult.Refused("Choose the Aikido region your workspace lives in.");
        }

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || clientId.Trim().Length > 200)
        {
            return CommandResult.Refused("Enter the client id and secret of a read-only Aikido REST API client.");
        }

        var id = clientId.Trim();
        var secret = clientSecret.Trim();
        bool verified;
        try
        {
            verified = await client.VerifyAsync(normalisedRegion, id, secret, cancellationToken);
        }
        catch (AikidoAccessLostException ex)
        {
            return CommandResult.Refused(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return CommandResult.Refused("Aikido could not be reached. Check the region and try again.");
        }

        if (!verified)
        {
            return CommandResult.Refused("Those credentials cannot list repositories. The client needs the repositories:read scope.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var existing = await repository.GetConnectionAsync(tenantId, SecurityTools.Aikido);
        var connection = new SecurityToolConnection
        {
            ConnectionKey = existing?.ConnectionKey ?? Guid.NewGuid(),
            TenantId = tenantId,
            Tool = SecurityTools.Aikido,
            Region = normalisedRegion,
            ClientId = id,
            ProtectedCredentialJson = protector.Protect(new SecurityToolCredential(id, secret)),
            Status = SecurityConnectionStatus.Active,
            ConnectedByStaffKey = staffKey,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now,
        };
        await repository.UpsertConnectionAsync(connection);

        await repository.LogAsync(
            tenantId, SecurityAuditAction.EntityTypeConnection, connection.ConnectionKey.ToString(),
            existing is null ? SecurityAuditAction.ConnectionCreated : SecurityAuditAction.ConnectionUpdated,
            actorMemberId, JsonSerializer.Serialize(new { tool = SecurityTools.Aikido, region = normalisedRegion }), now);

        return CommandResult.Succeeded("Connected to Aikido. Run a sync to read repositories, gate settings and findings.", connection.ConnectionKey);
    }

    public async Task<CommandResult> DisconnectAsync(Guid tenantId, int actorMemberId, CancellationToken cancellationToken)
    {
        var existing = await repository.GetConnectionAsync(tenantId, SecurityTools.Aikido);
        if (existing is null)
        {
            return CommandResult.NotFound;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await repository.SetConnectionStatusAsync(tenantId, SecurityTools.Aikido, SecurityConnectionStatus.Disconnected, clearCredential: true, now);
        await repository.LogAsync(
            tenantId, SecurityAuditAction.EntityTypeConnection, existing.ConnectionKey.ToString(),
            SecurityAuditAction.ConnectionDisconnected, actorMemberId,
            JsonSerializer.Serialize(new { tool = SecurityTools.Aikido }), now);

        return CommandResult.Succeeded("Disconnected. The credential was deleted; contract assurance falls back to attestations.");
    }
}
