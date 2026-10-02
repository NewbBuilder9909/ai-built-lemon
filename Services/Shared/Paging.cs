namespace ProgrammePulse.Services.Shared;

/// <summary>
/// A 1-based page of a list view (finding A7 of
/// docs/architecture-review-2026-09-24.md: list views loaded every row).
/// Built from untrusted query-string values, so it clamps rather than
/// throws: a page below 1 is page 1, and the size is kept within bounds.
/// </summary>
public readonly record struct PageRequest
{
    public const int DefaultSize = 50;
    public const int MaxSize = 200;

    /// <summary>
    /// The deepest page anyone can ask for. Without it ?page=2000000000
    /// overflows <see cref="Skip"/> to a negative offset (a server error) and
    /// a merely huge one forces a long scan (Aikido: uncontrolled resource
    /// consumption). No list here has anywhere near this many pages.
    /// </summary>
    public const int MaxNumber = 10_000;

    public PageRequest(int number, int size = DefaultSize)
    {
        Number = Math.Clamp(number, 1, MaxNumber);
        Size = Math.Clamp(size, 1, MaxSize);
    }

    public int Number { get; }

    public int Size { get; }

    public int Skip => (Number - 1) * Size;

    /// <summary>
    /// Rows to fetch: one more than the page, so whether a next page exists
    /// is known without a COUNT(*) over the whole list.
    /// </summary>
    public int Fetch => Size + 1;

    public static PageRequest First(int size = DefaultSize) => new(1, size);
}

/// <summary>One page of rows, and whether there are more after it.</summary>
public sealed record ResultPage<T>(IReadOnlyList<T> Items, int Number, int Size, bool HasNext)
{
    public bool HasPrevious => Number > 1;

    public PageLinks Links => new(Number, HasPrevious, HasNext);

    /// <summary>
    /// Builds a page from rows fetched with <see cref="PageRequest.Fetch"/>
    /// (at most one more than the page); the extra row only signals a next page.
    /// </summary>
    public static ResultPage<T> From(IReadOnlyList<T> fetched, PageRequest request) =>
        new(fetched.Take(request.Size).ToList(), request.Number, request.Size, fetched.Count > request.Size);

    /// <summary>Pages a list already held in memory (fakes, and bounded merges).</summary>
    public static ResultPage<T> Of(IEnumerable<T> all, PageRequest request) =>
        From(all.Skip(request.Skip).Take(request.Fetch).ToList(), request);
}

/// <summary>What a pager needs to render, without the page's row type.</summary>
public sealed record PageLinks(int Number, bool HasPrevious, bool HasNext)
{
    public bool IsNeeded => HasPrevious || HasNext;
}
