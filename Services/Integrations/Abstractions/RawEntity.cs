namespace ProgrammePulse.Services.Integrations.Abstractions;

/// <summary>
/// Pairs a deserialized Bronze DTO with the exact JSON text the source
/// returned for that individual entity, so Bronze capture never depends on
/// the typed DTO being a lossless model of the API response.
///
/// Source-neutral by design and therefore in Abstractions rather than in any
/// one vendor's folder: it was originally declared inside the ClickUp
/// namespace, which forced the Hub Planner client to take a `using` on a
/// competitor integration's namespace. That cross-vendor dependency is now
/// barred by ProgrammePulse.Tests/Architecture/SourceIndependenceTests.
/// </summary>
public sealed record RawEntity<T>(T Item, string RawJson);
