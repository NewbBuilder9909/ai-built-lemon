using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Models.ViewModels.ServiceOps;

/// <summary>Service health for a period, and what the viewer may do from it.</summary>
public sealed record ServiceHealthPageViewModel(ServiceHealthReport Report, bool CanReviewRootCause, bool CanManageDesk);

/// <summary>Connected support desks and each stream's coverage.</summary>
/// <param name="RunStates">Durable run state per desk source (last run, running, failed), from the shared sync-run table.</param>
public sealed record DesksPageViewModel(
    IReadOnlyList<DeskConnection> Connections,
    IReadOnlyList<DeskCoverage> Coverage,
    IReadOnlyList<ProgrammePulse.Models.ViewModels.ProgrammeOverview.SourcePublicationStateViewModel>? RunStates = null);

/// <summary>Case-to-code links awaiting or holding a reviewer's verdict.</summary>
public sealed record SupportLinksPageViewModel(
    IReadOnlyList<SupportCodeLink> Links,
    bool CanReviewRootCause,
    IReadOnlyList<DeskOption>? Desks = null);

/// <summary>
/// A desk as a form choice: its name, never its credential. Disconnected
/// desks stay listed because their imported cases can still be linked.
/// </summary>
public sealed record DeskOption(Guid ConnectionKey, string DisplayName, bool IsDisconnected);

/// <summary>Desk agents not yet mapped to a person, and who they could be mapped to.</summary>
public sealed record DeskAgentsPageViewModel(
    IReadOnlyList<(string ExternalAgentId, string? DisplayName, int Cases)> Unmapped,
    IReadOnlyList<StaffProfile> Roster,
    IReadOnlyList<DeskConnection> Connections);
