using ProgrammePulse.Services.SecurityAssurance;

namespace ProgrammePulse.Models.ViewModels.SecurityAssurance;

/// <summary>/staffops/security: the tenant's scanning-tool connection and what it last read.</summary>
public sealed record SecurityToolsPageViewModel(
    SecurityConnectionSummary? Connection,
    SecurityAssuranceSnapshot Snapshot,
    IReadOnlyList<string> Regions);
