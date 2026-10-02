namespace ProgrammePulse.Services.Integrations.Abstractions;

/// <summary>
/// A source that publishes into Silver from an upload rather than a
/// credentialed sync — today, file import. It has no <c>RunAsync</c> and no
/// "Sync now" button, which is why it is not an <see cref="ISyncSource"/>;
/// but it does leave a <c>ProgrammeOps_SyncRun</c> row under
/// <see cref="Name"/>, so the publication panel can report it like any other
/// source.
///
/// Without this, the Programme Overview told a customer who had just imported
/// their data that "no source has completed a publication yet, so coverage is
/// unknown", directly above the imported figures.
///
/// Registered as a plain instance in the composer (the one place allowed to
/// name a vendor), so the Gold-layer status service still never references a
/// vendor namespace — SourceIndependenceTests keeps holding.
/// </summary>
/// <param name="Name">Must equal the value the source writes to <c>SyncRun.Source</c>.</param>
/// <param name="DisplayName">Human label for the data-sources panel.</param>
public sealed record UploadSource(string Name, string DisplayName);
