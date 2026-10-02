namespace ProgrammePulse.Services.ProgrammeOps;

public interface IRawConnectorPayloadRepository
{
    Task SaveAsync(Guid tenantId, string source, string sourceAccountId, string entityType,
        string externalId, string payloadJson, DateTime fetchedAtUtc);
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc);
}
