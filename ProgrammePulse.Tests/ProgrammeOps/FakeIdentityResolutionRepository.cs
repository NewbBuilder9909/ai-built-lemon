using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// In-memory IIdentityResolutionRepository with the same sighting-upsert
/// semantics as the NPoco one (keyed on tenant + source + user id + email,
/// count increments, resolved rows re-open when seen again). Every method
/// except the two age-based retention purges (DeleteUnresolvedNotSeenSinceAsync,
/// DeleteLinksForStaffAsync) filters/stamps by tenantId, same as the real
/// repository.
/// </summary>
public sealed class FakeIdentityResolutionRepository : IIdentityResolutionRepository
{
    public readonly List<ExternalIdentityLink> Links = [];
    public readonly List<UnresolvedIdentity> Unresolved = [];
    public DateTime? LastPurgeCutoff { get; private set; }

    public Task<IReadOnlyList<ExternalIdentityLink>> GetLinksAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<ExternalIdentityLink>>(Links.Where(l => l.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<ExternalIdentityLink>> GetLinksForSourceAsync(string externalSource, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<ExternalIdentityLink>>(
            Links.Where(l => l.ExternalSource == externalSource && l.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<ExternalIdentityLink>> GetLinksForStaffAsync(Guid staffKey) =>
        Task.FromResult<IReadOnlyList<ExternalIdentityLink>>(Links.Where(l => l.StaffKey == staffKey).ToList());

    public Task<ExternalIdentityLink> CreateLinkAsync(ExternalIdentityLink link, Guid tenantId)
    {
        var normalised = link with { TenantId = tenantId, Email = link.Email?.ToLowerInvariant() };
        var conflict = Links.FirstOrDefault(l => l.TenantId == tenantId && l.ExternalSource == normalised.ExternalSource
            && ((normalised.ExternalUserId is not null && l.ExternalUserId == normalised.ExternalUserId)
                || (normalised.Email is not null && string.Equals(l.Email, normalised.Email, StringComparison.OrdinalIgnoreCase))));
        if (conflict is not null)
        {
            throw new DuplicateIdentityLinkException(normalised.ExternalSource, normalised.ExternalUserId, normalised.Email, conflict.StaffKey);
        }

        Links.Add(normalised);
        return Task.FromResult(normalised);
    }

    public Task DeleteLinkAsync(Guid linkKey, Guid tenantId)
    {
        Links.RemoveAll(l => l.LinkKey == linkKey && l.TenantId == tenantId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UnresolvedIdentity>> GetUnresolvedAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<UnresolvedIdentity>>(
            Unresolved.Where(u => u.TenantId == tenantId && !u.IsResolved).OrderByDescending(u => u.OccurrenceCount).ToList());

    public Task<UnresolvedIdentity?> GetUnresolvedByKeyAsync(Guid unresolvedIdentityKey, Guid tenantId) =>
        Task.FromResult(Unresolved.FirstOrDefault(u => u.UnresolvedIdentityKey == unresolvedIdentityKey && u.TenantId == tenantId));

    public Task<UnresolvedIdentity> RecordSightingAsync(string externalSource, string? externalUserId, string? email, string? displayName, string context, DateTime nowUtc, Guid tenantId, Guid? suggestedStaffKey = null)
    {
        var normalisedEmail = email?.ToLowerInvariant();
        var existing = Unresolved.FirstOrDefault(u =>
            u.TenantId == tenantId && u.ExternalSource == externalSource && u.ExternalUserId == externalUserId && u.Email == normalisedEmail);
        UnresolvedIdentity row;
        if (existing is null)
        {
            row = new UnresolvedIdentity
            {
                UnresolvedIdentityKey = Guid.NewGuid(),
                TenantId = tenantId,
                ExternalSource = externalSource,
                ExternalUserId = externalUserId,
                Email = normalisedEmail,
                DisplayName = displayName,
                Context = context,
                FirstSeenUtc = nowUtc,
                LastSeenUtc = nowUtc,
                OccurrenceCount = 1,
                SuggestedStaffKey = suggestedStaffKey
            };
        }
        else
        {
            Unresolved.Remove(existing);
            row = existing with
            {
                OccurrenceCount = existing.OccurrenceCount + 1,
                LastSeenUtc = nowUtc,
                Context = context,
                DisplayName = displayName ?? existing.DisplayName,
                SuggestedStaffKey = suggestedStaffKey,
                ResolvedStaffKey = null,
                ResolvedAtUtc = null
            };
        }

        Unresolved.Add(row);
        return Task.FromResult(row);
    }

    public Task MarkResolvedAsync(Guid unresolvedIdentityKey, Guid staffKey, DateTime nowUtc, Guid tenantId)
    {
        var existing = Unresolved.FirstOrDefault(u => u.UnresolvedIdentityKey == unresolvedIdentityKey && u.TenantId == tenantId);
        if (existing is not null)
        {
            Unresolved.Remove(existing);
            Unresolved.Add(existing with { ResolvedStaffKey = staffKey, ResolvedAtUtc = nowUtc });
        }

        return Task.CompletedTask;
    }

    public Task<int> DeleteUnresolvedNotSeenSinceAsync(DateTime cutoffUtc)
    {
        LastPurgeCutoff = cutoffUtc;
        return Task.FromResult(Unresolved.RemoveAll(u => !u.IsResolved && u.LastSeenUtc < cutoffUtc));
    }

    public Task DeleteLinksForStaffAsync(Guid staffKey)
    {
        Links.RemoveAll(l => l.StaffKey == staffKey);
        return Task.CompletedTask;
    }
}
