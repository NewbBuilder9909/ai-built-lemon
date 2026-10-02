using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.ViewModels.Reporting;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// The RAID register (risks and issues against a project): the page's read
/// model and its four commands. Moved out of StaffReportingController so the
/// rules (trimming, an Open starting status, timestamps) live in one place
/// and can be tested without HTTP.
/// </summary>
public interface IRaidService
{
    Task<RaidViewModel> BuildAsync(Guid tenantId, Guid? programmeKey = null, Guid? customerKey = null);

    Task<CommandOutcome> CreateRiskAsync(Guid tenantId, Guid projectKey, string? title, string? description, SeverityLevel severity);

    Task SetRiskStatusAsync(Guid tenantId, Guid riskKey, RiskStatus status);

    Task<CommandOutcome> CreateIssueAsync(Guid tenantId, Guid projectKey, string? title, string? description, SeverityLevel severity);

    Task SetIssueStatusAsync(Guid tenantId, Guid issueKey, IssueStatus status);
}

public sealed class RaidService(IProgrammeRepository programmeRepository, TimeProvider timeProvider) : IRaidService
{
    public const string TitleRequired = "Enter a title before logging it.";

    public async Task<RaidViewModel> BuildAsync(Guid tenantId, Guid? programmeKey = null, Guid? customerKey = null)
    {
        var risks = await programmeRepository.GetRisksAsync(tenantId);
        var issues = await programmeRepository.GetIssuesAsync(tenantId);
        var projects = await programmeRepository.GetProjectsAsync(tenantId);
        var programmes = await programmeRepository.GetProgrammesAsync(tenantId);
        var scope = PortfolioScope.Resolve(programmes, await programmeRepository.GetCustomersAsync(tenantId), programmeKey, customerKey);
        if (programmeKey is not null || customerKey is not null)
        {
            var keys = programmes.Where(p => PortfolioScope.Includes(scope, p)).Select(p => p.ProgrammeKey).ToHashSet();
            projects = projects.Where(p => keys.Contains(p.ProgrammeKey)).ToList();
            var projectKeys = projects.Select(p => p.ProjectKey).ToHashSet();
            risks = risks.Where(r => projectKeys.Contains(r.ProjectKey)).ToList();
            issues = issues.Where(i => projectKeys.Contains(i.ProjectKey)).ToList();
        }
        var projectNamesByKey = projects.ToDictionary(p => p.ProjectKey, p => p.Name);

        var riskRows = risks
            .Select(r => new RiskRowViewModel(r.RiskKey, projectNamesByKey.GetValueOrDefault(r.ProjectKey, "(unknown project)"), r.Title, r.Description, r.Severity, r.Status))
            .OrderByDescending(r => r.Severity)
            .ToList();

        var issueRows = issues
            .Select(i => new IssueRowViewModel(i.IssueKey, projectNamesByKey.GetValueOrDefault(i.ProjectKey, "(unknown project)"), i.Title, i.Description, i.Severity, i.Status))
            .OrderByDescending(i => i.Severity)
            .ToList();

        var projectOptions = projects.Select(p => new ProjectOptionViewModel(p.ProjectKey, p.Name)).ToList();

        return new RaidViewModel(riskRows, issueRows, projectOptions) { Scope = scope };
    }

    public async Task<CommandOutcome> CreateRiskAsync(Guid tenantId, Guid projectKey, string? title, string? description, SeverityLevel severity)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return CommandOutcome.Invalid(TitleRequired);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await programmeRepository.UpsertRiskAsync(new Risk
        {
            RiskKey = Guid.NewGuid(),
            ProjectKey = projectKey,
            Title = title.Trim(),
            Description = Clean(description),
            Severity = severity,
            Status = RiskStatus.Open,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, tenantId);

        return CommandOutcome.Ok;
    }

    public async Task SetRiskStatusAsync(Guid tenantId, Guid riskKey, RiskStatus status)
    {
        // An unknown key is ignored, as it always was: the register is
        // re-rendered either way and a stale form must not error.
        var existing = await programmeRepository.GetRiskByKeyAsync(riskKey, tenantId);
        if (existing is not null)
        {
            await programmeRepository.UpsertRiskAsync(existing with { Status = status, UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime }, tenantId);
        }
    }

    public async Task<CommandOutcome> CreateIssueAsync(Guid tenantId, Guid projectKey, string? title, string? description, SeverityLevel severity)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return CommandOutcome.Invalid(TitleRequired);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await programmeRepository.UpsertIssueAsync(new Issue
        {
            IssueKey = Guid.NewGuid(),
            ProjectKey = projectKey,
            Title = title.Trim(),
            Description = Clean(description),
            Severity = severity,
            Status = IssueStatus.Open,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, tenantId);

        return CommandOutcome.Ok;
    }

    public async Task SetIssueStatusAsync(Guid tenantId, Guid issueKey, IssueStatus status)
    {
        var existing = await programmeRepository.GetIssueByKeyAsync(issueKey, tenantId);
        if (existing is not null)
        {
            await programmeRepository.UpsertIssueAsync(existing with { Status = status, UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime }, tenantId);
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
