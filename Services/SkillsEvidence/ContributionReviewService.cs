using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.SkillsEvidence;

public sealed class ContributionReviewService(
    IContributionReviewRepository reviews,
    ISkillsEvidenceRepository skills,
    IStaffRepository staff,
    IEvidencePortfolioQueryService portfolios,
    TimeProvider clock)
{
    public async Task<ContributionExamplesPage> GetPageAsync(Guid subject, Guid tenantId, int page, int sourcePage = 1)
    {
        var person = await RequireStaffAsync(subject, tenantId);
        var rows = await reviews.GetPageAsync(subject, tenantId, page);
        var current = await skills.GetCurrentForStaffAsync(subject, tenantId);
        var names = (await skills.GetSkillsAsync(tenantId, true)).ToDictionary(s => s.SkillKey, s => s.Name);
        var sources = await reviews.GetSourcePageAsync(subject, tenantId, sourcePage);
        return new(person.StaffKey, person.FullName, page, rows.Take(IContributionReviewRepository.PageSize).ToArray(),
            rows.Count > IContributionReviewRepository.PageSize,
            current.Where(a => a.Status != AssertionStatus.Withdrawn).Select(a => new ContributionAssertionChoice(a.AssertionKey,
                names.GetValueOrDefault(a.SkillKey, a.SkillKey), a.Proficiency, a.Status)).ToArray(),
            sources.Items, await portfolios.BuildCoverageAsync(tenantId))
            { SourcePage = sources.Number, HasMoreSources = sources.HasNext,
                Form = new() { SourcePage = sources.Number } };
    }

    public async Task<ContributionExampleDetail> GetDetailAsync(Guid linkKey, Guid tenantId)
    {
        var link = await RequireLinkAsync(linkKey, tenantId);
        var person = await RequireStaffAsync(link.StaffKey, tenantId);
        var assertion = await skills.GetAssertionAsync(link.AssertionKey, tenantId);
        var skill = assertion is null ? null : await skills.GetSkillAsync(assertion.SkillKey, tenantId);
        var source = (await reviews.GetSourcesAsync(link.StaffKey, tenantId, [link.EvidenceKey])).SingleOrDefault();
        var history = await reviews.GetHistoryAsync(linkKey, tenantId);
        var actors = (await staff.GetByTenantAsync(tenantId)).ToDictionary(p => p.StaffKey, p => p.FullName);
        return new(link, person.FullName, assertion, skill?.Name, source, history.Take(50).ToArray(), history.Count > 50,
            await portfolios.BuildCoverageAsync(tenantId)) { ActorNames = actors };
    }

    public async Task<Guid> SubmitAsync(Guid subject, Guid tenantId, Guid assertionKey, Guid evidenceKey,
        string? demonstration, string? aiTool, AiAssistanceWorkflow workflow)
    {
        await RequireStaffAsync(subject, tenantId);
        await RequireReferencesAsync(subject, tenantId, assertionKey, evidenceKey);
        var note = Note(demonstration, "Explain what you demonstrated in this contribution.");
        var tool = Tool(aiTool, workflow);
        var link = new SkillContributionReview
        {
            LinkKey = Guid.NewGuid(), Revision = 1, TenantId = tenantId, StaffKey = subject,
            AssertionKey = assertionKey, EvidenceKey = evidenceKey, Status = ContributionReviewStatus.Submitted,
            DemonstrationNote = note, AiTool = tool, AiWorkflow = workflow,
            RecordedByStaffKey = subject, RecordedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        await reviews.AppendAsync(link, 0);
        return link.LinkKey;
    }

    public async Task ReviseAsync(Guid linkKey, Guid subject, Guid tenantId, int expectedRevision,
        string? demonstration, string? aiTool, AiAssistanceWorkflow workflow)
    {
        var link = await RequireOwnedAsync(linkKey, subject, tenantId, expectedRevision);
        await RequireReferencesAsync(subject, tenantId, link.AssertionKey, link.EvidenceKey);
        var next = link with
        {
            Revision = link.Revision + 1, Status = ContributionReviewStatus.Submitted,
            DemonstrationNote = Note(demonstration, "Explain the corrected contribution."),
            AiTool = Tool(aiTool, workflow), AiWorkflow = workflow, DecisionNote = null,
            RecordedByStaffKey = subject, RecordedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        await reviews.AppendAsync(next, expectedRevision);
    }

    public async Task WithdrawAsync(Guid linkKey, Guid subject, Guid tenantId, int expectedRevision)
    {
        var link = await RequireOwnedAsync(linkKey, subject, tenantId, expectedRevision);
        if (link.Status == ContributionReviewStatus.Withdrawn)
            throw new SkillAssertionValidationException("This example has already been withdrawn.");
        // Withdrawal remains possible after a source is removed or an assertion superseded.
        await reviews.AppendAsync(link with
        {
            Revision = link.Revision + 1, Status = ContributionReviewStatus.Withdrawn, DecisionNote = null,
            RecordedByStaffKey = subject, RecordedAtUtc = clock.GetUtcNow().UtcDateTime
        }, expectedRevision);
    }

    public async Task DecideAsync(Guid linkKey, Guid reviewer, Guid tenantId, int expectedRevision,
        ContributionReviewStatus decision, string? rationale)
    {
        await RequireStaffAsync(reviewer, tenantId);
        var link = await RequireLinkAsync(linkKey, tenantId);
        if (link.StaffKey == reviewer) throw new SkillAssertionValidationException("You cannot review your own contribution example.");
        if (link.Revision != expectedRevision) throw new ContributionReviewConflictException();
        if (link.Status != ContributionReviewStatus.Submitted)
            throw new SkillAssertionValidationException("Only submitted examples can be reviewed. The subject can correct and resubmit an example.");
        if (decision is not (ContributionReviewStatus.Accepted or ContributionReviewStatus.Rejected))
            throw new SkillAssertionValidationException("Choose accept or reject.");
        await RequireStaffAsync(link.StaffKey, tenantId);
        await RequireReferencesAsync(link.StaffKey, tenantId, link.AssertionKey, link.EvidenceKey);
        await reviews.AppendAsync(link with
        {
            Revision = link.Revision + 1, Status = decision,
            DecisionNote = Note(rationale, "Explain why this example is relevant or why it is not."),
            RecordedByStaffKey = reviewer, RecordedAtUtc = clock.GetUtcNow().UtcDateTime
        }, expectedRevision);
        // Deliberately no ISkillAssertionService: accepting relevance never validates proficiency.
    }

    private async Task<StaffProfile> RequireStaffAsync(Guid key, Guid tenantId)
    {
        var person = await staff.GetByStaffKeyAsync(key);
        if (person?.TenantId != tenantId || !person.IsActive) throw new CrossTenantReferenceException("Staff", key);
        return person;
    }

    private async Task<SkillContributionReview> RequireLinkAsync(Guid key, Guid tenantId) =>
        await reviews.GetCurrentAsync(key, tenantId) ?? throw new CrossTenantReferenceException("Contribution", key);

    private async Task<SkillContributionReview> RequireOwnedAsync(Guid key, Guid subject, Guid tenantId, int revision)
    {
        await RequireStaffAsync(subject, tenantId);
        var link = await RequireLinkAsync(key, tenantId);
        if (link.StaffKey != subject) throw new CrossTenantReferenceException("Contribution", key);
        if (link.Revision != revision) throw new ContributionReviewConflictException();
        return link;
    }

    private async Task RequireReferencesAsync(Guid subject, Guid tenantId, Guid assertionKey, Guid evidenceKey)
    {
        var assertion = await skills.GetAssertionAsync(assertionKey, tenantId);
        if (assertion?.StaffKey != subject) throw new CrossTenantReferenceException("Assertion", assertionKey);
        if (!assertion.IsCurrent || assertion.Status == AssertionStatus.Withdrawn)
            throw new SkillAssertionValidationException("Choose a current skill assertion. Previous revisions retain their history.");
        if ((await reviews.GetSourcesAsync(subject, tenantId, [evidenceKey])).Count == 0)
            throw new CrossTenantReferenceException("Evidence", evidenceKey);
    }

    private static string Note(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new SkillAssertionValidationException(message);
        if (value.Trim().Length > 1000) throw new SkillAssertionValidationException("Keep the explanation to 1,000 characters.");
        return value.Trim();
    }

    private static string? Tool(string? value, AiAssistanceWorkflow workflow)
    {
        if (!Enum.IsDefined(workflow)) throw new SkillAssertionValidationException("Choose a recognised assistance workflow.");
        var tool = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (tool?.Length > 100) throw new SkillAssertionValidationException("Keep the tool name to 100 characters.");
        if (tool is null && workflow != AiAssistanceWorkflow.Unspecified)
            throw new SkillAssertionValidationException("Name the tool, or leave the workflow unspecified.");
        return tool;
    }
}

public sealed record ContributionAssertionChoice(Guid Key, string Name, ProficiencyLevel Proficiency, AssertionStatus Status);
public sealed record ContributionExamplesPage(Guid StaffKey, string StaffName, int Page,
    IReadOnlyList<SkillContributionReview> Examples, bool HasMore, IReadOnlyList<ContributionAssertionChoice> Assertions,
    IReadOnlyList<SkillContributionSource> Sources, EvidenceCoverageSummary Coverage)
{
    public bool IsOwn { get; init; }
    public int SourcePage { get; init; } = 1;
    public bool HasMoreSources { get; init; }
    public bool CanDeclare { get; init; }
    public Models.ViewModels.SkillsEvidence.ContributionExampleForm Form { get; init; } = new();
    public string? Error { get; init; }
}
public sealed record ContributionExampleDetail(SkillContributionReview Current, string StaffName,
    StaffSkillAssertion? Assertion, string? SkillName, SkillContributionSource? Source,
    IReadOnlyList<SkillContributionReview> History, bool HasOlderHistory, EvidenceCoverageSummary Coverage)
{
    public IReadOnlyDictionary<Guid, string> ActorNames { get; init; } = new Dictionary<Guid, string>();
    public bool CanDeclare { get; init; }
    public bool CanReview { get; init; }
    public bool IsOwn { get; init; }
    public Models.ViewModels.SkillsEvidence.ContributionExampleForm Form { get; init; } = new();
    public Models.ViewModels.SkillsEvidence.ContributionDecisionForm Decision { get; init; } = new();
    public string? Error { get; init; }
    public bool CanUseEvidence => Source is not null && Assertion is { IsCurrent: true } && Assertion.Status != AssertionStatus.Withdrawn;
}
