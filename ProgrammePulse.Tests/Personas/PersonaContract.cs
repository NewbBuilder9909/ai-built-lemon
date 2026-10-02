using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProgrammePulse.Tests.Personas;

/// <summary>
/// A persona journey contract loaded from TestScenarios/Northstar/personas.
///
/// These files declare *expected* behaviour. They are deliberately not the
/// source of authorization truth — Models/Staff/RoleCapabilities is, and
/// PersonaContractTests asserts the two agree. Keeping the expectation in a
/// separate, reviewable file is the point: a reviewer can read what each
/// class of user is supposed to be able to do without reading C#, and a
/// change to the application that quietly widens access fails against a file
/// nobody edited.
/// </summary>
public sealed record PersonaContract
{
    [JsonPropertyName("persona")] public required string Persona { get; init; }
    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }
    [JsonPropertyName("user")] public required string User { get; init; }
    [JsonPropertyName("memberGroup")] public required string MemberGroup { get; init; }
    [JsonPropertyName("summary")] public string? Summary { get; init; }
    [JsonPropertyName("can")] public required string[] Can { get; init; }
    [JsonPropertyName("cannot")] public required string[] Cannot { get; init; }

    public override string ToString() => Persona;
}

public sealed record CapabilityRoute
{
    [JsonPropertyName("method")] public required string Method { get; init; }
    [JsonPropertyName("path")] public required string Path { get; init; }

    /// <summary>The page a POST's antiforgery token is lifted from.</summary>
    [JsonPropertyName("form")] public string? Form { get; init; }

    [JsonPropertyName("fields")] public Dictionary<string, string>? Fields { get; init; }

    public bool IsPost => string.Equals(Method, "POST", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Method} {Path}";
}

public sealed record CapabilityRouteMapping
{
    [JsonPropertyName("capability")] public required string Capability { get; init; }
    [JsonPropertyName("routes")] public required CapabilityRoute[] Routes { get; init; }
    [JsonPropertyName("noRouteReason")] public string? NoRouteReason { get; init; }

    /// <summary>
    /// True while the route is still gated by a legacy Is…Async predicate
    /// rather than HasAsync. The matrix still asserts it — the capability map
    /// and the legacy predicate agree for these today — but it marks what is
    /// left to migrate.
    /// </summary>
    [JsonPropertyName("stillLegacyGate")] public bool StillLegacyGate { get; init; }
}

public sealed record CapabilityRouteFile
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; }
    [JsonPropertyName("capabilities")] public required CapabilityRouteMapping[] Capabilities { get; init; }
}

public static class PersonaContracts
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static string Directory => Path.Combine(AppContext.BaseDirectory, "TestScenarios", "Northstar");

    public static IReadOnlyList<PersonaContract> All()
    {
        var personaDirectory = Path.Combine(Directory, "personas");
        if (!System.IO.Directory.Exists(personaDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Persona contracts were not copied to the output directory: {personaDirectory}");
        }

        return [.. System.IO.Directory.GetFiles(personaDirectory, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(Load)];
    }

    private static PersonaContract Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<PersonaContract>(File.ReadAllText(path), Options)
                ?? throw new InvalidOperationException($"'{path}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"'{Path.GetFileName(path)}' is not valid persona JSON: {ex.Message}", ex);
        }
    }

    public static CapabilityRouteFile Routes()
    {
        var path = Path.Combine(Directory, "capability-routes.json");
        return JsonSerializer.Deserialize<CapabilityRouteFile>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException($"'{path}' deserialized to null.");
    }

    /// <summary>The seeded persona matching a contract, by email.</summary>
    public static PersonaDefinition Definition(PersonaContract contract) =>
        NorthstarPersonas.Signable.SingleOrDefault(p => string.Equals(p.Email, contract.User, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"Persona contract '{contract.Persona}' names user '{contract.User}', which no seeded persona matches.");
}
