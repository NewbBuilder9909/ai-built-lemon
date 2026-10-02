using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// Names that must never appear in this repository, such as the author's
/// employers. A text is checked word by word and in adjacent word pairs, so
/// a two-word name is caught as well as a one-word one.
///
/// The public copy ships with an empty list: a hash of a well-known name can
/// be guessed, so publishing even hashes would say who was being kept out.
/// List names one per line in <c>~/.programmepulse/forbidden-names.txt</c>,
/// outside the repository; every test here reads it when it exists. For a
/// private fork, <c>scripts/forbidden-name-hash.ps1 -Name "..."</c> prints a
/// hash to add below.
/// </summary>
public static partial class ForbiddenNames
{
    private static readonly HashSet<string> Hashes = [];

    public static string LocalListPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".programmepulse", "forbidden-names.txt");

    private static readonly Lazy<HashSet<string>> All = new(() =>
    {
        var all = new HashSet<string>(Hashes, StringComparer.Ordinal);
        if (File.Exists(LocalListPath))
        {
            foreach (var line in File.ReadAllLines(LocalListPath).Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                all.Add(Hash(string.Join(' ', Words(line))));
            }
        }

        return all;
    });

    public static string Hash(string normalised) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised))).ToLowerInvariant();

    /// <summary>How many words or word pairs in <paramref name="text"/> are forbidden names. The names themselves are never returned.</summary>
    public static int CountIn(string text, IReadOnlySet<string>? hashes = null)
    {
        var set = hashes ?? All.Value;
        var words = Words(text);
        var hits = 0;
        for (var i = 0; i < words.Count; i++)
        {
            if (set.Contains(Hash(words[i]))) hits++;
            if (i + 1 < words.Count && set.Contains(Hash(words[i] + " " + words[i + 1]))) hits++;
        }

        return hits;
    }

    private static List<string> Words(string text) =>
        WordPattern().Matches(text).Select(m => m.Value.ToLowerInvariant()).ToList();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}

public class ForbiddenNamesTests
{
    private static readonly string[] Extensions =
        [".cs", ".cshtml", ".md", ".json", ".csv", ".txt", ".resx", ".css", ".js", ".mjs", ".ps1", ".sh", ".yml", ".yaml", ".xml", ".csproj", ".slnx", ".props", ".html"];

    private static readonly string[] SkippedDirectories =
        [".git", "bin", "obj", "artifacts", ".claude", "node_modules", "umbraco", "TestResults", ".vs"];

    [Fact]
    public void One_and_two_word_names_are_caught_whatever_their_case_and_punctuation()
    {
        var hashes = new HashSet<string> { ForbiddenNames.Hash("zarnwick"), ForbiddenNames.Hash("quobble hall") };

        Assert.Equal(2, ForbiddenNames.CountIn("Worked at ZARNWICK, then Quobble-Hall.", hashes));
        Assert.Equal(0, ForbiddenNames.CountIn("Quobble alone, Hallway, zarnwicks.", hashes));
    }

    [Fact]
    public void No_file_in_the_repository_names_an_owner_employer()
    {
        var offending = Files()
            .Select(path => (Path: Path.GetRelativePath(SourceTree.Root, path), Hits: ForbiddenNames.CountIn(File.ReadAllText(path))))
            .Where(f => f.Hits > 0)
            .Select(f => $"{f.Path} ({f.Hits})")
            .ToList();

        Assert.True(offending.Count == 0, "These files name an employer the owner listed as forbidden: " + string.Join(", ", offending));
    }

    private static IEnumerable<string> Files()
    {
        var pending = new Stack<string>([SourceTree.Root]);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }
}
