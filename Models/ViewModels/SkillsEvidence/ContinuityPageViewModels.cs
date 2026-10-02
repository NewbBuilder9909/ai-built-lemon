using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Models.ViewModels.SkillsEvidence;

/// <summary>The key-person coverage page: the report, the people and actions it can reference, and what the viewer may do.</summary>
public sealed record ContinuityPlanPageViewModel(
    KeyPersonCoverageReport Report,
    IReadOnlyList<StaffProfile> Roster,
    IReadOnlyList<CoverageAction> Actions,
    bool CanManage,
    bool CanRecordDecision);

/// <summary>The suggestion queue, with names resolved inside the tenant.</summary>
public sealed record SuggestionsPageViewModel(
    IReadOnlyList<Suggestion> Suggestions,
    IReadOnlyList<StaffProfile> Roster,
    IReadOnlyDictionary<Guid, string> Names,
    bool CanDecide);

/// <summary>The evidence-collection basis page: readiness checks, the live decision and its superseded history.</summary>
public sealed record ProcessingDecisionPageViewModel(
    EnablementReadinessReport Readiness,
    EvidenceProcessingDecision? Live,
    IReadOnlyList<EvidenceProcessingDecision> History,
    DateOnly AsOf);
