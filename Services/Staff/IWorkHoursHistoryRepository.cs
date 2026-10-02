using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Services.Staff;

public interface IWorkHoursHistoryRepository
{
    Task<WorkHoursHistory?> GetCurrentAsync(Guid staffKey);

    Task<IReadOnlyList<WorkHoursHistory>> GetHistoryAsync(Guid staffKey);

    /// <summary>
    /// Contracted-hours history for several people in one read, keyed by
    /// staff key (no rows maps to an empty list). Same shape and reason as
    /// IStaffRateRepository's batched history read.
    /// </summary>
    async Task<IReadOnlyDictionary<Guid, IReadOnlyList<WorkHoursHistory>>> GetHistoryAsync(IReadOnlyCollection<Guid> staffKeys, CancellationToken cancellationToken = default)
    {
        var history = new Dictionary<Guid, IReadOnlyList<WorkHoursHistory>>();
        foreach (var staffKey in staffKeys.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            history[staffKey] = await GetHistoryAsync(staffKey);
        }

        return history;
    }

    /// <summary>
    /// Closes the current row's EffectiveToUtc (if one exists) and inserts a
    /// new current row, so history is append-only and audited for free —
    /// same shape as IStaffRateRepository.SetCurrentRateAsync.
    /// </summary>
    Task<WorkHoursHistory> SetCurrentHoursAsync(Guid staffKey, decimal hoursPerWeek, Guid changedByStaffKey);
}
