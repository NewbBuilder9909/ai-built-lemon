namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Arithmetic shared by live reporting and deterministic scenario evaluation.
/// Null planned/estimated hours mean unavailable evidence, not zero demand.
/// The caller is responsible for selecting one tenant and one time window.
/// </summary>
public static class CapacityFacts
{
    public static CapacityResult Calculate(
        decimal contractualHours,
        decimal absenceHours,
        decimal? plannedHours,
        decimal? estimatedHours,
        decimal actualHours)
    {
        if (contractualHours < 0 || absenceHours < 0 || actualHours < 0
            || plannedHours < 0 || estimatedHours < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contractualHours), "Hours cannot be negative.");
        }

        var available = Math.Max(0m, contractualHours - absenceHours);
        return new CapacityResult(
            contractualHours,
            absenceHours,
            available,
            plannedHours,
            estimatedHours,
            actualHours,
            plannedHours is null ? null : available - plannedHours.Value,
            plannedHours is null || estimatedHours is null ? null : plannedHours.Value - estimatedHours.Value,
            estimatedHours is null ? null : actualHours - estimatedHours.Value,
            available - actualHours);
    }
}

public sealed record CapacityResult(
    decimal ContractualHours,
    decimal AbsenceHours,
    decimal AvailableHours,
    decimal? PlannedHours,
    decimal? EstimatedHours,
    decimal ActualHours,
    decimal? AllocationHeadroomHours,
    decimal? PlanAlignmentHours,
    decimal? EffortVarianceHours,
    decimal ActualResidualHours);
