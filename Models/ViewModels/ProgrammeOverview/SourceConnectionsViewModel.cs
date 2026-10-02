namespace ProgrammePulse.Models.ViewModels.ProgrammeOverview;

/// <summary>
/// The source connections page (/staffops/programme/connections): whether
/// this tenant has configured its own ClickUp/Hub Planner credential, or is
/// still using the deployment-wide fallback. The credential itself is never
/// read back — only whether one is set and, for ClickUp, which workspace.
/// </summary>
public sealed record SourceConnectionsViewModel(
    SourceConnectionRowViewModel ClickUp,
    SourceConnectionRowViewModel HubPlanner,
    string? Message);

public sealed record SourceConnectionRowViewModel(
    string DisplayName,
    bool HasOwnCredential,
    string? ExternalAccountId);
