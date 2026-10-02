using Microsoft.Extensions.Diagnostics.HealthChecks;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Startup;

/// <summary>
/// Readiness probe dependency: can the app open a scope against
/// umbracoDbDSN and run a trivial query. Mapped at /health/ready (tag
/// "ready") — the liveness probe at /health deliberately has no
/// dependencies so a database outage restarts nothing, it only takes the
/// instance out of rotation.
/// </summary>
public sealed class DatabaseHealthCheck(IScopeProvider scopeProvider) : IHealthCheck
{
    public const string Name = "database";
    public const string ReadyTag = "ready";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeProvider.CreateScope(autoComplete: true);
            await scope.Database.ExecuteScalarAsync<int>("SELECT 1");
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database unreachable.", ex);
        }
    }
}
