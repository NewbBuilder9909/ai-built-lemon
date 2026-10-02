namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// Turns a set of changed file paths into language hints.
///
/// What a hint means, exactly: "this person participated in changes to
/// files classified as this language during this period". It is **not** a
/// proficiency, it never becomes one automatically, and
/// <see cref="EngineeringEvidence.LanguageHints"/> is deliberately not
/// joined to <see cref="StaffSkillAssertion"/> anywhere in the data.
///
/// Generated and vendored paths are excluded because including them is
/// actively misleading: a lockfile refresh would make someone look like a
/// JSON expert, and checking in a vendored dependency would credit them
/// with tens of thousands of lines of someone else's language. Exclusion
/// is by path *and* by filename, because both are how generated code
/// arrives.
///
/// Pure, no I/O, provider-neutral — a GitLab or plain-git adapter uses the
/// same classifier on the same kind of path list.
/// </summary>
public static class ChangedPathClassifier
{
    /// <summary>
    /// A hint needs this many distinct files before it is reported. One
    /// touched file is noise — a typo fix in a README should not put
    /// "Markdown" on somebody's evidence.
    /// </summary>
    public const int MinimumFilesForHint = 2;

    /// <summary>Path segments that mean "not written here". Matched case-insensitively on any segment.</summary>
    private static readonly string[] VendoredSegments =
    [
        "node_modules", "vendor", "third_party", "thirdparty", "packages",
        "bower_components", "jspm_packages", "bin", "obj", "dist", "build",
        "out", "target", "coverage", "__pycache__", ".venv", "venv",
        "migrations/generated", "generated", "gen", "__generated__", ".next", ".nuxt"
    ];

    /// <summary>Filename markers that mean "a tool wrote this".</summary>
    private static readonly string[] GeneratedFileMarkers =
    [
        ".min.js", ".min.css", ".map", ".g.cs", ".generated.cs", ".designer.cs",
        ".pb.go", "_pb2.py", ".lock", "-lock.json", ".snap"
    ];

    /// <summary>Exact filenames that are lockfiles or tool output whatever their extension.</summary>
    private static readonly string[] GeneratedFileNames =
    [
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "composer.lock",
        "gemfile.lock", "cargo.lock", "poetry.lock", "packages.lock.json", "go.sum"
    ];

    private static readonly IReadOnlyDictionary<string, string> LanguageByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "csharp",
            [".fs"] = "fsharp",
            [".vb"] = "visual-basic",
            [".ts"] = "typescript",
            [".tsx"] = "typescript",
            [".js"] = "javascript",
            [".jsx"] = "javascript",
            [".mjs"] = "javascript",
            [".py"] = "python",
            [".rb"] = "ruby",
            [".go"] = "go",
            [".rs"] = "rust",
            [".java"] = "java",
            [".kt"] = "kotlin",
            [".swift"] = "swift",
            [".php"] = "php",
            [".sql"] = "sql",
            [".sh"] = "shell",
            [".ps1"] = "powershell",
            [".css"] = "css",
            [".scss"] = "css",
            [".less"] = "css",
            [".html"] = "html",
            [".cshtml"] = "razor",
            [".razor"] = "razor",
            [".yml"] = "yaml",
            [".yaml"] = "yaml",
            [".tf"] = "terraform",
            [".bicep"] = "bicep",
            [".dockerfile"] = "docker",
            [".md"] = "documentation"
        };

    /// <summary>Filenames with no useful extension that still identify a technology.</summary>
    private static readonly IReadOnlyDictionary<string, string> LanguageByFileName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dockerfile"] = "docker",
            ["makefile"] = "make"
        };

    /// <summary>
    /// True when a path should not contribute to any hint. Public because
    /// the ingestion service reports how many files it excluded — an
    /// exclusion nobody can see is indistinguishable from a bug.
    /// </summary>
    public static bool IsExcluded(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var normalized = path.Replace('\\', '/').Trim().TrimStart('/');
        var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];

        if (GeneratedFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
            || GeneratedFileMarkers.Any(marker => fileName.EndsWith(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Segment-wise, not substring: "vendor" must not exclude
        // "vendor-portal/Program.cs", which is somebody's actual work.
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var directories = segments.Length > 1 ? segments[..^1] : [];

        foreach (var segment in directories)
        {
            if (VendoredSegments.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            // Two-segment markers such as "migrations/generated".
            if (segment.StartsWith('.') && segment.Length > 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The language a single path suggests, or null if it suggests nothing useful.</summary>
    public static string? Classify(string? path)
    {
        if (IsExcluded(path))
        {
            return null;
        }

        var normalized = path!.Replace('\\', '/').Trim();
        var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];

        if (LanguageByFileName.TryGetValue(fileName, out var byName))
        {
            return byName;
        }

        var dot = fileName.LastIndexOf('.');
        if (dot < 0 || dot == fileName.Length - 1)
        {
            return null;
        }

        return LanguageByExtension.GetValueOrDefault(fileName[dot..]);
    }

    /// <summary>
    /// The hints a whole changeset supports, alphabetically. A language
    /// needs <see cref="MinimumFilesForHint"/> distinct files before it is
    /// reported, so a single incidental file does not become a claim.
    /// </summary>
    public static IReadOnlyList<string> Hints(IEnumerable<string?>? paths)
    {
        if (paths is null)
        {
            return [];
        }

        var filesByLanguage = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            var language = Classify(path);
            if (language is null)
            {
                continue;
            }

            if (!filesByLanguage.TryGetValue(language, out var files))
            {
                filesByLanguage[language] = files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            files.Add(path!);
        }

        return [.. filesByLanguage
            .Where(entry => entry.Value.Count >= MinimumFilesForHint)
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)];
    }
}
