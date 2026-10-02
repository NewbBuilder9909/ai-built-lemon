using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Programme;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class ProgrammeRepository(IScopeProvider scopeProvider) : IProgrammeRepository
{
    public async Task<IReadOnlyList<Programme>> GetProgrammesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ProgrammeDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<Project>> GetProjectsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ProjectDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<Workstream>> GetWorkstreamsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<WorkstreamDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<WorkItemDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<Dependency>> GetDependenciesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<DependencyDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<Risk>> GetRisksAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<RiskDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<Issue>> GetIssuesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<IssueDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<TimeEntry>> GetTimeEntriesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<TimeEntryDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    // workDate is persisted as the entry's ReportDate on every write (see
    // UpsertTimeEntryAsync, and AddTenantReadIndexes' backfill), so the period
    // is a range seek on IX_ProgrammeOps_TimeEntry_tenant_workDate.
    public async Task<IReadOnlyList<TimeEntry>> GetTimeEntriesAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<TimeEntryDto>(Sql.Builder.Where(
            "tenantId = @0 AND workDate >= @1 AND workDate <= @2",
            tenantId, from.ToDateTime(TimeOnly.MinValue), to.ToDateTime(TimeOnly.MinValue)), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetLoggedHoursByWorkItemAsync(Guid tenantId, Guid? staffKey = null, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var rows = staffKey is { } person
            ? await scope.Database.FetchAsync<LoggedHoursRow>(
                $"SELECT workItemKey AS WorkItemKey, SUM(durationHours) AS Hours FROM {TimeEntryDto.TableName} WHERE tenantId = @0 AND staffKey = @1 AND workItemKey IS NOT NULL GROUP BY workItemKey",
                new object[] { tenantId, person }, cancellationToken)
            : await scope.Database.FetchAsync<LoggedHoursRow>(
                $"SELECT workItemKey AS WorkItemKey, SUM(durationHours) AS Hours FROM {TimeEntryDto.TableName} WHERE tenantId = @0 AND workItemKey IS NOT NULL GROUP BY workItemKey",
                new object[] { tenantId }, cancellationToken);
        return rows.ToDictionary(row => row.WorkItemKey, row => row.Hours);
    }

    // 1900-01-01 was a Monday, so days-since-then mod 7 is the offset back to
    // the ISO week's Monday whatever the session's DATEFIRST is.
    private const string IsoWeekStartSql = "DATEADD(day, -(DATEDIFF(day, '19000101', t.workDate) % 7), t.workDate)";

    public async Task<IReadOnlyList<WeeklyProjectCount>> GetWeeklyProjectCountsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var rows = await scope.Database.FetchAsync<WeeklyProjectCountRow>(
            $"""
            SELECT t.staffKey AS StaffKey, {IsoWeekStartSql} AS WeekStart, COUNT(DISTINCT ws.projectKey) AS DistinctProjects
            FROM {TimeEntryDto.TableName} t
            LEFT JOIN {WorkItemDto.TableName} wi ON wi.workItemKey = t.workItemKey AND wi.tenantId = @0
            LEFT JOIN {WorkstreamDto.TableName} ws ON ws.workstreamKey = wi.workstreamKey AND ws.tenantId = @0
            WHERE t.tenantId = @0 AND t.workDate IS NOT NULL AND t.staffKey IS NOT NULL
            GROUP BY t.staffKey, {IsoWeekStartSql}
            """,
            new object[] { tenantId }, cancellationToken);
        return rows.Select(row => new WeeklyProjectCount(row.StaffKey, DateOnly.FromDateTime(row.WeekStart), row.DistinctProjects)).ToList();
    }

    private sealed class WeeklyProjectCountRow
    {
        public Guid StaffKey { get; set; }

        public DateTime WeekStart { get; set; }

        public int DistinctProjects { get; set; }
    }

    public async Task<IReadOnlyList<string>> GetDatedTimeSourcesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return await scope.Database.FetchAsync<string>(
            $"SELECT DISTINCT externalSource FROM {TimeEntryDto.TableName} WHERE tenantId = @0 AND workDate IS NOT NULL AND staffKey IS NOT NULL AND LTRIM(RTRIM(externalSource)) <> ''",
            new object[] { tenantId }, cancellationToken);
    }

    public async Task<WorkItem?> GetWorkItemByKeyAsync(Guid workItemKey, Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<WorkItemDto>(
            Sql.Builder.Where("workItemKey = @0 AND tenantId = @1", workItemKey, tenantId), cancellationToken);
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyList<WorkItem>> SearchWorkItemsAsync(Guid tenantId, string term, int take, CancellationToken cancellationToken = default)
    {
        var pattern = "%" + SqlLike.Escape(term) + "%";
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<WorkItemDto>(
            $"SELECT TOP (@2) * FROM {WorkItemDto.TableName} WHERE tenantId = @0"
            + " AND (title LIKE @1 ESCAPE '\\' OR externalId LIKE @1 ESCAPE '\\')"
            + " ORDER BY title, workItemKey",
            new object[] { tenantId, pattern, take }, cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<Risk?> GetRiskByKeyAsync(Guid riskKey, Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<RiskDto>(
            Sql.Builder.Where("riskKey = @0 AND tenantId = @1", riskKey, tenantId), cancellationToken);
        return dto is null ? null : Map(dto);
    }

    public async Task<Issue?> GetIssueByKeyAsync(Guid issueKey, Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<IssueDto>(
            Sql.Builder.Where("issueKey = @0 AND tenantId = @1", issueKey, tenantId), cancellationToken);
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyList<TimeEntry>> GetTimeEntriesForWorkItemAsync(Guid workItemKey, Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<TimeEntryDto>(
            Sql.Builder.Where("tenantId = @0 AND workItemKey = @1", tenantId, workItemKey), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    private sealed class LoggedHoursRow
    {
        public Guid WorkItemKey { get; set; }

        public decimal Hours { get; set; }
    }

    public async Task<TimeEntryCoverage> GetTimeEntryCoverageAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var undatedHours = await scope.Database.ExecuteScalarAsync<decimal?>(
            $"SELECT SUM(durationHours) FROM {TimeEntryDto.TableName} WHERE tenantId = @0 AND workDate IS NULL", new object[] { tenantId }, cancellationToken);
        var hasTempo = await scope.Database.ExecuteScalarAsync<int>(
            $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {TimeEntryDto.TableName} WHERE tenantId = @0 AND externalSource = @1) THEN 1 ELSE 0 END",
            new object[] { tenantId, "Tempo" }, cancellationToken);
        return new TimeEntryCoverage(undatedHours ?? 0m, hasTempo == 1);
    }

    public async Task<SourceDataPresence> GetSourcePresenceAsync(Guid tenantId, string source, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var flags = await scope.Database.ExecuteScalarAsync<int>(
            $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {WorkItemDto.TableName} WHERE tenantId = @0 AND externalSource = @1) THEN 1 ELSE 0 END"
            + $" + CASE WHEN EXISTS (SELECT 1 FROM {TimeEntryDto.TableName} WHERE tenantId = @0 AND externalSource = @1) THEN 2 ELSE 0 END",
            new object[] { tenantId, source }, cancellationToken);
        return new SourceDataPresence((flags & 1) != 0, (flags & 2) != 0);
    }

    public async Task<IReadOnlyList<Customer>> GetCustomersAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<CustomerDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<WorkstreamBaseline>> GetWorkstreamBaselinesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<WorkstreamBaselineDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ChangeRequest>> GetChangeRequestsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ChangeRequestDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ProgrammeStakeholder>> GetStakeholdersAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ProgrammeStakeholderDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ReportingSnapshot>> GetReportingSnapshotsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ReportingSnapshotDto>(
            Sql.Builder.Where("tenantId = @0", tenantId).OrderBy("capturedAtUtc"), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<Alert>> GetOpenAlertsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<AlertDto>(
            Sql.Builder.Where("tenantId = @0 AND acknowledgedAtUtc IS NULL", tenantId).OrderBy("raisedAtUtc DESC"), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<ResultPage<Alert>> GetOpenAlertsPageAsync(Guid tenantId, PageRequest page, IReadOnlyCollection<Guid>? visibleStaffKeys = null, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<AlertDto>(
            VisibleAlerts(tenantId, visibleStaffKeys).OrderBy("raisedAtUtc DESC", "id DESC").ForPage(page), cancellationToken);
        return ResultPage<Alert>.From(dtos.Select(Map).ToList(), page);
    }

    public async Task<int> CountOpenAlertsAsync(Guid tenantId, IReadOnlyCollection<Guid>? visibleStaffKeys = null, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var query = new Sql($"SELECT COUNT(*) FROM {AlertDto.TableName}").Append(VisibleAlerts(tenantId, visibleStaffKeys));
        return await scope.Database.ExecuteScalarAsync<int>(query.SQL, query.Arguments, cancellationToken);
    }

    /// <summary>
    /// Open alerts a caller may see. Null <paramref name="visibleStaffKeys"/> is
    /// tenant-wide (an Admin); otherwise only blocked-workstream alerts and
    /// alerts about the given people (a Team Lead's team). One statement, so
    /// the tenant predicate always travels with the visibility rule.
    /// </summary>
    private static Sql VisibleAlerts(Guid tenantId, IReadOnlyCollection<Guid>? visibleStaffKeys)
    {
        var blocked = AlertType.WorkstreamBlocked.ToString();
        return visibleStaffKeys switch
        {
            null => Sql.Builder.Where("tenantId = @0 AND acknowledgedAtUtc IS NULL", tenantId),
            { Count: 0 } => Sql.Builder.Where("tenantId = @0 AND acknowledgedAtUtc IS NULL AND [type] = @1", tenantId, blocked),
            _ => Sql.Builder.Where("tenantId = @0 AND acknowledgedAtUtc IS NULL AND ([type] = @1 OR entityKey IN (@2))", tenantId, blocked, visibleStaffKeys),
        };
    }

    public async Task<WorkItem?> GetWorkItemByExternalIdAsync(string externalSource, string externalId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await FindByExternalIdAsync<WorkItemDto>(scope.Database, externalSource, externalId, tenantId, cancellationToken);
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyList<PlannedAllocation>> GetPlannedAllocationsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<PlannedAllocationDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<PlannedAllocation> UpsertPlannedAllocationAsync(PlannedAllocation allocation, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await EnsureBelongsToTenantAsync<ProjectDto>(scope.Database, "projectKey", allocation.ProjectKey, d => d.TenantId, tenantId, "Project");
        var existing = await FindByExternalIdAsync<PlannedAllocationDto>(scope.Database, allocation.ExternalSource, allocation.ExternalId, tenantId);

        var dto = existing ?? new PlannedAllocationDto { PlannedAllocationKey = Guid.NewGuid(), CreatedAtUtc = allocation.CreatedAtUtc };
        dto.TenantId = tenantId;
        dto.ProjectKey = allocation.ProjectKey;
        dto.StaffKey = allocation.StaffKey;
        dto.Title = allocation.Title;
        dto.StartUtc = allocation.StartUtc;
        dto.EndUtc = allocation.EndUtc;
        dto.AllocatedHours = allocation.AllocatedHours;
        dto.AllocationPercent = allocation.AllocationPercent;
        dto.RawType = allocation.RawType;
        dto.ExternalSource = allocation.ExternalSource;
        dto.ExternalId = allocation.ExternalId;
        dto.ExternalResourceId = allocation.ExternalResourceId;
        dto.UpdatedAtUtc = allocation.UpdatedAtUtc;

        await SaveAsync(scope.Database, dto, existing is not null);
        scope.Complete();

        return Map(dto);
    }

    private static PlannedAllocation Map(PlannedAllocationDto dto) => new()
    {
        PlannedAllocationKey = dto.PlannedAllocationKey,
        TenantId = dto.TenantId,
        ProjectKey = dto.ProjectKey,
        StaffKey = dto.StaffKey,
        Title = dto.Title,
        StartUtc = dto.StartUtc,
        EndUtc = dto.EndUtc,
        AllocatedHours = dto.AllocatedHours,
        AllocationPercent = dto.AllocationPercent,
        RawType = dto.RawType,
        ExternalSource = dto.ExternalSource,
        ExternalId = dto.ExternalId,
        ExternalResourceId = dto.ExternalResourceId,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    public async Task<IReadOnlyList<WorkItemAllocation>> GetAllocationsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<WorkItemAllocationDto>(Sql.Builder.Where("tenantId = @0", tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<WorkItemAllocation>> GetAllocationsByWorkItemKeyAsync(Guid workItemKey, Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<WorkItemAllocationDto>(
            Sql.Builder.Where("workItemKey = @0 AND tenantId = @1", workItemKey, tenantId), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<Programme> UpsertProgrammeAsync(Programme programme, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var existing = await FindByExternalIdAsync<ProgrammeDto>(scope.Database, programme.ExternalSource, programme.ExternalId, tenantId);

        var customerKey = existing?.CustomerKey ?? programme.CustomerKey;
        if (customerKey is Guid newCustomerKey && customerKey != existing?.CustomerKey)
        {
            await EnsureBelongsToTenantAsync<CustomerDto>(scope.Database, "customerKey", newCustomerKey, d => d.TenantId, tenantId, "Customer");
        }

        var dto = existing ?? new ProgrammeDto { ProgrammeKey = Guid.NewGuid(), CreatedAtUtc = programme.CreatedAtUtc };
        dto.TenantId = tenantId;
        dto.Name = programme.Name;
        dto.Description = programme.Description;
        dto.CustomerKey = customerKey;
        dto.ExternalSource = programme.ExternalSource;
        dto.ExternalId = programme.ExternalId;
        dto.UpdatedAtUtc = programme.UpdatedAtUtc;

        await SaveAsync(scope.Database, dto, existing is not null);
        scope.Complete();

        return Map(dto);
    }

    public async Task<Project> UpsertProjectAsync(Project project, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await EnsureBelongsToTenantAsync<ProgrammeDto>(scope.Database, "programmeKey", project.ProgrammeKey, d => d.TenantId, tenantId, "Programme");
        var existing = await FindByExternalIdAsync<ProjectDto>(scope.Database, project.ExternalSource, project.ExternalId, tenantId);

        var dto = existing ?? new ProjectDto { ProjectKey = Guid.NewGuid(), CreatedAtUtc = project.CreatedAtUtc };
        dto.TenantId = tenantId;
        dto.ProgrammeKey = project.ProgrammeKey;
        dto.Name = project.Name;
        dto.Description = project.Description;
        dto.ExternalSource = project.ExternalSource;
        dto.ExternalId = project.ExternalId;
        dto.UpdatedAtUtc = project.UpdatedAtUtc;

        await SaveAsync(scope.Database, dto, existing is not null);
        scope.Complete();

        return Map(dto);
    }

    public async Task<Workstream> UpsertWorkstreamAsync(Workstream workstream, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await EnsureBelongsToTenantAsync<ProjectDto>(scope.Database, "projectKey", workstream.ProjectKey, d => d.TenantId, tenantId, "Project");
        var existing = await FindByExternalIdAsync<WorkstreamDto>(scope.Database, workstream.ExternalSource, workstream.ExternalId, tenantId);

        var dto = existing ?? new WorkstreamDto { WorkstreamKey = Guid.NewGuid(), CreatedAtUtc = workstream.CreatedAtUtc };
        dto.TenantId = tenantId;
        dto.ProjectKey = workstream.ProjectKey;
        dto.Name = workstream.Name;
        dto.ExternalSource = workstream.ExternalSource;
        dto.ExternalId = workstream.ExternalId;
        dto.UpdatedAtUtc = workstream.UpdatedAtUtc;

        await SaveAsync(scope.Database, dto, existing is not null);
        scope.Complete();

        return Map(dto);
    }

    public async Task<WorkItem> UpsertWorkItemAsync(WorkItem workItem, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await EnsureBelongsToTenantAsync<WorkstreamDto>(scope.Database, "workstreamKey", workItem.WorkstreamKey, d => d.TenantId, tenantId, "Workstream");
        var existing = await FindByExternalIdAsync<WorkItemDto>(scope.Database, workItem.ExternalSource, workItem.ExternalId, tenantId);

        var dto = existing ?? new WorkItemDto { WorkItemKey = Guid.NewGuid(), CreatedAtUtc = workItem.CreatedAtUtc };
        Apply(dto, workItem, tenantId);

        await SaveAsync(scope.Database, dto, existing is not null);
        scope.Complete();

        return Map(dto);
    }

    // The one definition of what an upsert writes to a work item row, shared
    // by the single-row and batched paths so they cannot drift apart.
    private static void Apply(WorkItemDto dto, WorkItem workItem, Guid tenantId)
    {
        dto.TenantId = tenantId;
        dto.WorkstreamKey = workItem.WorkstreamKey;
        dto.Title = workItem.Title;
        dto.Stage = workItem.Stage.ToString();
        dto.RawStatus = workItem.RawStatus;
        dto.IsMilestone = workItem.IsMilestone;
        dto.AssignedStaffKey = workItem.AssignedStaffKey;
        dto.DueDateUtc = workItem.DueDateUtc;
        dto.EstimatedHours = workItem.EstimatedHours;
        dto.ExternalSource = workItem.ExternalSource;
        dto.ExternalId = workItem.ExternalId;
        dto.ParentExternalId = workItem.ParentExternalId;
        dto.UpdatedAtUtc = workItem.UpdatedAtUtc;
    }

    private static readonly string[] WorkItemUpdateColumns =
    [
        "workstreamKey", "title", "stage", "rawStatus", "isMilestone", "assignedStaffKey", "dueDateUtc",
        "estimatedHours", "externalSource", "externalId", "parentExternalId", "updatedAtUtc"
    ];

    private static readonly string[] WorkItemInsertColumns = ["workItemKey", "tenantId", .. WorkItemUpdateColumns, "createdAtUtc"];

    private static object?[] UpdateValues(WorkItemDto d) =>
    [
        d.WorkstreamKey, d.Title, d.Stage, d.RawStatus, d.IsMilestone, d.AssignedStaffKey, d.DueDateUtc,
        d.EstimatedHours, d.ExternalSource, d.ExternalId, d.ParentExternalId, d.UpdatedAtUtc
    ];

    public async Task<IReadOnlyList<WorkItem>> UpsertWorkItemsAsync(IReadOnlyList<WorkItemUpsert> items, DateTime nowUtc, Guid tenantId)
    {
        if (items.Count == 0)
        {
            return [];
        }

        using var scope = scopeProvider.CreateScope();
        var database = scope.Database;

        await EnsureAllBelongToTenantAsync(database, WorkstreamDto.TableName, "workstreamKey", items.Select(i => i.Item.WorkstreamKey), tenantId, "Workstream");
        var existing = await FindByExternalIdsAsync<WorkItemDto>(database, items.Select(i => (i.Item.ExternalSource, i.Item.ExternalId)), d => d.ExternalSource, d => d.ExternalId, tenantId);

        var plan = new BatchPlan<WorkItemDto>(existing);
        var saved = new List<WorkItem>(items.Count);
        var allocations = new Dictionary<Guid, IReadOnlyList<Guid>>();
        foreach (var upsert in items)
        {
            var item = upsert.Item;
            var dto = plan.Resolve(item.ExternalSource, item.ExternalId,
                () => new WorkItemDto { WorkItemKey = Guid.NewGuid(), CreatedAtUtc = item.CreatedAtUtc });
            Apply(dto, item, tenantId);
            saved.Add(Map(dto));
            allocations[dto.WorkItemKey] = upsert.AllocatedStaffKeys;
        }

        await SqlMultiRow.UpdateAsync(database, WorkItemDto.TableName, "workItemKey", WorkItemUpdateColumns,
            plan.Updates.Select(d => (object?[])[d.WorkItemKey, .. UpdateValues(d)]), tenantId);
        await SqlMultiRow.InsertAsync(database, WorkItemDto.TableName, WorkItemInsertColumns,
            plan.Inserts.Select(d => (object?[])[d.WorkItemKey, d.TenantId, .. UpdateValues(d), d.CreatedAtUtc]));

        // Replace-not-patch, as UpsertWorkItemAllocationsAsync: every key here
        // is this tenant's, having just been matched or inserted under it.
        foreach (var chunk in allocations.Keys.Chunk(SqlInList.ChunkSize))
        {
            await database.ExecuteAsync(
                $"DELETE FROM {WorkItemAllocationDto.TableName} WHERE tenantId = @0 AND workItemKey IN (@1)", tenantId, chunk);
        }

        await SqlMultiRow.InsertAsync(database, WorkItemAllocationDto.TableName,
            ["allocationKey", "tenantId", "workItemKey", "staffKey", "isPrimary", "createdAtUtc"],
            allocations.SelectMany(pair => pair.Value.Select((staffKey, i) =>
                (object?[])[Guid.NewGuid(), tenantId, pair.Key, staffKey, i == 0, nowUtc])));

        scope.Complete();
        return saved;
    }

    /// <summary>
    /// Decides, row by row in input order, whether a batched upsert updates
    /// an existing row or inserts a new one, matching the single-row path: a
    /// row whose (source, external id) was already seen earlier in the batch
    /// updates that earlier row, as a second single-row call would.
    /// </summary>
    private sealed class BatchPlan<TDto>(Dictionary<string, TDto> existing) where TDto : class
    {
        private readonly Dictionary<string, TDto> _seen = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<TDto> _updates = new(System.Collections.Generic.ReferenceEqualityComparer.Instance);

        public List<TDto> Inserts { get; } = [];

        public IEnumerable<TDto> Updates => _updates;

        public TDto Resolve(string? externalSource, string? externalId, Func<TDto> create)
        {
            var key = ExternalKey(externalSource, externalId);
            if (key is not null && _seen.TryGetValue(key, out var seen))
            {
                return seen;
            }

            TDto dto;
            if (key is not null && existing.TryGetValue(key, out var found))
            {
                dto = found;
                _updates.Add(dto);
            }
            else
            {
                dto = create();
                Inserts.Add(dto);
            }

            if (key is not null)
            {
                _seen[key] = dto;
            }

            return dto;
        }
    }

    private static string? ExternalKey(string? externalSource, string? externalId) =>
        externalSource is null || externalId is null ? null : $"{externalSource}\n{externalId}";

    /// <summary>Batched FindByExternalIdAsync: existing rows keyed by (source, external id), one read per source and chunk.</summary>
    private static async Task<Dictionary<string, TDto>> FindByExternalIdsAsync<TDto>(
        IUmbracoDatabase database, IEnumerable<(string? Source, string? Id)> keys,
        Func<TDto, string?> sourceOf, Func<TDto, string?> idOf, Guid tenantId, CancellationToken cancellationToken = default)
        where TDto : class
    {
        var found = new Dictionary<string, TDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in keys.Where(k => k.Source is not null && k.Id is not null).GroupBy(k => k.Source!, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var chunk in source.Select(k => k.Id!).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(SqlInList.ChunkSize))
            {
                var rows = await database.FetchAsync<TDto>(
                    Sql.Builder.Where("externalSource = @0 AND externalId IN (@1) AND tenantId = @2", source.Key, chunk, tenantId), cancellationToken);
                foreach (var row in rows)
                {
                    found[ExternalKey(sourceOf(row), idOf(row))!] = row;
                }
            }
        }

        return found;
    }

    /// <summary>Batched EnsureBelongsToTenantAsync: every key must be a row of this tenant, or the batch fails naming the first that is not.</summary>
    private static async Task EnsureAllBelongToTenantAsync(IUmbracoDatabase database, string table, string keyColumn, IEnumerable<Guid> keys, Guid tenantId, string entityName)
    {
        var wanted = keys.Distinct().ToList();
        var owned = new HashSet<Guid>();
        foreach (var chunk in wanted.Chunk(SqlInList.ChunkSize))
        {
            owned.UnionWith(await database.FetchAsync<Guid>(
                $"SELECT {SqlIdentifier.Quote(keyColumn)} FROM {SqlIdentifier.Quote(table)} WHERE tenantId = @0 AND {SqlIdentifier.Quote(keyColumn)} IN (@1)", tenantId, chunk));
        }

        foreach (var key in wanted.Where(key => !owned.Contains(key)))
        {
            throw new CrossTenantReferenceException(entityName, key);
        }
    }

    public async Task UpsertWorkItemAllocationsAsync(Guid workItemKey, IReadOnlyList<Guid> staffKeys, DateTime nowUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();

        await EnsureBelongsToTenantAsync<WorkItemDto>(scope.Database, "workItemKey", workItemKey, d => d.TenantId, tenantId, "WorkItem");

        var existingRows = await scope.Database.FetchAsync<WorkItemAllocationDto>(
            Sql.Builder.Where("workItemKey = @0 AND tenantId = @1", workItemKey, tenantId));
        foreach (var row in existingRows)
        {
            await scope.Database.DeleteAsync(row);
        }

        for (var i = 0; i < staffKeys.Count; i++)
        {
            await scope.Database.InsertAsync(new WorkItemAllocationDto
            {
                AllocationKey = Guid.NewGuid(),
                TenantId = tenantId,
                WorkItemKey = workItemKey,
                StaffKey = staffKeys[i],
                IsPrimary = i == 0,
                CreatedAtUtc = nowUtc
            });
        }

        scope.Complete();
    }

    public async Task<Dependency> UpsertDependencyAsync(Dependency dependency, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await EnsureBelongsToTenantAsync<WorkItemDto>(scope.Database, "workItemKey", dependency.WorkItemKey, d => d.TenantId, tenantId, "WorkItem");
        await EnsureBelongsToTenantAsync<WorkItemDto>(scope.Database, "workItemKey", dependency.DependsOnWorkItemKey, d => d.TenantId, tenantId, "WorkItem");
        var existing = await scope.Database.FirstOrDefaultAsync<DependencyDto>(
            Sql.Builder.Where("workItemKey = @0 AND dependsOnWorkItemKey = @1 AND tenantId = @2", dependency.WorkItemKey, dependency.DependsOnWorkItemKey, tenantId));

        if (existing is not null)
        {
            scope.Complete();
            return Map(existing);
        }

        var dto = new DependencyDto
        {
            DependencyKey = Guid.NewGuid(),
            TenantId = tenantId,
            WorkItemKey = dependency.WorkItemKey,
            DependsOnWorkItemKey = dependency.DependsOnWorkItemKey,
            CreatedAtUtc = dependency.CreatedAtUtc
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<Risk> UpsertRiskAsync(Risk risk, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await EnsureBelongsToTenantAsync<ProjectDto>(scope.Database, "projectKey", risk.ProjectKey, d => d.TenantId, tenantId, "Project");
        var existing = await scope.Database.FirstOrDefaultAsync<RiskDto>(
            Sql.Builder.Where("riskKey = @0 AND tenantId = @1", risk.RiskKey, tenantId));

        var dto = existing ?? new RiskDto { RiskKey = risk.RiskKey, CreatedAtUtc = risk.CreatedAtUtc };
        dto.TenantId = tenantId;
        dto.ProjectKey = risk.ProjectKey;
        dto.Title = risk.Title;
        dto.Description = risk.Description;
        dto.Severity = risk.Severity.ToString();
        dto.Status = risk.Status.ToString();
        dto.UpdatedAtUtc = risk.UpdatedAtUtc;

        await SaveAsync(scope.Database, dto, existing is not null);
        scope.Complete();

        return Map(dto);
    }

    public async Task<Issue> UpsertIssueAsync(Issue issue, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await EnsureBelongsToTenantAsync<ProjectDto>(scope.Database, "projectKey", issue.ProjectKey, d => d.TenantId, tenantId, "Project");
        var existing = await scope.Database.FirstOrDefaultAsync<IssueDto>(
            Sql.Builder.Where("issueKey = @0 AND tenantId = @1", issue.IssueKey, tenantId));

        var dto = existing ?? new IssueDto { IssueKey = issue.IssueKey, CreatedAtUtc = issue.CreatedAtUtc };
        dto.TenantId = tenantId;
        dto.ProjectKey = issue.ProjectKey;
        dto.Title = issue.Title;
        dto.Description = issue.Description;
        dto.Severity = issue.Severity.ToString();
        dto.Status = issue.Status.ToString();
        dto.UpdatedAtUtc = issue.UpdatedAtUtc;

        await SaveAsync(scope.Database, dto, existing is not null);
        scope.Complete();

        return Map(dto);
    }

    public async Task<TimeEntry> UpsertTimeEntryAsync(TimeEntry timeEntry, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        if (timeEntry.WorkItemKey is Guid workItemKey)
        {
            await EnsureBelongsToTenantAsync<WorkItemDto>(scope.Database, "workItemKey", workItemKey, d => d.TenantId, tenantId, "WorkItem");
        }

        var existing = await FindByExternalIdAsync<TimeEntryDto>(scope.Database, timeEntry.ExternalSource, timeEntry.ExternalId, tenantId);

        var dto = existing ?? new TimeEntryDto { TimeEntryKey = Guid.NewGuid(), CreatedAtUtc = timeEntry.CreatedAtUtc };
        Apply(dto, timeEntry, tenantId);

        await SaveAsync(scope.Database, dto, existing is not null);
        scope.Complete();

        return Map(dto);
    }

    // The one definition of what an upsert writes to a time entry row. workDate
    // is always the entry's ReportDate, which the period seek relies on.
    private static void Apply(TimeEntryDto dto, TimeEntry timeEntry, Guid tenantId)
    {
        dto.TenantId = tenantId;
        dto.WorkItemKey = timeEntry.WorkItemKey;
        dto.StaffKey = timeEntry.StaffKey;
        dto.DurationHours = timeEntry.DurationHours;
        dto.StartedAtUtc = timeEntry.StartedAtUtc;
        dto.WorkDate = timeEntry.ReportDate?.ToDateTime(TimeOnly.MinValue);
        dto.IsBillable = timeEntry.IsBillable;
        dto.BillabilityKnown = timeEntry.BillabilityKnown;
        dto.ExternalSource = timeEntry.ExternalSource;
        dto.ExternalId = timeEntry.ExternalId;
        dto.SourceWorkItemExternalId = timeEntry.SourceWorkItemExternalId;
        dto.UpdatedAtUtc = timeEntry.UpdatedAtUtc;
    }

    private static readonly string[] TimeEntryUpdateColumns =
    [
        "workItemKey", "staffKey", "durationHours", "startedAtUtc", "workDate", "isBillable", "billabilityKnown",
        "externalSource", "externalId", "sourceWorkItemExternalId", "updatedAtUtc"
    ];

    private static readonly string[] TimeEntryInsertColumns = ["timeEntryKey", "tenantId", .. TimeEntryUpdateColumns, "createdAtUtc"];

    private static object?[] UpdateValues(TimeEntryDto d) =>
    [
        d.WorkItemKey, d.StaffKey, d.DurationHours, d.StartedAtUtc, d.WorkDate, d.IsBillable, d.BillabilityKnown,
        d.ExternalSource, d.ExternalId, d.SourceWorkItemExternalId, d.UpdatedAtUtc
    ];

    public async Task<IReadOnlyList<TimeEntry>> UpsertTimeEntriesAsync(IReadOnlyList<TimeEntry> entries, Guid tenantId)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        using var scope = scopeProvider.CreateScope();
        var database = scope.Database;

        await EnsureAllBelongToTenantAsync(database, WorkItemDto.TableName, "workItemKey",
            entries.Where(e => e.WorkItemKey is not null).Select(e => e.WorkItemKey!.Value), tenantId, "WorkItem");
        var existing = await FindByExternalIdsAsync<TimeEntryDto>(database, entries.Select(e => (e.ExternalSource, e.ExternalId)), d => d.ExternalSource, d => d.ExternalId, tenantId);

        var plan = new BatchPlan<TimeEntryDto>(existing);
        var saved = new List<TimeEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var dto = plan.Resolve(entry.ExternalSource, entry.ExternalId,
                () => new TimeEntryDto { TimeEntryKey = Guid.NewGuid(), CreatedAtUtc = entry.CreatedAtUtc });
            Apply(dto, entry, tenantId);
            saved.Add(Map(dto));
        }

        await SqlMultiRow.UpdateAsync(database, TimeEntryDto.TableName, "timeEntryKey", TimeEntryUpdateColumns,
            plan.Updates.Select(d => (object?[])[d.TimeEntryKey, .. UpdateValues(d)]), tenantId);
        await SqlMultiRow.InsertAsync(database, TimeEntryDto.TableName, TimeEntryInsertColumns,
            plan.Inserts.Select(d => (object?[])[d.TimeEntryKey, d.TenantId, .. UpdateValues(d), d.CreatedAtUtc]));

        scope.Complete();
        return saved;
    }

    public async Task<IReadOnlyDictionary<string, Guid>> GetWorkItemKeysByExternalIdAsync(string externalSource, IReadOnlyCollection<string> externalIds, Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var found = await FindByExternalIdsAsync<WorkItemDto>(scope.Database, externalIds.Select(id => ((string?)externalSource, (string?)id)),
            d => d.ExternalSource, d => d.ExternalId, tenantId, cancellationToken);
        return found.Values
            .Where(d => d.ExternalId is not null)
            .ToDictionary(d => d.ExternalId!, d => d.WorkItemKey, StringComparer.OrdinalIgnoreCase);
    }

    // Tempo reconciliation: removes only a record the source's audit feed
    // names as deleted. Never called to infer deletion from an absent result.
    public async Task<int> DeleteTimeEntryByExternalIdAsync(string source, string externalId, Guid tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        using var scope = scopeProvider.CreateScope();
        var count = await scope.Database.ExecuteAsync(
            $"DELETE FROM {TimeEntryDto.TableName} WHERE tenantId = @0 AND externalSource = @1 AND externalId = @2",
            new object[] { tenantId, source, externalId });
        scope.Complete();
        return count;
    }

    public async Task<int> DeleteTimeEntriesByExternalIdsAsync(string source, IReadOnlyCollection<string> externalIds, Guid tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        using var scope = scopeProvider.CreateScope();
        var removed = 0;
        foreach (var chunk in externalIds.Distinct(StringComparer.Ordinal).Chunk(SqlInList.ChunkSize))
        {
            removed += await scope.Database.ExecuteAsync(
                $"DELETE FROM {TimeEntryDto.TableName} WHERE tenantId = @0 AND externalSource = @1 AND externalId IN (@2)",
                tenantId, source, chunk);
        }
        scope.Complete();
        return removed;
    }

    // Everything that points at a work item goes with it, except recorded
    // time, which is unlinked: the hours happened even if the item is gone.
    public async Task<int> RemoveWorkItemsAsync(IReadOnlyCollection<Guid> workItemKeys, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var database = scope.Database;
        var removed = 0;
        foreach (var chunk in workItemKeys.Distinct().Chunk(SqlInList.ChunkSize))
        {
            await database.ExecuteAsync(
                $"UPDATE {TimeEntryDto.TableName} SET workItemKey = NULL WHERE tenantId = @0 AND workItemKey IN (@1)", tenantId, chunk);
            await database.ExecuteAsync(
                $"DELETE FROM {WorkItemAllocationDto.TableName} WHERE tenantId = @0 AND workItemKey IN (@1)", tenantId, chunk);
            await database.ExecuteAsync(
                $"DELETE FROM {DependencyDto.TableName} WHERE tenantId = @0 AND (workItemKey IN (@1) OR dependsOnWorkItemKey IN (@1))", tenantId, chunk);
            await database.ExecuteAsync(
                $"DELETE FROM {EstimateBaselineDto.TableName} WHERE tenantId = @0 AND workItemKey IN (@1)", tenantId, chunk);
            removed += await database.ExecuteAsync(
                $"DELETE FROM {WorkItemDto.TableName} WHERE tenantId = @0 AND workItemKey IN (@1)", tenantId, chunk);
        }
        scope.Complete();
        return removed;
    }

    public async Task<IReadOnlyList<TimeEntry>> GetTimeEntriesBySourceAsync(Guid tenantId, string source, CancellationToken cancellationToken = default)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<TimeEntryDto>(
            Sql.Builder.Where("tenantId = @0 AND externalSource = @1", tenantId, source), cancellationToken);
        return dtos.Select(Map).ToList();
    }

    public async Task<Customer> UpsertCustomerAsync(Customer customer, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var existing = await scope.Database.FirstOrDefaultAsync<CustomerDto>(
            Sql.Builder.Where("customerKey = @0 AND tenantId = @1", customer.CustomerKey, tenantId));

        var dto = existing ?? new CustomerDto { CustomerKey = customer.CustomerKey, CreatedAtUtc = customer.CreatedAtUtc };
        dto.TenantId = tenantId;
        dto.Name = customer.Name;
        dto.UpdatedAtUtc = customer.UpdatedAtUtc;

        await SaveAsync(scope.Database, dto, existing is not null);
        scope.Complete();

        return Map(dto);
    }

    public async Task<Programme> SetProgrammeBudgetAsync(Guid programmeKey, decimal? budgetAmount, string? budgetCurrency, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<ProgrammeDto>(
            Sql.Builder.Where("programmeKey = @0 AND tenantId = @1", programmeKey, tenantId))
            ?? throw new CrossTenantReferenceException("Programme", programmeKey);

        dto.BudgetAmount = budgetAmount;
        dto.BudgetCurrency = budgetCurrency;
        await scope.Database.UpdateAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<Programme> AssignProgrammeToCustomerAsync(Guid programmeKey, Guid? customerKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<ProgrammeDto>(
            Sql.Builder.Where("programmeKey = @0 AND tenantId = @1", programmeKey, tenantId))
            ?? throw new CrossTenantReferenceException("Programme", programmeKey);

        if (customerKey is Guid ownedCustomerKey)
        {
            await EnsureBelongsToTenantAsync<CustomerDto>(scope.Database, "customerKey", ownedCustomerKey, d => d.TenantId, tenantId, "Customer");
        }

        dto.CustomerKey = customerKey;
        await scope.Database.UpdateAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<WorkstreamBaseline> LockBaselineAsync(Guid workstreamKey, decimal baselineHours, Guid? lockedByStaffKey, DateTime nowUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var existing = await scope.Database.FirstOrDefaultAsync<WorkstreamBaselineDto>(
            Sql.Builder.Where("workstreamKey = @0 AND tenantId = @1", workstreamKey, tenantId));

        if (existing is not null)
        {
            scope.Complete();
            return Map(existing);
        }

        await EnsureBelongsToTenantAsync<WorkstreamDto>(scope.Database, "workstreamKey", workstreamKey, d => d.TenantId, tenantId, "Workstream");

        var dto = new WorkstreamBaselineDto
        {
            WorkstreamKey = workstreamKey,
            TenantId = tenantId,
            BaselineHours = baselineHours,
            LockedAtUtc = nowUtc,
            LockedByStaffKey = lockedByStaffKey
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<ChangeRequest> CreateChangeRequestAsync(ChangeRequest changeRequest, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await EnsureBelongsToTenantAsync<ProjectDto>(scope.Database, "projectKey", changeRequest.ProjectKey, d => d.TenantId, tenantId, "Project");
        var dto = new ChangeRequestDto
        {
            ChangeRequestKey = changeRequest.ChangeRequestKey,
            TenantId = tenantId,
            ProjectKey = changeRequest.ProjectKey,
            Title = changeRequest.Title,
            Description = changeRequest.Description,
            Status = changeRequest.Status.ToString(),
            RequestedByStaffKey = changeRequest.RequestedByStaffKey,
            DecidedByStaffKey = changeRequest.DecidedByStaffKey,
            DecidedAtUtc = changeRequest.DecidedAtUtc,
            CreatedAtUtc = changeRequest.CreatedAtUtc,
            UpdatedAtUtc = changeRequest.UpdatedAtUtc
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<ChangeRequest> DecideChangeRequestAsync(Guid changeRequestKey, ChangeRequestStatus status, Guid? decidedByStaffKey, DateTime nowUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<ChangeRequestDto>(
            Sql.Builder.Where("changeRequestKey = @0 AND tenantId = @1", changeRequestKey, tenantId))
            ?? throw new CrossTenantReferenceException("ChangeRequest", changeRequestKey);

        dto.Status = status.ToString();
        dto.DecidedByStaffKey = decidedByStaffKey;
        dto.DecidedAtUtc = nowUtc;
        dto.UpdatedAtUtc = nowUtc;

        await scope.Database.UpdateAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<ProgrammeStakeholder> AddStakeholderAsync(ProgrammeStakeholder stakeholder, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await EnsureBelongsToTenantAsync<ProgrammeDto>(scope.Database, "programmeKey", stakeholder.ProgrammeKey, d => d.TenantId, tenantId, "Programme");
        var dto = new ProgrammeStakeholderDto
        {
            ProgrammeStakeholderKey = stakeholder.ProgrammeStakeholderKey,
            TenantId = tenantId,
            ProgrammeKey = stakeholder.ProgrammeKey,
            StaffKey = stakeholder.StaffKey,
            ExternalName = stakeholder.ExternalName,
            Role = stakeholder.Role.ToString(),
            CreatedAtUtc = stakeholder.CreatedAtUtc
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task RemoveStakeholderAsync(Guid programmeStakeholderKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var existing = await scope.Database.FirstOrDefaultAsync<ProgrammeStakeholderDto>(
            Sql.Builder.Where("programmeStakeholderKey = @0 AND tenantId = @1", programmeStakeholderKey, tenantId));

        if (existing is not null)
        {
            await scope.Database.DeleteAsync(existing);
        }

        scope.Complete();
    }

    public async Task<ReportingSnapshot> CaptureReportingSnapshotAsync(ReportingSnapshot snapshot, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = new ReportingSnapshotDto
        {
            SnapshotKey = snapshot.SnapshotKey,
            TenantId = tenantId,
            CapturedAtUtc = snapshot.CapturedAtUtc,
            PeriodStart = snapshot.PeriodStart.ToDateTime(TimeOnly.MinValue),
            PeriodEnd = snapshot.PeriodEnd.ToDateTime(TimeOnly.MinValue),
            TotalEstimatedHours = snapshot.TotalEstimatedHours,
            TotalActualHours = snapshot.TotalActualHours,
            TotalVarianceHours = snapshot.TotalVarianceHours,
            OpenWorkItems = snapshot.OpenWorkItems,
            BlockedWorkItems = snapshot.BlockedWorkItems,
            AverageUtilisationPercent = snapshot.AverageUtilisationPercent,
            CapturedByStaffKey = snapshot.CapturedByStaffKey
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    private static ReportingSnapshot Map(ReportingSnapshotDto dto) => new()
    {
        SnapshotKey = dto.SnapshotKey,
        TenantId = dto.TenantId,
        CapturedAtUtc = dto.CapturedAtUtc,
        PeriodStart = DateOnly.FromDateTime(dto.PeriodStart),
        PeriodEnd = DateOnly.FromDateTime(dto.PeriodEnd),
        TotalEstimatedHours = dto.TotalEstimatedHours,
        TotalActualHours = dto.TotalActualHours,
        TotalVarianceHours = dto.TotalVarianceHours,
        OpenWorkItems = dto.OpenWorkItems,
        BlockedWorkItems = dto.BlockedWorkItems,
        AverageUtilisationPercent = dto.AverageUtilisationPercent,
        CapturedByStaffKey = dto.CapturedByStaffKey
    };

    /// <summary>
    /// Raises an open alert unless one is already open for the same tenant,
    /// type and entity, in which case that one is returned. The check and the
    /// insert are one statement holding a key-range lock, so concurrent
    /// detection runs cannot both insert; UX_ProgrammeOps_Alert_open is the
    /// backstop that makes a duplicate impossible rather than unlikely.
    /// </summary>
    public async Task<Alert> RaiseAlertAsync(Alert alert, Guid tenantId)
    {
        if (alert.AcknowledgedAtUtc is null)
        {
            using var openScope = scopeProvider.CreateScope();
            var type = alert.Type.ToString();
            await openScope.Database.ExecuteAsync(
                $"""
                INSERT INTO {AlertDto.TableName} (alertKey, tenantId, [type], entityKey, message, raisedAtUtc)
                SELECT @0, @1, @2, @3, @4, @5
                WHERE NOT EXISTS (
                    SELECT 1 FROM {AlertDto.TableName} WITH (UPDLOCK, HOLDLOCK)
                    WHERE tenantId = @1 AND [type] = @2 AND entityKey = @3 AND acknowledgedAtUtc IS NULL)
                """,
                alert.AlertKey, tenantId, type, alert.EntityKey, alert.Message, alert.RaisedAtUtc);
            var open = await openScope.Database.FirstAsync<AlertDto>(
                Sql.Builder.Where("tenantId = @0 AND [type] = @1 AND entityKey = @2 AND acknowledgedAtUtc IS NULL", tenantId, type, alert.EntityKey));
            openScope.Complete();
            return Map(open);
        }

        using var scope = scopeProvider.CreateScope();
        var dto = new AlertDto
        {
            AlertKey = alert.AlertKey,
            TenantId = tenantId,
            Type = alert.Type.ToString(),
            EntityKey = alert.EntityKey,
            Message = alert.Message,
            RaisedAtUtc = alert.RaisedAtUtc,
            AcknowledgedAtUtc = alert.AcknowledgedAtUtc,
            AcknowledgedByStaffKey = alert.AcknowledgedByStaffKey
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task AcknowledgeAlertAsync(Guid alertKey, Guid? acknowledgedByStaffKey, DateTime nowUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<AlertDto>(
            Sql.Builder.Where("alertKey = @0 AND tenantId = @1", alertKey, tenantId));

        if (dto is not null)
        {
            dto.AcknowledgedAtUtc = nowUtc;
            dto.AcknowledgedByStaffKey = acknowledgedByStaffKey;
            await scope.Database.UpdateAsync(dto);
        }

        scope.Complete();
    }

    private static Alert Map(AlertDto dto) => new()
    {
        AlertKey = dto.AlertKey,
        TenantId = dto.TenantId,
        Type = Enum.Parse<AlertType>(dto.Type),
        EntityKey = dto.EntityKey,
        Message = dto.Message,
        RaisedAtUtc = dto.RaisedAtUtc,
        AcknowledgedAtUtc = dto.AcknowledgedAtUtc,
        AcknowledgedByStaffKey = dto.AcknowledgedByStaffKey
    };

    private static async Task<TDto?> FindByExternalIdAsync<TDto>(IUmbracoDatabase database, string? externalSource, string? externalId, Guid tenantId, CancellationToken cancellationToken = default)
        where TDto : class
    {
        if (externalSource is null || externalId is null)
        {
            return null;
        }

        return await database.FirstOrDefaultAsync<TDto>(
            Sql.Builder.Where("externalSource = @0 AND externalId = @1 AND tenantId = @2", externalSource, externalId, tenantId), cancellationToken);
    }

    /// <summary>
    /// Every Upsert/Create method below stamps TenantId on the row it
    /// writes, but a foreign key it takes from the caller (ProgrammeKey,
    /// ProjectKey, WorkstreamKey, WorkItemKey, CustomerKey) was never
    /// checked against that same tenant — a controller-supplied key from
    /// another tenant would be accepted as-is, silently cross-linking two
    /// tenants' data. This is the fix: look the parent up by its own key
    /// and refuse unless it belongs to the same tenant. Thrown as
    /// CrossTenantReferenceException so callers can turn it into NotFound(),
    /// the same "wrong tenant looks like doesn't exist" treatment every
    /// other guessed-key case in this codebase already gets.
    /// </summary>
    private static async Task EnsureBelongsToTenantAsync<TDto>(IUmbracoDatabase database, string keyColumn, Guid key, Func<TDto, Guid?> tenantIdSelector, Guid tenantId, string entityName)
        where TDto : class
    {
        var parent = await database.FirstOrDefaultAsync<TDto>(Sql.Builder.Where($"{SqlIdentifier.Quote(keyColumn)} = @0", key));
        if (parent is null || tenantIdSelector(parent) != tenantId)
        {
            throw new CrossTenantReferenceException(entityName, key);
        }
    }

    private static Task SaveAsync<TDto>(IUmbracoDatabase database, TDto dto, bool isUpdate)
        where TDto : notnull =>
        isUpdate ? database.UpdateAsync(dto) : database.InsertAsync(dto);

    private static Programme Map(ProgrammeDto dto) => new()
    {
        ProgrammeKey = dto.ProgrammeKey,
        TenantId = dto.TenantId,
        Name = dto.Name,
        Description = dto.Description,
        CustomerKey = dto.CustomerKey,
        BudgetAmount = dto.BudgetAmount,
        BudgetCurrency = dto.BudgetCurrency,
        ExternalSource = dto.ExternalSource,
        ExternalId = dto.ExternalId,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static Project Map(ProjectDto dto) => new()
    {
        ProjectKey = dto.ProjectKey,
        TenantId = dto.TenantId,
        ProgrammeKey = dto.ProgrammeKey,
        Name = dto.Name,
        Description = dto.Description,
        ExternalSource = dto.ExternalSource,
        ExternalId = dto.ExternalId,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static Workstream Map(WorkstreamDto dto) => new()
    {
        WorkstreamKey = dto.WorkstreamKey,
        TenantId = dto.TenantId,
        ProjectKey = dto.ProjectKey,
        Name = dto.Name,
        ExternalSource = dto.ExternalSource,
        ExternalId = dto.ExternalId,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static WorkItem Map(WorkItemDto dto) => new()
    {
        WorkItemKey = dto.WorkItemKey,
        TenantId = dto.TenantId,
        WorkstreamKey = dto.WorkstreamKey,
        Title = dto.Title,
        Stage = Enum.Parse<WorkItemLifecycleStage>(dto.Stage),
        RawStatus = dto.RawStatus,
        IsMilestone = dto.IsMilestone,
        AssignedStaffKey = dto.AssignedStaffKey,
        DueDateUtc = dto.DueDateUtc,
        EstimatedHours = dto.EstimatedHours,
        ExternalSource = dto.ExternalSource,
        ExternalId = dto.ExternalId,
        ParentExternalId = dto.ParentExternalId,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static Dependency Map(DependencyDto dto) => new()
    {
        DependencyKey = dto.DependencyKey,
        TenantId = dto.TenantId,
        WorkItemKey = dto.WorkItemKey,
        DependsOnWorkItemKey = dto.DependsOnWorkItemKey,
        CreatedAtUtc = dto.CreatedAtUtc
    };

    private static WorkItemAllocation Map(WorkItemAllocationDto dto) => new()
    {
        AllocationKey = dto.AllocationKey,
        TenantId = dto.TenantId,
        WorkItemKey = dto.WorkItemKey,
        StaffKey = dto.StaffKey,
        IsPrimary = dto.IsPrimary,
        CreatedAtUtc = dto.CreatedAtUtc
    };

    private static Risk Map(RiskDto dto) => new()
    {
        RiskKey = dto.RiskKey,
        TenantId = dto.TenantId,
        ProjectKey = dto.ProjectKey,
        Title = dto.Title,
        Description = dto.Description,
        Severity = Enum.Parse<SeverityLevel>(dto.Severity),
        Status = Enum.Parse<RiskStatus>(dto.Status),
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static Issue Map(IssueDto dto) => new()
    {
        IssueKey = dto.IssueKey,
        TenantId = dto.TenantId,
        ProjectKey = dto.ProjectKey,
        Title = dto.Title,
        Description = dto.Description,
        Severity = Enum.Parse<SeverityLevel>(dto.Severity),
        Status = Enum.Parse<IssueStatus>(dto.Status),
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static TimeEntry Map(TimeEntryDto dto) => new()
    {
        TimeEntryKey = dto.TimeEntryKey,
        TenantId = dto.TenantId,
        WorkItemKey = dto.WorkItemKey,
        StaffKey = dto.StaffKey,
        DurationHours = dto.DurationHours,
        StartedAtUtc = dto.StartedAtUtc,
        WorkDate = dto.WorkDate is { } workDate ? DateOnly.FromDateTime(workDate) : null,
        IsBillable = dto.IsBillable,
        BillabilityKnown = dto.BillabilityKnown,
        ExternalSource = dto.ExternalSource,
        ExternalId = dto.ExternalId,
        SourceWorkItemExternalId = dto.SourceWorkItemExternalId,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static Customer Map(CustomerDto dto) => new()
    {
        CustomerKey = dto.CustomerKey,
        TenantId = dto.TenantId,
        Name = dto.Name,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static WorkstreamBaseline Map(WorkstreamBaselineDto dto) => new()
    {
        WorkstreamKey = dto.WorkstreamKey,
        TenantId = dto.TenantId,
        BaselineHours = dto.BaselineHours,
        LockedAtUtc = dto.LockedAtUtc,
        LockedByStaffKey = dto.LockedByStaffKey
    };

    private static ChangeRequest Map(ChangeRequestDto dto) => new()
    {
        ChangeRequestKey = dto.ChangeRequestKey,
        TenantId = dto.TenantId,
        ProjectKey = dto.ProjectKey,
        Title = dto.Title,
        Description = dto.Description,
        Status = Enum.Parse<ChangeRequestStatus>(dto.Status),
        RequestedByStaffKey = dto.RequestedByStaffKey,
        DecidedByStaffKey = dto.DecidedByStaffKey,
        DecidedAtUtc = dto.DecidedAtUtc,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static ProgrammeStakeholder Map(ProgrammeStakeholderDto dto) => new()
    {
        ProgrammeStakeholderKey = dto.ProgrammeStakeholderKey,
        TenantId = dto.TenantId,
        ProgrammeKey = dto.ProgrammeKey,
        StaffKey = dto.StaffKey,
        ExternalName = dto.ExternalName,
        Role = Enum.Parse<StakeholderRole>(dto.Role),
        CreatedAtUtc = dto.CreatedAtUtc
    };
}
