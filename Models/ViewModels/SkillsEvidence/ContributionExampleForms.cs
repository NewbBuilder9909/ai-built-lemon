using System.ComponentModel.DataAnnotations;
using ProgrammePulse.Models.SkillsEvidence;

namespace ProgrammePulse.Models.ViewModels.SkillsEvidence;

public sealed class ContributionExampleForm
{
    public Guid AssertionKey { get; set; }
    public Guid EvidenceKey { get; set; }
    public int Revision { get; set; }
    [Range(1, 10000)] public int SourcePage { get; set; } = 1;
    [Required, StringLength(1000)] public string? DemonstrationNote { get; set; }
    [StringLength(100)] public string? AiTool { get; set; }
    public AiAssistanceWorkflow AiWorkflow { get; set; }
}

public sealed class ContributionDecisionForm
{
    public int Revision { get; set; }
    public ContributionReviewStatus Status { get; set; }
    [Required, StringLength(1000)] public string? Note { get; set; }
}
