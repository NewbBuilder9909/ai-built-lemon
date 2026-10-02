using System.Text.Json;
using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Services.ServiceOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.Integrations.Freshdesk;

/// <summary>
/// Connects a Freshdesk account with an API key. The key is verified
/// against the desk before it is stored, so an admin who mistypes the
/// account or pastes the wrong key is told immediately rather than
/// discovering it on the first sync. Moved verbatim out of
/// StaffServiceController.
/// </summary>
public interface IFreshdeskConnectionService
{
    Task<CommandResult> ConnectAsync(Guid tenantId, Guid staffKey, int actorMemberId, string? account, string? apiToken, string? approvedComponents, CancellationToken cancellationToken);
}

public sealed class FreshdeskConnectionService(
    IServiceOpsRepository serviceOpsRepository,
    IDeskCredentialProtector protector,
    IFreshdeskClient freshdeskClient,
    TimeProvider timeProvider) : IFreshdeskConnectionService
{
    public async Task<CommandResult> ConnectAsync(Guid tenantId, Guid staffKey, int actorMemberId, string? account, string? apiToken, string? approvedComponents, CancellationToken cancellationToken)
    {
        var apiBaseUrl = DeskHostPolicy.CanonicalizeFreshdesk(account);
        var accountId = DeskHostPolicy.AccountIdFor(DeskHostPolicy.FreshdeskProvider, account);
        if (apiBaseUrl is null || accountId is null)
        {
            return CommandResult.Refused("Enter your Freshdesk account, e.g. \"acme\" or \"acme.freshdesk.com\".");
        }

        if (string.IsNullOrWhiteSpace(apiToken))
        {
            return CommandResult.Refused("Enter the Freshdesk API key for a read-only agent.");
        }

        var components = DeskAdminService.ParseComponents(approvedComponents);
        if (components is null)
        {
            return CommandResult.Refused(DeskAdminService.InvalidComponents);
        }

        string? verified;
        try
        {
            verified = await freshdeskClient.VerifyAsync(apiBaseUrl, apiToken.Trim(), cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            return CommandResult.Refused("The desk could not be reached. Check the account and the API key.");
        }

        if (verified is null)
        {
            return CommandResult.Refused("That API key was rejected by the desk, or cannot read tickets.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var existing = await serviceOpsRepository.GetConnectionByAccountAsync(
            DeskHostPolicy.FreshdeskProvider, accountId, tenantId);

        var connection = await serviceOpsRepository.UpsertConnectionAsync(new DeskConnection
        {
            ConnectionKey = existing?.ConnectionKey ?? Guid.NewGuid(),
            TenantId = tenantId,
            Provider = DeskHostPolicy.FreshdeskProvider,
            SourceAccountId = accountId,
            DisplayName = verified,
            ApiBaseUrl = apiBaseUrl,
            ApprovedComponents = components,
            Status = DeskConnectionStatus.Active,
            ProtectedCredentialJson = protector.Protect(new DeskCredential(apiToken.Trim(), accountId)),
            ConnectedByStaffKey = staffKey,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now
        });

        await serviceOpsRepository.LogAsync(
            ServiceOpsAuditAction.EntityTypeConnection, connection.ConnectionKey.ToString(),
            existing is null ? ServiceOpsAuditAction.ConnectionCreated : ServiceOpsAuditAction.ConnectionUpdated,
            actorMemberId,
            JsonSerializer.Serialize(new { account = accountId, components = components.Count }),
            now, tenantId);

        return CommandResult.Succeeded($"Connected to {verified} with {components.Count} approved components. Run a sync to import case metadata.", connection.ConnectionKey);
    }
}
