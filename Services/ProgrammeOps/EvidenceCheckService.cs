using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>What the person asked to check, straight from the page. Validated by <see cref="IEvidenceCheckService.ResolveScopeAsync"/>.</summary>
public sealed record EvidenceScopeRequest(
    Guid? ProgrammeKey = null,
    Guid? CustomerKey = null,
    DateOnly? From = null,
    DateOnly? To = null,
    DateOnly? ExtractedOn = null,
    string? Decision = null);

/// <summary>A valid scope, or why the request isn't one. <see cref="Picker"/> fills the programme and customer lists either way.</summary>
public sealed record EvidenceScopeResolution(EvidenceScope? Scope, string? Error, PortfolioScopeViewModel Picker);

public interface IEvidenceCheckService
{
    /// <summary>
    /// Validates a scope against the tenant: the programme and customer must be
    /// this tenant's and agree with each other, the period must be a real
    /// period (at most <see cref="ReportingPeriod.MaxDays"/> days), and an
    /// extract date can't be in the future.
    /// </summary>
    Task<EvidenceScopeResolution> ResolveScopeAsync(Guid tenantId, EvidenceScopeRequest request, CancellationToken cancellationToken = default);

    Task<EvidenceCheckReport> BuildAsync(Guid tenantId, EvidenceScope scope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads one scope's Silver data and source run state for
/// <see cref="EvidenceCheckCalculator"/>: the work items of the scope's
/// programmes, their time within the scope's period (the period read, never
/// the whole history), and all time ever recorded against them as one SQL
/// aggregate for estimate accuracy. Time linked to no work item belongs to
/// no programme, so a programme scope leaves it out and says how much.
///
/// Reachable by delivery-reporting roles below Admin, so, following
/// ProgrammeOverviewQueryService, it never touches StaffRate or any cost
/// figure: the check is about evidence, and hours are the only effort unit it reads.
/// </summary>
public sealed class EvidenceCheckService(
    IProgrammeReadRepository programmes,
    IIdentityResolutionRepository identities,
    ISyncStatusQueryService syncStatus,
    TimeProvider clock) : IEvidenceCheckService
{
    public async Task<EvidenceScopeResolution> ResolveScopeAsync(Guid tenantId, EvidenceScopeRequest request, CancellationToken cancellationToken = default)
    {
        var picker = PortfolioScope.Resolve(
            await programmes.GetProgrammesAsync(tenantId, cancellationToken),
            await programmes.GetCustomersAsync(tenantId, cancellationToken),
            request.ProgrammeKey, request.CustomerKey);
        if (!picker.IsValid)
            return new(null, "That programme or customer isn't in this organisation, or the programme doesn't belong to that customer.", picker);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (!ReportingPeriod.TryResolve(request.From, request.To, today, EvidenceScope.DefaultPeriodDays - 1, today, out var from, out var to, out var periodError))
            return new(null, periodError, picker);
        if (request.ExtractedOn is { } extracted && extracted > today)
            return new(null, "The extract date is in the future. Enter the date the data was exported.", picker);
        var decision = string.IsNullOrWhiteSpace(request.Decision) ? null : request.Decision.Trim();
        if (decision is { Length: > EvidenceScope.MaxDecisionLength })
            return new(null, $"The decision is longer than {EvidenceScope.MaxDecisionLength} characters.", picker);

        return new(new EvidenceScope(request.ProgrammeKey, request.CustomerKey, picker.Label, from, to, request.ExtractedOn, decision), null, picker);
    }

    public async Task<EvidenceCheckReport> BuildAsync(Guid tenantId, EvidenceScope scope, CancellationToken cancellationToken = default)
    {
        var allProgrammes = await programmes.GetProgrammesAsync(tenantId, cancellationToken);
        var projects = await programmes.GetProjectsAsync(tenantId, cancellationToken);
        var workstreams = await programmes.GetWorkstreamsAsync(tenantId, cancellationToken);
        var workItems = await programmes.GetWorkItemsAsync(tenantId, cancellationToken);

        if (!scope.IsWholeOrganisation)
        {
            var picker = new PortfolioScopeViewModel(scope.ProgrammeKey, scope.CustomerKey, scope.Label, true, [], []);
            var inScopeProgrammes = allProgrammes.Where(p => PortfolioScope.Includes(picker, p)).Select(p => p.ProgrammeKey).ToHashSet();
            var inScopeProjects = projects.Where(p => inScopeProgrammes.Contains(p.ProgrammeKey)).Select(p => p.ProjectKey).ToHashSet();
            var inScopeWorkstreams = workstreams.Where(w => inScopeProjects.Contains(w.ProjectKey)).Select(w => w.WorkstreamKey).ToHashSet();
            workItems = workItems.Where(i => inScopeWorkstreams.Contains(i.WorkstreamKey)).ToList();
        }

        var itemKeys = workItems.Select(i => i.WorkItemKey).ToHashSet();
        var periodTime = await programmes.GetTimeEntriesAsync(tenantId, scope.PeriodFrom, scope.PeriodTo, cancellationToken);
        var inScopeTime = periodTime
            .Where(t => t.WorkItemKey is { } key ? itemKeys.Contains(key) : scope.IsWholeOrganisation)
            .ToList();
        // Linked to an item outside the scope is simply another programme's time;
        // linked to nothing can't be placed anywhere, so it's stated.
        var hoursOutsideScope = scope.IsWholeOrganisation ? 0m
            : periodTime.Where(t => t.WorkItemKey is null).Sum(t => t.DurationHours);
        var recorded = (await programmes.GetLoggedHoursByWorkItemAsync(tenantId, cancellationToken: cancellationToken))
            .Where(kv => itemKeys.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var unresolved = await identities.GetUnresolvedAsync(tenantId);
        var sources = await syncStatus.GetPublicationStatesAsync(tenantId);

        // A source that has never run for this tenant isn't "connected" —
        // only sources with some run history can be failed or stale.
        var runState = sources
            .Where(s => s.LastPublishedAtUtc is not null || s.LastRunFailed || s.IsRunning)
            .Select(s => new SourceRunState(s.DisplayName, s.LastPublishedAtUtc, s.LastRunFailed, s.IsRunning))
            .ToList();

        return EvidenceCheckCalculator.Evaluate(
            new EvidenceCheckInput(projects, workstreams, workItems, inScopeTime, unresolved.Count(u => !u.IsResolved), runState,
                scope, recorded, hoursOutsideScope),
            clock.GetUtcNow().UtcDateTime);
    }
}
