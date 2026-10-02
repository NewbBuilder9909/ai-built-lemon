using NPoco;

namespace ProgrammePulse.Services.Shared;

/// <summary>SQL Server paging for a query that already ends in ORDER BY.</summary>
public static class SqlPaging
{
    /// <summary>
    /// Appends OFFSET/FETCH for the page, fetching one extra row so
    /// <see cref="ResultPage{T}.From"/> can tell whether a next page exists. The
    /// ORDER BY must be total (end on a unique column), or rows can repeat or
    /// vanish between pages.
    /// </summary>
    public static Sql ForPage(this Sql orderedQuery, PageRequest page) =>
        orderedQuery.Append("OFFSET @0 ROWS FETCH NEXT @1 ROWS ONLY", page.Skip, page.Fetch);
}
