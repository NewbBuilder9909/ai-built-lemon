using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Services.Staff;

/// <summary>
/// Admin-only. Nothing outside StaffAdminController and this repository should
/// ever call these methods — see StaffRate for why cost data is kept structurally
/// separate from the Staff record.
/// </summary>
public interface IStaffRateRepository
{
    Task<StaffRate?> GetCurrentAsync(Guid staffKey);

    Task<IReadOnlyList<StaffRate>> GetHistoryAsync(Guid staffKey);

    /// <summary>
    /// Rate history for several people in one read, keyed by staff key (a
    /// person with no rates maps to an empty list). Costing a period must not
    /// cost one round trip per person. The keys come from a tenant-scoped
    /// roster or time read, so the caller has already proven them.
    /// </summary>
    async Task<IReadOnlyDictionary<Guid, IReadOnlyList<StaffRate>>> GetHistoryAsync(IReadOnlyCollection<Guid> staffKeys, CancellationToken cancellationToken = default)
    {
        var history = new Dictionary<Guid, IReadOnlyList<StaffRate>>();
        foreach (var staffKey in staffKeys.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            history[staffKey] = await GetHistoryAsync(staffKey);
        }

        return history;
    }

    /// <summary>
    /// Closes the current rate's EffectiveToUtc (if one exists) and inserts a new
    /// current rate row, so rate history is append-only and audited for free.
    /// </summary>
    Task<StaffRate> SetCurrentRateAsync(Guid staffKey, decimal costPerHour, string rateCurrency, Guid changedByStaffKey);
}
