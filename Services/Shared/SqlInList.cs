namespace ProgrammePulse.Services.Shared;

/// <summary>
/// Helpers for batched "WHERE key IN (...)" reads. SQL Server caps a command
/// at 2,100 parameters, and NPoco expands a list into one parameter per
/// element, so a batched read fetches in chunks of <see cref="ChunkSize"/>.
/// </summary>
public static class SqlInList
{
    public const int ChunkSize = 1000;

    /// <summary>
    /// Groups rows by key, preserving their read order within each key, and
    /// maps every requested key, including ones with no rows (to an empty
    /// list), so a caller never has to tell "none" from "not asked".
    /// </summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<T>> GroupByKey<T>(
        IEnumerable<Guid> requestedKeys, IEnumerable<T> rows, Func<T, Guid> keyOf)
    {
        var result = requestedKeys.Distinct().ToDictionary(key => key, _ => (IReadOnlyList<T>)[]);
        foreach (var group in rows.GroupBy(keyOf))
        {
            result[group.Key] = group.ToList();
        }

        return result;
    }
}
