using ProgrammePulse.Services.Shared;
using System.Text.Json;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// The identity queue: people the syncs saw but could not match to a staff
/// profile, and the explicit links that resolve them. Moved out of
/// StaffIdentityController. Each command returns the sentence the person sees
/// afterwards, success or refusal, because every outcome here is explained
/// rather than silent.
/// </summary>
public interface IIdentityQueueService
{
    Task<IdentityQueueViewModel> BuildAsync(Guid tenantId, string? message, PageRequest page);

    Task<string> LinkAsync(Guid tenantId, Guid unresolvedIdentityKey, Guid staffKey, int? actorMemberId);

    /// <summary>
    /// Links a queue row to the staff profile its email matched, after
    /// checking that the match still holds: the profile is active in this
    /// tenant and still has that email. The one-click path for the common case.
    /// </summary>
    Task<string> ApproveSuggestionAsync(Guid tenantId, Guid unresolvedIdentityKey, int? actorMemberId);

    Task<string> UnlinkAsync(Guid tenantId, Guid linkKey, int? actorMemberId);
}

public sealed class IdentityQueueService(
    IIdentityResolutionRepository identityRepository,
    IStaffRepository staffRepository,
    IAuditLogRepository auditLogRepository,
    ISourceConnectionRepository sourceConnectionRepository,
    TimeProvider timeProvider) : IIdentityQueueService
{
    public const string AuditEntityType = "IdentityLink";

    public async Task<IdentityQueueViewModel> BuildAsync(Guid tenantId, string? message, PageRequest page)
    {
        var unresolved = await identityRepository.GetUnresolvedPageAsync(tenantId, page);
        var links = await identityRepository.GetLinksAsync(tenantId);
        var staff = await staffRepository.GetByTenantAsync(tenantId);
        var staffByKey = staff.ToDictionary(s => s.StaffKey);

        return new IdentityQueueViewModel(
            unresolved.Items.Select(u =>
            {
                // A suggestion is offered only while it would still pass ApproveSuggestionAsync.
                var suggested = u.SuggestedStaffKey is { } key && staffByKey.TryGetValue(key, out var match) && StillMatches(match, u) ? match : null;
                return new UnresolvedIdentityRowViewModel(
                    u.UnresolvedIdentityKey, u.ExternalSource, u.ExternalUserId, u.Email, u.DisplayName, u.Context,
                    u.OccurrenceCount, u.FirstSeenUtc, u.LastSeenUtc, suggested?.StaffKey, suggested?.FullName);
            }).ToList(),
            links.Select(l => new IdentityLinkRowViewModel(
                l.LinkKey, l.ExternalSource, l.ExternalUserId, l.Email,
                staffByKey.TryGetValue(l.StaffKey, out var s) ? s.FullName : "(unknown staff)",
                l.CreatedAtUtc)).ToList(),
            staff.Where(s => s.IsActive).OrderBy(s => s.FullName)
                .Select(s => new StaffOptionViewModel(s.StaffKey, s.FullName, s.Email)).ToList(),
            message,
            unresolved.Links);
    }

    public async Task<string> LinkAsync(Guid tenantId, Guid unresolvedIdentityKey, Guid staffKey, int? actorMemberId)
    {
        var unresolved = await identityRepository.GetUnresolvedByKeyAsync(unresolvedIdentityKey, tenantId);
        if (unresolved is null)
        {
            return "That queue entry no longer exists.";
        }

        var staff = await staffRepository.GetByStaffKeyAsync(staffKey);
        if (staff is null || staff.TenantId != tenantId || !staff.IsActive)
        {
            return "Choose an active staff member to link to.";
        }

        return await CreateLinkAsync(tenantId, unresolved, staff, "Chosen", actorMemberId);
    }

    public async Task<string> ApproveSuggestionAsync(Guid tenantId, Guid unresolvedIdentityKey, int? actorMemberId)
    {
        var unresolved = await identityRepository.GetUnresolvedByKeyAsync(unresolvedIdentityKey, tenantId);
        if (unresolved is null)
        {
            return "That queue entry no longer exists.";
        }

        var staff = unresolved.SuggestedStaffKey is { } key ? await staffRepository.GetByStaffKeyAsync(key) : null;
        if (staff is null || staff.TenantId != tenantId || !StillMatches(staff, unresolved))
        {
            return $"The suggested match for {Describe(unresolved)} no longer holds. Choose a staff member to link to instead.";
        }

        return await CreateLinkAsync(tenantId, unresolved, staff, "ApprovedEmailMatch", actorMemberId);
    }

    /// <summary>An active profile whose email is still the one the source tool reported.</summary>
    private static bool StillMatches(StaffProfile staff, UnresolvedIdentity unresolved) =>
        staff.IsActive && unresolved.Email is not null
        && string.Equals(staff.Email?.Trim(), unresolved.Email, StringComparison.OrdinalIgnoreCase);

    private async Task<string> CreateLinkAsync(Guid tenantId, UnresolvedIdentity unresolved, StaffProfile staff, string method, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var connection = await sourceConnectionRepository.GetOrCreateActiveAsync(tenantId, unresolved.ExternalSource, externalAccountId: null, now);
        ExternalIdentityLink link;
        try
        {
            link = await identityRepository.CreateLinkAsync(new ExternalIdentityLink
            {
                LinkKey = Guid.NewGuid(),
                ConnectionKey = connection.ConnectionKey,
                ExternalSource = unresolved.ExternalSource,
                ExternalUserId = unresolved.ExternalUserId,
                Email = unresolved.Email,
                StaffKey = staff.StaffKey,
                CreatedByMemberId = actorMemberId,
                CreatedAtUtc = now
            }, tenantId);
        }
        catch (DuplicateIdentityLinkException ex)
        {
            var existingStaff = await staffRepository.GetByStaffKeyAsync(ex.ExistingStaffKey);
            return $"{Describe(unresolved)} is already linked to {existingStaff?.FullName ?? "another staff profile"}. Remove that link under \"Approved identity links\" first if it is wrong.";
        }

        await identityRepository.MarkResolvedAsync(unresolved.UnresolvedIdentityKey, staff.StaffKey, now, tenantId);

        await auditLogRepository.LogAsync(
            AuditEntityType,
            link.LinkKey.ToString(),
            "IdentityLinked",
            actorMemberId,
            JsonSerializer.Serialize(new { link.ExternalSource, link.ExternalUserId, link.Email, link.StaffKey, unresolved.UnresolvedIdentityKey, method }),
            now,
            tenantId);

        return $"Linked {Describe(unresolved)} to {staff.FullName}. The next sync will use this link.";
    }

    public async Task<string> UnlinkAsync(Guid tenantId, Guid linkKey, int? actorMemberId)
    {
        var existing = (await identityRepository.GetLinksAsync(tenantId)).FirstOrDefault(l => l.LinkKey == linkKey);
        if (existing is null)
        {
            return "That link no longer exists.";
        }

        await identityRepository.DeleteLinkAsync(linkKey, tenantId);

        await auditLogRepository.LogAsync(
            AuditEntityType,
            linkKey.ToString(),
            "IdentityUnlinked",
            actorMemberId,
            JsonSerializer.Serialize(new { existing.ExternalSource, existing.ExternalUserId, existing.Email, existing.StaffKey }),
            timeProvider.GetUtcNow().UtcDateTime,
            tenantId);

        return "Link removed. If that person is still in the source tool they will reappear in the queue on the next sync.";
    }

    private static string Describe(UnresolvedIdentity identity) =>
        identity.DisplayName ?? identity.Email ?? $"{identity.ExternalSource} user {identity.ExternalUserId}";
}
