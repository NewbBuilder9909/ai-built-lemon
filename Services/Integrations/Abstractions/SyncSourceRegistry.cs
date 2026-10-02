namespace ProgrammePulse.Services.Integrations.Abstractions;

/// <summary>
/// The single list of integrated sources. Replaces the hard-coded vendor
/// literals that previously lived in the overview controller, the overview
/// view and SyncStatusQueryService.KnownSources — each of which had to be
/// edited by hand to add a source, and could silently disagree with the
/// others.
/// </summary>
public interface ISyncSourceRegistry
{
    /// <summary>Every registered source, in a stable display order.</summary>
    IReadOnlyList<ISyncSource> All { get; }

    /// <summary>The source with this <see cref="ISyncSource.Name"/>, or null. Case-insensitive, so a URL segment can be matched directly.</summary>
    ISyncSource? Find(string? name);
}

/// <summary>
/// Built over the DI-discovered <see cref="ISyncSource"/> implementations.
/// Registered scoped, not singleton: each source adapter wraps a scoped sync
/// service (which in turn holds scoped repositories and the per-run identity
/// resolver), so a singleton registry would capture and reuse one request's
/// dependencies for the lifetime of the process.
///
/// Ordered by DisplayName so the overview page and the platform console list
/// sources identically, and so a new source doesn't reshuffle the page
/// depending on composer registration order.
/// </summary>
public sealed class SyncSourceRegistry : ISyncSourceRegistry
{
    private readonly Dictionary<string, ISyncSource> _byName;

    public SyncSourceRegistry(IEnumerable<ISyncSource> sources)
    {
        All = sources.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

        // Two sources sharing a Name would silently corrupt the provenance
        // trail: SyncRun.Source and Silver ExternalSource both key on it, so
        // one source's runs and rows would be attributed to the other. Fail
        // at startup rather than at read time.
        var duplicates = All
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate ISyncSource.Name registered: {string.Join(", ", duplicates)}. "
                + "Each source's Name keys its sync runs and its Silver rows' ExternalSource, so it must be unique.");
        }

        _byName = All.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ISyncSource> All { get; }

    public ISyncSource? Find(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : _byName.GetValueOrDefault(name);
}
