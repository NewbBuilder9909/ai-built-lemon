using System.Text.Json;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.Experience;

public sealed class ProfessionalCapacityCasesTests
{
    private sealed record Case(
        string Name,
        decimal ContractualHours,
        decimal AbsenceHours,
        decimal? PlannedHours,
        decimal? EstimatedHours,
        decimal ActualHours,
        decimal AvailableHours,
        decimal? AllocationHeadroomHours,
        decimal? PlanAlignmentHours,
        decimal? EffortVarianceHours,
        decimal ActualResidualHours);

    [Fact]
    public void Professional_decision_cases_match_the_reviewed_results()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Experience", "professional-capacity-cases.json");
        var cases = JsonSerializer.Deserialize<Case[]>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(cases);
        Assert.Equal(6, cases.Length);
        foreach (var item in cases)
        {
            var result = CapacityFacts.Calculate(
                item.ContractualHours, item.AbsenceHours, item.PlannedHours, item.EstimatedHours, item.ActualHours);

            Assert.True(result.AvailableHours == item.AvailableHours, $"{item.Name}: available hours");
            Assert.True(result.PlannedHours == item.PlannedHours, $"{item.Name}: planned hours");
            Assert.True(result.EstimatedHours == item.EstimatedHours, $"{item.Name}: estimated hours");
            Assert.True(result.ActualHours == item.ActualHours, $"{item.Name}: actual hours");
            Assert.True(result.AllocationHeadroomHours == item.AllocationHeadroomHours, $"{item.Name}: allocation headroom");
            Assert.True(result.PlanAlignmentHours == item.PlanAlignmentHours, $"{item.Name}: plan alignment");
            Assert.True(result.EffortVarianceHours == item.EffortVarianceHours, $"{item.Name}: effort variance");
            Assert.True(result.ActualResidualHours == item.ActualResidualHours, $"{item.Name}: actual residual");
        }
    }
}
