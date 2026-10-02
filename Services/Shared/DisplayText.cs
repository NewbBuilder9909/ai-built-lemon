using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProgrammePulse.Services.Shared;

/// <summary>
/// Turns an identifier into words for a page: <c>UpcomingOverAllocation</c>
/// becomes "Upcoming over allocation". Enum names shown raw were one of the
/// clearest signs of an unfinished interface, and they broke mid-word in a
/// narrow column.
/// </summary>
public static partial class DisplayText
{
    public static string Words(Enum value) => Words(value.ToString());

    public static string Words(string identifier) =>
        string.IsNullOrEmpty(identifier)
            ? identifier
            : Boundary().Replace(identifier, match => " " + match.Value.ToLowerInvariant());

    /// <summary>
    /// The top-level fields of a JSON object as label and value, for showing
    /// audit detail to a person rather than as raw JSON. Ids are shortened
    /// (the full value stays in the record and the CSV); anything that is
    /// not a JSON object comes back as a single "Detail" field.
    /// </summary>
    public static IReadOnlyList<(string Label, string Value)> Fields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return [("Detail", json)];
            return document.RootElement.EnumerateObject()
                .Select(p => (Capitalise(Words(p.Name)), Value(p.Value)))
                .ToList();
        }
        catch (JsonException)
        {
            return [("Detail", json)];
        }
    }

    private static string Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => Shorten(value.GetString() ?? ""),
        JsonValueKind.Array => value.GetArrayLength() == 0 ? "none" : string.Join(", ", value.EnumerateArray().Select(Value)),
        JsonValueKind.Object => "…",
        JsonValueKind.Null => "—",
        JsonValueKind.True => "yes",
        JsonValueKind.False => "no",
        _ => value.GetRawText()
    };

    private static string Shorten(string text) => Guid.TryParse(text, out _) ? text[..8] + "…" : text;

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    [GeneratedRegex("(?<=[a-z0-9])[A-Z]")]
    private static partial Regex Boundary();
}
