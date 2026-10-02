namespace ProgrammePulse.Models.Programme;

/// <summary>
/// A logged block of work against a WorkItem, mapped from ClickUp time entries.
/// WorkItemKey/StaffKey are nullable because correlation is best-effort — the
/// same limitations as WorkItem.AssignedStaffKey apply: a task not yet synced,
/// or no staff record matching the entry's user email. WorkDate is the source's
/// calendar date for period reporting; it does not imply that StartedAtUtc is
/// known. Tempo explicitly distinguishes these because its UTC timestamp can
/// reflect the browser time zone. Entries with neither date are excluded from
/// period-based reporting but remain in unscoped effort totals.
/// </summary>
public sealed record TimeEntry
{
    public required Guid TimeEntryKey { get; init; }

    public Guid? TenantId { get; init; }

    public Guid? WorkItemKey { get; init; }

    public Guid? StaffKey { get; init; }

    public required decimal DurationHours { get; init; }

    public DateTime? StartedAtUtc { get; init; }

    public DateOnly? WorkDate { get; init; }

    public bool IsBillable { get; init; } = true;

    /// <summary>False when the provider supplied no approved billability meaning; never treat that as non-billable fact.</summary>
    public bool BillabilityKnown { get; init; } = true;

    public string? ExternalSource { get; init; }

    public string? ExternalId { get; init; }

    /// <summary>
    /// The source's own id for the work item this entry was logged against,
    /// in the same form as that item's <see cref="WorkItem.ExternalId"/>.
    /// Kept even when <see cref="WorkItemKey"/> is null, because "these hours
    /// belong to an issue that isn't synced" is actionable and "these hours
    /// belong to nothing" is not. Null when the source named no item.
    /// </summary>
    public string? SourceWorkItemExternalId { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    /// <summary>
    /// The date period reports attribute this entry to: the provider's work
    /// date, else the UTC date of its start, else none. The repository
    /// persists exactly this value as <c>workDate</c>, which is what lets a
    /// period read seek on (tenantId, workDate) instead of scanning history.
    /// </summary>
    public DateOnly? ReportDate => WorkDate
        ?? (StartedAtUtc is { } started ? DateOnly.FromDateTime(started) : null);
}

/// <summary>
/// Tenant-wide facts a period report states alongside its rows, without
/// reading the tenant's whole time history to find them.
/// </summary>
/// <param name="UndatedHours">Hours with no report date, which no period can include.</param>
/// <param name="HasTempoEntries">Whether any entry came from Tempo, whose billability is unconfirmed.</param>
public sealed record TimeEntryCoverage(decimal UndatedHours, bool HasTempoEntries);

/// <summary>Whether a tenant holds any work items, or any time entries, from one source.</summary>
public sealed record SourceDataPresence(bool HasWorkItems, bool HasTimeEntries)
{
    public bool Any => HasWorkItems || HasTimeEntries;
}

/// <summary>
/// How many distinct projects one person logged time against in one ISO
/// week: the concurrency series Delivery Load is built from. Only weeks with
/// at least one dated entry exist; zero means time was logged but against no
/// work the tenant has synced.
/// </summary>
public sealed record WeeklyProjectCount(Guid StaffKey, DateOnly WeekStart, int DistinctProjects)
{
    /// <summary>The Monday of the ISO week containing the date.</summary>
    public static DateOnly WeekStartOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
