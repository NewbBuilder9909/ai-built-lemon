using System.Reflection;
using System.Text.RegularExpressions;
using ProgrammePulse.Services.ContractOps;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// Finding B6 of docs/architecture-review-2026-09-24.md: a report the caller
/// has abandoned (closed tab, timed-out proxy) used to run its SQL to the
/// end. The heavy read path now carries the request's CancellationToken from
/// the page action down to NPoco. These keep that from eroding: a new read
/// without a token, or a repository that accepts one and then drops it, fails
/// the build.
///
/// Scope, stated so it is not mistaken for more: the programme read
/// repository, the batched staff reads, and the report services behind the
/// Reporting Hub, Cost Summary, Programme Overview, Delivery Load, My Work,
/// Alerts and budget/contract costing. Writes and the other feature areas'
/// reads do not take a token yet.
/// </summary>
public partial class CancellationTokenTests
{
    private static readonly Type[] ReportServices =
    [
        typeof(IReportingQueryService),
        typeof(IProgrammeOverviewQueryService),
        typeof(IDeliveryLoadQueryService),
        typeof(IMyWorkQueryService),
        typeof(IProgrammeBudgetService),
        typeof(IContractCommercialService),
        typeof(IAlertDetectionService),
    ];

    private static readonly string[] RepositoryFiles =
    [
        "Services/ProgrammeOps/ProgrammeRepository.cs",
        "Services/Staff/StaffRepository.cs",
        "Services/Staff/StaffRateRepository.cs",
        "Services/Staff/WorkHoursHistoryRepository.cs",
        "Services/Staff/AvailabilityRepository.cs",
    ];

    [Fact]
    public void Every_programme_read_ends_with_a_cancellation_token()
    {
        var offenders = typeof(IProgrammeReadRepository).GetMethods()
            .Where(m => !EndsWithToken(m))
            .Select(m => m.Name)
            .ToList();

        Assert.True(offenders.Count == 0, "Add a trailing CancellationToken to: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_report_read_ends_with_a_cancellation_token()
    {
        var offenders = ReportServices
            .SelectMany(service => service.GetMethods()
                .Where(m => m.Name.StartsWith("Get", StringComparison.Ordinal)
                    || m.Name.StartsWith("Count", StringComparison.Ordinal)
                    || m.Name.StartsWith("Build", StringComparison.Ordinal))
                .Where(m => !EndsWithToken(m))
                .Select(m => $"{service.Name}.{m.Name}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Add a trailing CancellationToken to: " + string.Join(", ", offenders));
    }

    [Fact]
    public void A_repository_method_that_accepts_a_token_passes_it_on()
    {
        var offenders = new List<string>();
        foreach (var relative in RepositoryFiles)
        {
            var code = SourceTree.CodeOf(Path.Combine(SourceTree.Root, relative));
            var members = MemberStart().Matches(code).ToList();
            for (var i = 0; i < members.Count; i++)
            {
                var end = i + 1 < members.Count ? members[i + 1].Index : code.Length;
                var member = code[members[i].Index..end];
                var bodyStart = member.IndexOf("cancellationToken", StringComparison.Ordinal);
                if (!member.Contains("CancellationToken cancellationToken", StringComparison.Ordinal) || bodyStart < 0)
                {
                    continue;
                }

                // The first mention is the parameter itself; the body must use it again.
                if (member.IndexOf("cancellationToken", bodyStart + 1, StringComparison.Ordinal) < 0)
                {
                    offenders.Add($"{Path.GetFileName(relative)}: {members[i].Groups["name"].Value}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Accepts a CancellationToken but never passes it to the database: " + string.Join(", ", offenders));
    }

    private static bool EndsWithToken(MethodInfo method) =>
        method.GetParameters() is { Length: > 0 } parameters && parameters[^1].ParameterType == typeof(CancellationToken);

    [GeneratedRegex(@"^    (?:public|private|internal)[^\n=;{]*?\b(?<name>[A-Z][A-Za-z0-9_]*)\s*(?:<[^>\n]*>)?\s*\(", RegexOptions.Multiline)]
    private static partial Regex MemberStart();
}
