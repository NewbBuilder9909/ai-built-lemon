namespace ProgrammePulse.Services.ProgrammeOps;

public interface IDeliveryLoadQueryService
{
    /// <summary>
    /// Builds the delivery load view for one tenant. <paramref name="includePeople"/>
    /// follows the EstimateCalibrationService precedent: the aggregate
    /// coverage figures are a team-level fact, while named findings identify
    /// individuals and sit behind a separate capability.
    /// </summary>
    Task<DeliveryLoadReport> BuildAsync(Guid tenantId, bool includePeople, CancellationToken cancellationToken = default);
}
