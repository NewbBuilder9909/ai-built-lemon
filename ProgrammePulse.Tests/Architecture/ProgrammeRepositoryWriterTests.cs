using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// Phase 2 split IProgrammeRepository into a read half
/// (<see cref="IProgrammeReadRepository"/>) and the full repository. Gold and
/// reporting services take the read half, so a report cannot write Silver
/// rows even by mistake. Taking the writable repository is a decision, made
/// here, with a reason, not something a constructor drifts into.
/// </summary>
public sealed class ProgrammeRepositoryWriterTests
{
    private static readonly Dictionary<string, string> Writers = new(StringComparer.Ordinal)
    {
        ["ClickUpMappingService"] = "Bronze-to-Silver mapping for ClickUp: upserts the programme graph and time.",
        ["HubPlannerMappingService"] = "Bronze-to-Silver mapping for Hub Planner: upserts planned allocations.",
        ["JiraSyncSource"] = "Jira source: upserts work items.",
        ["TempoSyncSource"] = "Tempo source: upserts time entries.",
        ["DeliveryExportImportService"] = "File import source: upserts work items and time entries from an uploaded export.",
        ["AlertDetectionService"] = "Raises alerts after a sync or on demand.",
        ["GovernanceService"] = "Baselines, change requests and stakeholders.",
        ["PortfolioAdminService"] = "Customers, programme assignment and budgets.",
        ["RaidService"] = "Risks and issues.",
        ["ReportingSnapshotService"] = "Captures reporting snapshots.",
    };

    [Fact]
    public void Only_listed_writers_take_the_writable_programme_repository()
    {
        var takers = typeof(IProgrammeRepository).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IProgrammeRepository))))
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unlisted = takers.Where(name => !Writers.ContainsKey(name)).Order().ToList();
        Assert.True(unlisted.Count == 0,
            "Take IProgrammeReadRepository instead, or list the type as a writer with a reason: " + string.Join(", ", unlisted));

        var stale = Writers.Keys.Where(name => !takers.Contains(name)).Order().ToList();
        Assert.True(stale.Count == 0, "No longer takes the writable repository; remove from the list: " + string.Join(", ", stale));
    }

    [Fact]
    public void Query_services_take_only_the_read_half()
    {
        var offenders = typeof(IProgrammeRepository).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("QueryService", StringComparison.Ordinal))
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IProgrammeRepository))))
            .Select(t => t.Name)
            .ToList();

        Assert.True(offenders.Count == 0, "A query service must not be able to write: " + string.Join(", ", offenders));
    }
}
