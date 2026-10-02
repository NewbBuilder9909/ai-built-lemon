using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;

namespace ProgrammePulse.Tests.Integration;

internal static class EvidenceSqlSetup
{
    public static async Task PermitAsync(ProgrammePulseWebApplicationFactory factory, Guid tenant)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IContinuityRepository>().SupersedeProcessingDecisionAsync(new()
        {
            DecisionKey = Guid.NewGuid(), TenantId = tenant,
            LawfulBasis = EvidenceLawfulBasis.LegitimateInterests, WorkerNoticeGiven = true,
            DpiaCompleted = true, Purpose = "Isolated test fixture", DecidedByStaffKey = Guid.NewGuid(),
            DecidedAtUtc = DateTime.UtcNow, ReviewDueOn = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)
        }, DateTime.UtcNow);
    }
}
