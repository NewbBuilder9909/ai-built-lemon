using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed record SearchProgrammeHit(Guid ProgrammeKey, string Name, string? CustomerName);

public sealed record SearchCustomerHit(Guid CustomerKey, string Name);

/// <summary>A work item with the path that places it, so a reader can find it in the source tool.</summary>
public sealed record SearchWorkItemHit(
    string Title,
    string Path,
    Guid ProgrammeKey,
    string? Source,
    string? ExternalId,
    WorkItemLifecycleStage Stage,
    DateTime? DueDateUtc,
    string? AssigneeName);

public sealed record SearchPersonHit(Guid StaffKey, string Name, string? JobTitle, bool IsActive);

public sealed record SearchResults(
    string Term,
    IReadOnlyList<SearchProgrammeHit> Programmes,
    IReadOnlyList<SearchCustomerHit> Customers,
    IReadOnlyList<SearchWorkItemHit> WorkItems,
    bool MoreWorkItems,
    IReadOnlyList<SearchPersonHit> People)
{
    public const int MinimumLength = 2;
    public const int MaximumLength = 100;

    public bool TooShort => Term.Length < MinimumLength;

    public bool IsEmpty => Programmes.Count == 0 && Customers.Count == 0 && WorkItems.Count == 0 && People.Count == 0;

    public static SearchResults None(string term) => new(term, [], [], [], false, []);
}

public interface IPortfolioSearchService
{
    /// <param name="includePeople">Whether the caller may see the staff roster (ManageStaff).</param>
    Task<SearchResults> SearchAsync(Guid tenantId, string? term, bool includePeople, CancellationToken cancellationToken = default);
}

/// <summary>
/// One box that finds a programme, customer, work item or person by name
/// or source id, inside the caller's tenant. Work items come from a bounded
/// SQL read (title or source id, at most <see cref="WorkItemLimit"/>);
/// programmes, projects, workstreams and customers are small reference
/// lists. Never reads time entries or cost.
/// </summary>
public sealed class PortfolioSearchService(IProgrammeReadRepository programmes, IStaffRepository staff) : IPortfolioSearchService
{
    public const int WorkItemLimit = 25;

    public async Task<SearchResults> SearchAsync(Guid tenantId, string? term, bool includePeople, CancellationToken cancellationToken = default)
    {
        var text = (term ?? string.Empty).Trim();
        if (text.Length > SearchResults.MaximumLength)
        {
            text = text[..SearchResults.MaximumLength];
        }

        if (text.Length < SearchResults.MinimumLength)
        {
            return SearchResults.None(text);
        }

        bool Matches(string? value) => value?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false;

        var allProgrammes = await programmes.GetProgrammesAsync(tenantId, cancellationToken);
        var customers = await programmes.GetCustomersAsync(tenantId, cancellationToken);
        var customerNames = customers.ToDictionary(c => c.CustomerKey, c => c.Name);

        var programmeHits = allProgrammes
            .Where(p => Matches(p.Name))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new SearchProgrammeHit(p.ProgrammeKey, p.Name, p.CustomerKey is { } c ? customerNames.GetValueOrDefault(c) : null))
            .ToList();

        var customerHits = customers
            .Where(c => Matches(c.Name))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new SearchCustomerHit(c.CustomerKey, c.Name))
            .ToList();

        // One past the limit says whether there are more, without counting.
        var items = await programmes.SearchWorkItemsAsync(tenantId, text, WorkItemLimit + 1, cancellationToken);
        var workItemHits = new List<SearchWorkItemHit>();
        IReadOnlyList<Models.Staff.StaffProfile>? roster = null;
        if (items.Count > 0)
        {
            var workstreams = (await programmes.GetWorkstreamsAsync(tenantId, cancellationToken)).ToDictionary(w => w.WorkstreamKey);
            var projects = (await programmes.GetProjectsAsync(tenantId, cancellationToken)).ToDictionary(p => p.ProjectKey);
            var programmeNames = allProgrammes.ToDictionary(p => p.ProgrammeKey, p => p.Name);
            roster = await staff.GetByTenantAsync(tenantId, cancellationToken);
            var names = roster.ToDictionary(s => s.StaffKey, s => s.FullName);

            foreach (var item in items.Take(WorkItemLimit))
            {
                if (!workstreams.TryGetValue(item.WorkstreamKey, out var workstream)
                    || !projects.TryGetValue(workstream.ProjectKey, out var project))
                {
                    continue;
                }

                var path = $"{programmeNames.GetValueOrDefault(project.ProgrammeKey, "?")} › {project.Name} › {workstream.Name}";
                workItemHits.Add(new SearchWorkItemHit(
                    item.Title, path, project.ProgrammeKey, item.ExternalSource, item.ExternalId, item.Stage, item.DueDateUtc,
                    item.AssignedStaffKey is { } assignee ? names.GetValueOrDefault(assignee) : null));
            }
        }

        var people = new List<SearchPersonHit>();
        if (includePeople)
        {
            roster ??= await staff.GetByTenantAsync(tenantId, cancellationToken);
            people = roster
                .Where(s => Matches(s.FullName) || Matches(s.Email) || Matches(s.JobTitle))
                .OrderBy(s => s.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(s => new SearchPersonHit(s.StaffKey, s.FullName, s.JobTitle, s.IsActive))
                .ToList();
        }

        return new SearchResults(text, programmeHits, customerHits, workItemHits, items.Count > WorkItemLimit, people);
    }
}
