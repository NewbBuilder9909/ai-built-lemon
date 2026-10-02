using ProgrammePulse.Models.ViewModels.Staff;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.ServiceOps;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.Audit;

/// <summary>
/// The Admin audit page: one tenant's recent entries from the separate
/// per-area audit trails (Staff Ops, Programme Ops, Skills and Evidence,
/// Service Ops), merged newest first. Actor ids resolve to names only for
/// members of that tenant.
///
/// A deliberately top-level area. It reads four feature areas, and none of
/// them may depend on it. Putting the merge in Staff would have created a
/// Staff ⇄ ProgrammeOps cycle, which FeatureAreaDependencyTests refuses.
/// A single platform audit contract is architecture review Phase 4 (D2).
/// </summary>
public interface IAuditTrailQueryService
{
    Task<AuditTrailPageViewModel> GetPageAsync(Guid tenantId, PageRequest page);
}

public sealed class AuditTrailQueryService(
    IStaffRepository staffRepository,
    IStaffAuditLogRepository staffAuditLogRepository,
    IAuditLogRepository programmeAuditLogRepository,
    ISkillsEvidenceAuditLogRepository skillsEvidenceAuditLogRepository,
    IServiceOpsRepository serviceOpsRepository) : IAuditTrailQueryService
{
    /// <summary>
    /// How far back the merged trail can be paged. Page n has to read the
    /// newest skip + size rows of every trail, so the cost of a page grows
    /// with its depth; past this, the page says older entries exist.
    /// </summary>
    public const int DepthLimit = 2000;

    public async Task<AuditTrailPageViewModel> GetPageAsync(Guid tenantId, PageRequest page)
    {
        var lastPage = Math.Max(1, (DepthLimit + page.Size - 1) / page.Size);
        if (page.Number > lastPage)
        {
            page = new PageRequest(lastPage, page.Size);
        }

        // One row past the page to know a next page exists, and one past the
        // depth limit to know whether the limit hid anything.
        var merged = await GetRecentAsync(tenantId, Math.Min(page.Skip + page.Fetch, DepthLimit + 1));
        var entries = ResultPage<AuditEntryRowViewModel>.Of(merged.Take(DepthLimit), page);
        return new AuditTrailPageViewModel(entries, merged.Count > DepthLimit && !entries.HasNext, DepthLimit);
    }

    private async Task<IReadOnlyList<AuditEntryRowViewModel>> GetRecentAsync(Guid tenantId, int take)
    {
        var roster = await staffRepository.GetByTenantAsync(tenantId);
        var namesByMemberId = roster.ToDictionary(s => s.MemberId, s => s.FullName);

        var staffEntries = await staffAuditLogRepository.GetRecentAsync(take, tenantId);
        var programmeEntries = await programmeAuditLogRepository.GetRecentAsync(take, tenantId);

        // Skills, evidence and service audit rows were being written and
        // never read — the reviewed judgements that most need an audit
        // trail (a skill validation, an identity mapping, a confirmed
        // root cause, a recorded lawful basis) were invisible here.
        var skillsEntries = await skillsEvidenceAuditLogRepository.GetRecentAsync(take, tenantId);
        var serviceEntries = await serviceOpsRepository.GetRecentAuditAsync(take, tenantId);

        return staffEntries
            .Select(e => new AuditEntryRowViewModel("Staff Ops", e.EntityType, e.EntityId, e.Action, ActorName(e.ActorMemberId, namesByMemberId), e.DetailJson, e.TimestampUtc))
            .Concat(programmeEntries
                .Select(e => new AuditEntryRowViewModel("Programme Ops", e.EntityType, e.EntityId, e.Action, ActorName(e.ActorMemberId, namesByMemberId), e.DetailJson, e.TimestampUtc)))
            .Concat(skillsEntries
                .Select(e => new AuditEntryRowViewModel("Skills & Evidence", e.EntityType, e.EntityId, e.Action, ActorName(e.ActorMemberId, namesByMemberId), e.DetailJson, e.TimestampUtc)))
            .Concat(serviceEntries
                .Select(e => new AuditEntryRowViewModel("Service Ops", e.EntityType, e.EntityId, e.Action, ActorName(e.ActorMemberId, namesByMemberId), e.DetailJson, e.TimestampUtc)))
            .OrderByDescending(r => r.TimestampUtc)
            // Tie-break so rows sharing a timestamp keep one order across pages.
            .ThenBy(r => r.Source, StringComparer.Ordinal)
            .ThenBy(r => r.EntityType, StringComparer.Ordinal)
            .ThenBy(r => r.EntityId, StringComparer.Ordinal)
            .ThenBy(r => r.Action, StringComparer.Ordinal)
            .Take(take)
            .ToList();
    }

    private static string ActorName(int? actorMemberId, IReadOnlyDictionary<int, string> namesByMemberId) =>
        actorMemberId is null
            ? "system"
            : namesByMemberId.TryGetValue(actorMemberId.Value, out var name) ? name : $"member #{actorMemberId}";
}
