namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Bronze layer persistence. SaveAsync captures a raw ClickUp API response
/// verbatim; nothing else in this interface transforms it. Only
/// Services/Integrations/ClickUp code should depend on this.
/// </summary>
public interface IClickUpRawPayloadRepository
{
    Task SaveAsync(string entityType, string externalId, string workspaceId, string payloadJson, DateTime fetchedAtUtc, Guid tenantId);

    /// <summary>
    /// Captures many payloads of one entity type verbatim, in one batched
    /// write rather than one round trip each. The default saves one at a time.
    /// </summary>
    async Task SaveManyAsync(string entityType, IReadOnlyList<(string ExternalId, string PayloadJson)> payloads, string workspaceId, DateTime fetchedAtUtc, Guid tenantId)
    {
        foreach (var (externalId, payloadJson) in payloads)
        {
            await SaveAsync(entityType, externalId, workspaceId, payloadJson, fetchedAtUtc, tenantId);
        }
    }

    /// <summary>Retention purge (ProgrammeOpsOptions.RawPayloadRetentionDays), global across tenants; returns rows deleted.</summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc);
}
