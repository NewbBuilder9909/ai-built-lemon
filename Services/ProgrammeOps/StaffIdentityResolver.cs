using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class StaffIdentityResolver(
    IIdentityResolutionRepository identityRepository,
    IStaffRepository staffRepository,
    TimeProvider timeProvider) : IStaffIdentityResolver
{
    private Dictionary<string, List<StaffProfile>>? _staffByEmail;
    private readonly Dictionary<string, Dictionary<string, HashSet<Guid>>> _linksByUserIdPerSource = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, HashSet<Guid>>> _linksByEmailPerSource = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unresolvedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ambiguousKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _suggestedKeys = new(StringComparer.OrdinalIgnoreCase);
    private int _unidentifiableSightings;

    public int UnresolvedCount => _unresolvedKeys.Count;

    public int AmbiguousCount => _ambiguousKeys.Count;

    public int SuggestedCount => _suggestedKeys.Count;

    public int UnidentifiableSightings => _unidentifiableSightings;

    public async Task<Guid?> ResolveAsync(string externalSource, string? externalUserId, string? email, string? displayName, string context, Guid tenantId)
    {
        var userId = Normalise(externalUserId);
        var normalisedEmail = Normalise(email)?.ToLowerInvariant();

        if (userId is null && normalisedEmail is null)
        {
            // Nothing to key a queue row on, but it must still count: a
            // "0 unmatched" header is only honest if this is reported too.
            _unidentifiableSightings++;
            return null;
        }

        var (byUserId, byEmail) = await LinksForAsync(externalSource, tenantId);

        if (userId is not null && byUserId.TryGetValue(userId, out var linkedByUserId))
        {
            return await DecideAsync(linkedByUserId, externalSource, userId, normalisedEmail, displayName, context, "explicit links on this source user id", tenantId);
        }

        if (normalisedEmail is not null && byEmail.TryGetValue(normalisedEmail, out var linkedByEmail))
        {
            return await DecideAsync(linkedByEmail, externalSource, userId, normalisedEmail, displayName, context, "explicit links on this email", tenantId);
        }

        var staffByEmail = await StaffByEmailAsync(tenantId);
        if (normalisedEmail is not null && staffByEmail.TryGetValue(normalisedEmail, out var candidates))
        {
            // Duplicate roster emails are usually a leaver re-hired with a
            // new profile: one active profile among the duplicates is an
            // unambiguous answer. Two active ones (or only inactive ones)
            // are a decision for an admin, not a coin toss.
            var active = candidates.Where(c => c.IsActive).Select(c => c.StaffKey).ToHashSet();
            var keys = active.Count == 1 ? active : candidates.Select(c => c.StaffKey).ToHashSet();
            if (keys.Count == 1)
            {
                // A match, but only a suggestion. Anyone who can set an email
                // in the source tool could otherwise put their work under a
                // colleague's name (Aikido: business logic bypass), so the
                // person stays unresolved until an Admin approves it on the
                // identity queue, the rule the evidence area already follows.
                _suggestedKeys.Add(Key(externalSource, userId, normalisedEmail));
                await RecordUnresolvedAsync(externalSource, userId, normalisedEmail, displayName, context, tenantId, keys.First());
                return null;
            }

            return await DecideAsync(keys, externalSource, userId, normalisedEmail, displayName, context, "staff profiles share this email", tenantId);
        }

        await RecordUnresolvedAsync(externalSource, userId, normalisedEmail, displayName, context, tenantId);
        return null;
    }

    private async Task<Guid?> DecideAsync(HashSet<Guid> candidates, string externalSource, string? userId, string? normalisedEmail, string? displayName, string context, string ambiguityReason, Guid tenantId)
    {
        if (candidates.Count == 1)
        {
            return candidates.First();
        }

        // Ambiguous: never pick the first match. Queue it with the reason so
        // the admin can create the one explicit link that settles it.
        _ambiguousKeys.Add(Key(externalSource, userId, normalisedEmail));
        await RecordUnresolvedAsync(externalSource, userId, normalisedEmail, displayName, $"{context} — ambiguous: {candidates.Count} {ambiguityReason}", tenantId);
        return null;
    }

    private async Task RecordUnresolvedAsync(string externalSource, string? userId, string? normalisedEmail, string? displayName, string context, Guid tenantId, Guid? suggestedStaffKey = null)
    {
        _unresolvedKeys.Add(Key(externalSource, userId, normalisedEmail));
        await identityRepository.RecordSightingAsync(externalSource, userId, normalisedEmail, Normalise(displayName), context, timeProvider.GetUtcNow().UtcDateTime, tenantId, suggestedStaffKey);
    }

    private static string Key(string externalSource, string? userId, string? normalisedEmail) => $"{externalSource}|{userId}|{normalisedEmail}";

    private async Task<(Dictionary<string, HashSet<Guid>> ByUserId, Dictionary<string, HashSet<Guid>> ByEmail)> LinksForAsync(string externalSource, Guid tenantId)
    {
        if (_linksByUserIdPerSource.TryGetValue(externalSource, out var byUserId)
            && _linksByEmailPerSource.TryGetValue(externalSource, out var byEmail))
        {
            return (byUserId, byEmail);
        }

        byUserId = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        byEmail = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in await identityRepository.GetLinksForSourceAsync(externalSource, tenantId))
        {
            if (!string.IsNullOrWhiteSpace(link.ExternalUserId))
            {
                Add(byUserId, link.ExternalUserId, link.StaffKey);
            }

            if (!string.IsNullOrWhiteSpace(link.Email))
            {
                Add(byEmail, link.Email, link.StaffKey);
            }
        }

        _linksByUserIdPerSource[externalSource] = byUserId;
        _linksByEmailPerSource[externalSource] = byEmail;
        return (byUserId, byEmail);
    }

    private async Task<Dictionary<string, List<StaffProfile>>> StaffByEmailAsync(Guid tenantId)
    {
        if (_staffByEmail is not null)
        {
            return _staffByEmail;
        }

        _staffByEmail = new Dictionary<string, List<StaffProfile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var staff in await staffRepository.GetByTenantAsync(tenantId))
        {
            if (string.IsNullOrWhiteSpace(staff.Email))
            {
                continue;
            }

            if (!_staffByEmail.TryGetValue(staff.Email, out var list))
            {
                list = [];
                _staffByEmail[staff.Email] = list;
            }

            list.Add(staff);
        }

        return _staffByEmail;
    }

    private static void Add(Dictionary<string, HashSet<Guid>> index, string key, Guid staffKey)
    {
        if (!index.TryGetValue(key, out var set))
        {
            set = [];
            index[key] = set;
        }

        set.Add(staffKey);
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
