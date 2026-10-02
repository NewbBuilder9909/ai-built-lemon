namespace ProgrammePulse.Services.Tenancy;

public interface ITenantFeatureSelectionRepository
{
    Task<IReadOnlySet<string>> GetSelectedFeaturesAsync(Guid tenantId);

    Task ReplaceAsync(Guid tenantId, IEnumerable<string> featureKeys, DateTime nowUtc);
}
