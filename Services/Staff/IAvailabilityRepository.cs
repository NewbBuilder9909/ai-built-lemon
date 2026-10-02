using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Services.Staff;

public interface IAvailabilityRepository
{
    Task<IReadOnlyList<Availability>> GetForStaffAsync(Guid staffKey, DateOnly from, DateOnly to);

    /// <summary>
    /// Availability in [from, to] for several people in one read, keyed by
    /// staff key (no rows maps to an empty list), each list in the same
    /// order as the single-person read.
    /// </summary>
    async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Availability>>> GetForStaffAsync(IReadOnlyCollection<Guid> staffKeys, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var rows = new Dictionary<Guid, IReadOnlyList<Availability>>();
        foreach (var staffKey in staffKeys.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows[staffKey] = await GetForStaffAsync(staffKey, from, to);
        }

        return rows;
    }

    Task<Availability> CreateAsync(Availability availability);
}
