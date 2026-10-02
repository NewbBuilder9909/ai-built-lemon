using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Models.ViewModels.SkillsEvidence;

/// <summary>The evidence sources page: connections, coverage, and whether the GitHub App is configured here.</summary>
public sealed record EvidenceConnectionsPageViewModel(
    IReadOnlyList<EvidenceConnection> Connections,
    EvidenceCoverageSummary Coverage,
    bool AppConfigured,
    IReadOnlyList<ProgrammePulse.Models.ViewModels.ProgrammeOverview.SourcePublicationStateViewModel>? RunStates = null);

/// <summary>The identity-mapping queue: unidentified source accounts and who they could be mapped to.</summary>
public sealed record EvidenceActorsPageViewModel(
    IReadOnlyList<UnmappedEvidenceActor> Unmapped,
    IReadOnlyList<StaffProfile> Roster,
    IReadOnlyList<EvidenceConnection> Connections,
    ProgrammePulse.Services.Shared.PageLinks UnmappedPages,
    int UnidentifiedPeople);
