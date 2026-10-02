using ProgrammePulse.Models.ServiceOps;

namespace ProgrammePulse.Services.ServiceOps;

/// <summary>
/// Service demand, recurrence and resolution by component over a stated
/// period.
///
/// Structurally person-free, like the skills coverage view and for the
/// same reason: <c>ViewServiceHealth</c> is a wider grant than the
/// capability needed to see who resolved what, and
/// <see cref="ComponentServiceRow"/> has nowhere to put a person.
/// </summary>
public interface IServiceHealthQueryService
{
    /// <summary>
    /// <paramref name="periodEnd"/> is exclusive. The previous window of
    /// the same length is included for a like-for-like comparison, which
    /// is the only kind worth showing.
    /// </summary>
    Task<ServiceHealthReport> BuildAsync(Guid tenantId, DateOnly periodStart, DateOnly periodEnd);
}

public sealed class ServiceHealthQueryService(IServiceOpsRepository repository) : IServiceHealthQueryService
{
    public async Task<ServiceHealthReport> BuildAsync(Guid tenantId, DateOnly periodStart, DateOnly periodEnd)
    {
        if (periodEnd <= periodStart)
        {
            throw new ServiceOpsValidationException("The reporting period must end after it starts.");
        }

        var length = periodEnd.DayNumber - periodStart.DayNumber;
        var previousStart = periodStart.AddDays(-length);

        var current = await LoadAsync(tenantId, periodStart, periodEnd);
        var previous = await LoadAsync(tenantId, previousStart, periodStart);

        var coverage = (await repository.GetCoverageAsync(tenantId))
            .Where(c => c.Stream == DeskStream.Tickets)
            // Worst across connections: a tenant with two desks is only
            // as well covered as the less well covered one.
            .OrderByDescending(c => c.Status)
            .FirstOrDefault();

        var unmappedAgents = (await repository.GetUnmappedAgentsAsync(tenantId)).Count;

        return new ServiceHealthReport
        {
            Components = current.Rows,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            // Only offered when there is something to compare against;
            // an empty previous window would read as "demand doubled".
            PreviousPeriod = previous.Rows.Count > 0 ? previous.Rows : null,
            Coverage = coverage,
            CasesWithUnmappedComponent = current.UnmappedComponentCases,
            UnmappedAgents = unmappedAgents,
            WithdrawnExcluded = current.Withdrawn,
            IsUnverifiedReplay = coverage?.LastSucceededAtUtc is null
        };
    }

    private sealed record Window(IReadOnlyList<ComponentServiceRow> Rows, int UnmappedComponentCases, int Withdrawn);

    private async Task<Window> LoadAsync(Guid tenantId, DateOnly from, DateOnly to)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var all = await repository.GetCasesCreatedBetweenAsync(tenantId, fromUtc, toUtc);

        // Withdrawn cases are counted once, for disclosure, and then
        // excluded from every figure. A deleted ticket that silently
        // vanished would change last month's numbers retrospectively.
        var withdrawn = all.Count(c => c.IsWithdrawn);
        var cases = all.Where(c => c.CountsTowardsTrends).ToList();

        var links = (await repository.GetLinksAsync(tenantId))
            .ToLookup(l => l.ExternalTicketId, StringComparer.OrdinalIgnoreCase);

        var rows = cases
            .GroupBy(c => c.ComponentKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var resolved = group.Where(c => c.ResolvedAtUtc is not null).ToList();
                var restoreTimes = resolved
                    .Select(c => c.TimeToRestore)
                    .Where(t => t is not null)
                    .Select(t => t!.Value)
                    .ToList();

                var linkedTickets = group.Count(c => links[c.ExternalTicketId].Any());
                var confirmed = group.Count(c => links[c.ExternalTicketId].Any(l => l.IsCausalClaim));

                return new ComponentServiceRow
                {
                    ComponentKey = group.Key,
                    DisplayName = group.Key ?? "(no approved component)",
                    CasesOpened = group.Count(),
                    CasesResolved = resolved.Count,
                    Reopened = group.Count(c => c.IsReopened),
                    HighOrUrgent = group.Count(c => c.Priority >= SupportCasePriority.High),
                    MedianTimeToRestore = Median(restoreTimes),
                    ConfirmedCodeCauses = confirmed,
                    LinkedToSource = linkedTickets
                };
            })
            // Unmapped last: it is a data-quality bucket, not a component,
            // and sorting it among real ones invites reading it as one.
            .OrderBy(r => r.IsUnmappedComponent)
            .ThenByDescending(r => r.CasesOpened)
            .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new Window(rows, cases.Count(c => c.HasUnmappedComponent), withdrawn);
    }

    /// <summary>
    /// Median rather than mean. One three-week case would otherwise move
    /// the figure for everything else, and time-to-restore distributions
    /// have a long tail by nature.
    /// </summary>
    private static TimeSpan? Median(List<TimeSpan> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        var middle = values.Count / 2;

        return values.Count % 2 == 1
            ? values[middle]
            : new TimeSpan((values[middle - 1].Ticks + values[middle].Ticks) / 2);
    }
}
