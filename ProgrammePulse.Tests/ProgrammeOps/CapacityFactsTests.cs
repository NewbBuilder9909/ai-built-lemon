using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

public sealed class CapacityFactsTests
{
    [Fact]
    public void PreservesDistinctCapacityPlanEstimateAndActualFacts()
    {
        var result = CapacityFacts.Calculate(37.5m, 7.5m, 35m, 30m, 35m);

        Assert.Equal(30m, result.AvailableHours);
        Assert.Equal(35m, result.PlannedHours);
        Assert.Equal(30m, result.EstimatedHours);
        Assert.Equal(35m, result.ActualHours);
        Assert.Equal(-5m, result.AllocationHeadroomHours);
        Assert.Equal(5m, result.PlanAlignmentHours);
        Assert.Equal(5m, result.EffortVarianceHours);
        Assert.Equal(-5m, result.ActualResidualHours);
    }

    [Fact]
    public void MissingPlanAndEstimateStayUnknown()
    {
        var result = CapacityFacts.Calculate(37.5m, 0m, null, null, 0m);

        Assert.Null(result.AllocationHeadroomHours);
        Assert.Null(result.PlanAlignmentHours);
        Assert.Null(result.EffortVarianceHours);
    }

    [Fact]
    public void RejectsNegativeHours()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CapacityFacts.Calculate(37.5m, 0m, -1m, 20m, 0m));
    }
}
