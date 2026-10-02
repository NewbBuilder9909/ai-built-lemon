using System.Text.Json;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Declares which repositories serve which projects (step 1 of
/// docs/delivery-evidence-and-contract-assurance.md). Estimated effort and
/// contract assurance both hang off these links, so they are declared by an
/// Admin with ManageCustomers, audited, and ended rather than deleted.
/// </summary>
public interface ICodeRepositoryLinkService
{
    /// <param name="knownRepositories">
    /// Repositories the evidence sources read, supplied by the caller: Programme
    /// Ops does not reach into the evidence area. Empty when that feature is off.
    /// </param>
    Task<CodeRepositoriesViewModel> BuildPageAsync(
        Guid tenantId, PageRequest page, IReadOnlyList<CodeRepositoryRef> knownRepositories, bool evidenceSourcesAvailable, string? message);

    Task<CommandOutcome> LinkAsync(
        Guid tenantId, string? provider, string? sourceAccountId, string? repositoryKey, Guid projectKey, string? note,
        Guid? actorStaffKey, int? actorMemberId);

    Task<CommandOutcome> UnlinkAsync(Guid tenantId, Guid linkKey, Guid? actorStaffKey, int? actorMemberId);
}

public sealed class CodeRepositoryLinkService(
    ICodeRepositoryLinkRepository links,
    IProgrammeReadRepository programmes,
    IAuditLogRepository audit,
    TimeProvider timeProvider) : ICodeRepositoryLinkService
{
    public const string AuditEntityType = "CodeRepositoryLink";
    private const int MaxNoteLength = 512;

    public async Task<CodeRepositoriesViewModel> BuildPageAsync(
        Guid tenantId, PageRequest page, IReadOnlyList<CodeRepositoryRef> knownRepositories, bool evidenceSourcesAvailable, string? message)
    {
        var livePage = await links.GetLivePageAsync(tenantId, page);
        var allLive = await links.GetLiveAsync(tenantId);
        var projects = await programmes.GetProjectsAsync(tenantId);
        var programmesByKey = (await programmes.GetProgrammesAsync(tenantId)).ToDictionary(p => p.ProgrammeKey);
        var customerNames = (await programmes.GetCustomersAsync(tenantId)).ToDictionary(c => c.CustomerKey, c => c.Name);
        var projectsByKey = projects.ToDictionary(p => p.ProjectKey);

        string ProgrammeName(Project project) =>
            programmesByKey.TryGetValue(project.ProgrammeKey, out var programme) ? programme.Name : "(unknown programme)";

        string? CustomerName(Project project) =>
            programmesByKey.TryGetValue(project.ProgrammeKey, out var programme) && programme.CustomerKey is { } customerKey
                ? customerNames.GetValueOrDefault(customerKey)
                : null;

        var rows = livePage.Items.Select(link =>
        {
            var project = projectsByKey.GetValueOrDefault(link.ProjectKey);
            return new CodeRepositoryLinkRowViewModel(
                link.LinkKey,
                link.Repository,
                project?.Name ?? "(unknown project)",
                project is null ? "(unknown programme)" : ProgrammeName(project),
                project is null ? null : CustomerName(project),
                link.Note,
                link.LinkedAtUtc);
        }).ToList();

        var options = projects
            .Select(p => new ProjectOptionViewModel(p.ProjectKey,
                CustomerName(p) is { } customer ? $"{ProgrammeName(p)} › {p.Name} ({customer})" : $"{ProgrammeName(p)} › {p.Name}"))
            .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A repository the evidence reads but no project claims contributes to
        // no estimate and inherits no obligation: the gap an Admin must close.
        var unclaimed = knownRepositories
            .Where(known => !allLive.Any(link => link.Repository.SameRepositoryAs(known)))
            .DistinctBy(known => known.DisplayName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(known => known.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CodeRepositoriesViewModel(rows, livePage.Links, options, unclaimed, evidenceSourcesAvailable, message);
    }

    public async Task<CommandOutcome> LinkAsync(
        Guid tenantId, string? provider, string? sourceAccountId, string? repositoryKey, Guid projectKey, string? note,
        Guid? actorStaffKey, int? actorMemberId)
    {
        var repository = CodeRepositoryRef.TryCreate(provider, sourceAccountId, repositoryKey, out var error);
        if (repository is null)
        {
            return CommandOutcome.Invalid(error!);
        }

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote?.Length > MaxNoteLength)
        {
            return CommandOutcome.Invalid($"Keep the note under {MaxNoteLength} characters.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var link = new CodeRepositoryLink
        {
            LinkKey = Guid.NewGuid(),
            TenantId = tenantId,
            Repository = repository,
            ProjectKey = projectKey,
            Note = trimmedNote,
            LinkedByStaffKey = actorStaffKey,
            LinkedAtUtc = now,
        };

        // A project from another tenant throws CrossTenantReferenceException (a 404).
        if (!await links.TryCreateAsync(link))
        {
            return CommandOutcome.Invalid($"{repository.DisplayName} is already linked to that project.");
        }

        await audit.LogAsync(AuditEntityType, link.LinkKey.ToString(), "Linked", actorMemberId,
            JsonSerializer.Serialize(new
            {
                repository.Provider,
                repository.SourceAccountId,
                repository.RepositoryKey,
                projectKey,
                note = trimmedNote
            }),
            now, tenantId);

        return CommandOutcome.Ok;
    }

    public async Task<CommandOutcome> UnlinkAsync(Guid tenantId, Guid linkKey, Guid? actorStaffKey, int? actorMemberId)
    {
        var link = await links.GetLiveByKeyAsync(linkKey, tenantId);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (link is null || !await links.EndAsync(linkKey, tenantId, actorStaffKey, now))
        {
            return CommandOutcome.Invalid("That link has already been removed.");
        }

        await audit.LogAsync(AuditEntityType, linkKey.ToString(), "Unlinked", actorMemberId,
            JsonSerializer.Serialize(new
            {
                link.Repository.Provider,
                link.Repository.SourceAccountId,
                link.Repository.RepositoryKey,
                link.ProjectKey
            }),
            now, tenantId);

        return CommandOutcome.Ok;
    }
}
