namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Bronze layer persistence. SaveAsync captures a raw Hub Planner API
/// response verbatim; nothing else in this interface transforms it. Only
/// Services/Integrations/HubPlanner code should depend on this.
/// </summary>
public interface IHubPlannerRawPayloadRepository
{
    Task SaveAsync(string entityType, string externalId, string payloadJson, DateTime fetchedAtUtc, Guid tenantId);

    /// <summary>Retention purge (ProgrammeOpsOptions.RawPayloadRetentionDays), global across tenants; returns rows deleted.</summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc);
}
