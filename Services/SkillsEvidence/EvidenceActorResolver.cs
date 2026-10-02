using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>One external actor as a Bronze mapper hands it over.</summary>
public sealed record EvidenceActorSighting(
    string? ExternalActorId,
    string? Login,
    string? DisplayName,
    string? Email,
    string? AccountType);

/// <summary>What the resolver decided, and why.</summary>
public sealed record EvidenceActorResolution(
    Guid? StaffKey,
    EvidenceAttributionStatus Status,
    bool IsBot);

/// <summary>
/// "Which staff member is this external account?" — for engineering
/// evidence, and deliberately **not** the same answer as
/// <see cref="ProgrammeOps.IStaffIdentityResolver"/>.
///
/// That resolver used to let a matching email resolve the identity, on the
/// view that a wrong match only slightly misstates a capacity aggregate. It
/// now also treats an email match as a suggestion only (September 2026), but
/// the two stay separate: its links are per source, these per connection,
/// and the rule here was never a trade-off. Slice 2 states it
/// directly — "do not use display-name or email-only matching to publish a
/// staff profile" — because this evidence appears on a named individual's
/// record, and a wrong match credits one employee with another's work in a
/// system a manager may read before a career conversation.
///
/// So resolution here is: an approved <see cref="EvidenceActorLink"/> on
/// (tenant, connection, external actor id), or nothing. An email match is
/// downgraded to a *suggestion* on the queue row, which an admin still has
/// to approve. Two or more matches is
/// <see cref="UnmappedActorReason.Ambiguous"/> and never resolves; the
/// first match is never silently taken.
///
/// Scoped per request, so per sync run: links and staff are loaded once and
/// cached. The caches are not tenant-partitioned, so every call on one
/// instance must pass the same tenantId — true today, as a run is always
/// for one tenant. Same constraint, and the same reason, as the
/// ProgrammeOps resolver documents.
/// </summary>
public interface IEvidenceActorResolver
{
    /// <summary>
    /// Resolves and, when it cannot, records the sighting on the visible
    /// queue. Never returns a StaffKey that no human approved.
    /// </summary>
    Task<EvidenceActorResolution> ResolveAsync(
        EvidenceActorSighting sighting, Guid connectionKey, string provider, Guid tenantId, DateTime nowUtc);

    /// <summary>
    /// Distinct non-bot **accounts** this run could not attribute. Counts
    /// accounts, not sightings, because that is the number an admin has
    /// to work through on the queue.
    /// </summary>
    int UnmappedCount { get; }

    /// <summary>Distinct accounts refused because more than one staff member matched.</summary>
    int AmbiguousCount { get; }

    /// <summary>Distinct bot accounts excluded from person attribution.</summary>
    int BotCount { get; }

    /// <summary>
    /// Sightings with no id, no email and no login — nothing to key an
    /// approval on, so they can never reach the queue. Counted per
    /// sighting because there is by definition no way to tell how many
    /// people they represent, and kept separate from
    /// <see cref="UnmappedCount"/> so "N accounts unidentified" stays a
    /// true statement about N accounts. Same distinction, for the same
    /// reason, as ProgrammeOps' IStaffIdentityResolver.
    /// </summary>
    int UnidentifiableSightings { get; }
}

public sealed class EvidenceActorResolver(
    IEngineeringEvidenceRepository repository,
    IStaffRepository staffRepository) : IEvidenceActorResolver
{
    private Dictionary<string, Guid>? _linksByActorId;
    private ILookup<string, Guid>? _staffByEmail;
    private Guid _loadedForConnection;

    private readonly HashSet<string> _unmapped = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ambiguous = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _bots = new(StringComparer.OrdinalIgnoreCase);

    public int UnmappedCount => _unmapped.Count;
    public int AmbiguousCount => _ambiguous.Count;
    public int BotCount => _bots.Count;
    public int UnidentifiableSightings { get; private set; }

    public async Task<EvidenceActorResolution> ResolveAsync(
        EvidenceActorSighting sighting, Guid connectionKey, string provider, Guid tenantId, DateTime nowUtc)
    {
        var isBot = EvidenceActorClassifier.IsBot(sighting.Login, sighting.AccountType, sighting.Email);

        // An actor with no stable id cannot be mapped or queued — there is
        // nothing to key an approval on. A commit trailer with only a name
        // is the usual case. Counted as unmapped so the coverage figure
        // does not imply we attributed everything.
        var actorId = Normalize(sighting.ExternalActorId) ?? SyntheticIdFor(sighting);
        if (actorId is null)
        {
            // Counted separately, not as an unmapped account. Adding a
            // fresh key per sighting would make "N accounts
            // unidentified" — the number an admin sees and has to work
            // through — an overstatement of a queue they cannot clear,
            // because these can never reach it.
            UnidentifiableSightings++;
            return new EvidenceActorResolution(null, EvidenceAttributionStatus.Unmapped, isBot);
        }

        if (isBot)
        {
            _bots.Add(actorId);
            await QueueAsync(sighting, actorId, connectionKey, provider, tenantId, nowUtc,
                UnmappedActorReason.Bot, suggested: null, isBot: true);
            return new EvidenceActorResolution(null, EvidenceAttributionStatus.Bot, true);
        }

        await EnsureLoadedAsync(connectionKey, tenantId);

        if (_linksByActorId!.TryGetValue(actorId, out var approvedStaffKey))
        {
            return new EvidenceActorResolution(approvedStaffKey, EvidenceAttributionStatus.Mapped, false);
        }

        // No approved link. Everything below decides what the *queue row*
        // says, not whether this is attributed — it is not.
        var candidates = CandidatesByEmail(sighting.Email);

        if (candidates.Count > 1)
        {
            _ambiguous.Add(actorId);
            _unmapped.Add(actorId);
            await QueueAsync(sighting, actorId, connectionKey, provider, tenantId, nowUtc,
                UnmappedActorReason.Ambiguous, suggested: null, isBot: false);
            return new EvidenceActorResolution(null, EvidenceAttributionStatus.Ambiguous, false);
        }

        _unmapped.Add(actorId);
        await QueueAsync(sighting, actorId, connectionKey, provider, tenantId, nowUtc,
            candidates.Count == 1 ? UnmappedActorReason.EmailSuggestion : UnmappedActorReason.NoCandidate,
            suggested: candidates.Count == 1 ? candidates[0] : null,
            isBot: false);

        return new EvidenceActorResolution(null, EvidenceAttributionStatus.Unmapped, false);
    }

    /// <summary>
    /// A co-author trailer often carries an email and no provider account.
    /// Keying the queue row on the email lets repeated sightings collapse
    /// into one row an admin can approve once — the email is an
    /// *identifier* for queueing, never a resolution.
    /// </summary>
    private static string? SyntheticIdFor(EvidenceActorSighting sighting)
    {
        var email = Normalize(sighting.Email);
        if (email is not null)
        {
            return "email:" + email.ToLowerInvariant();
        }

        var login = Normalize(sighting.Login);
        return login is null ? null : "login:" + login.ToLowerInvariant();
    }

    private List<Guid> CandidatesByEmail(string? email)
    {
        var normalized = Normalize(email);
        return normalized is null ? [] : [.. _staffByEmail![normalized.ToLowerInvariant()]];
    }

    private async Task QueueAsync(
        EvidenceActorSighting sighting, string actorId, Guid connectionKey, string provider,
        Guid tenantId, DateTime nowUtc, UnmappedActorReason reason, Guid? suggested, bool isBot)
    {
        await repository.RecordUnmappedSightingAsync(new UnmappedEvidenceActor
        {
            UnmappedActorKey = Guid.NewGuid(),
            TenantId = tenantId,
            ConnectionKey = connectionKey,
            Provider = provider,
            ExternalActorId = actorId,
            ExternalLogin = sighting.Login,
            DisplayName = sighting.DisplayName,
            Email = sighting.Email,
            IsBot = isBot,
            Reason = reason,
            SuggestedStaffKey = suggested,
            OccurrenceCount = 1,
            FirstSeenUtc = nowUtc,
            LastSeenUtc = nowUtc
        });
    }

    private async Task EnsureLoadedAsync(Guid connectionKey, Guid tenantId)
    {
        if (_linksByActorId is not null && _loadedForConnection == connectionKey)
        {
            return;
        }

        var links = await repository.GetActorLinksAsync(connectionKey, tenantId);
        _linksByActorId = links
            .GroupBy(l => l.ExternalActorId, StringComparer.OrdinalIgnoreCase)
            // The unique index makes a duplicate impossible; First is a
            // defensive choice, not a silent tie-break.
            .ToDictionary(g => g.Key, g => g.First().StaffKey, StringComparer.OrdinalIgnoreCase);
        _loadedForConnection = connectionKey;

        if (_staffByEmail is null)
        {
            var staff = await staffRepository.GetByTenantAsync(tenantId);
            _staffByEmail = staff
                .Where(s => s.IsActive && !string.IsNullOrWhiteSpace(s.Email))
                .ToLookup(s => s.Email.Trim().ToLowerInvariant(), s => s.StaffKey, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
