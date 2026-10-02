namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// "Which StaffProfile is this external person?" — the one answer both
/// Bronze mappers (ClickUp assignees / time-entry users, Hub Planner
/// booking resources) ask. Resolution order:
/// <list type="number">
/// <item>an explicit <see cref="Models.Programme.ExternalIdentityLink"/> on (source, external user id);</item>
/// <item>an explicit link on (source, email);</item>
/// <item>otherwise null, and the sighting is recorded as an
/// <see cref="Models.Programme.UnresolvedIdentity"/> so it shows on the
/// identity queue instead of disappearing from capacity figures. When one
/// active StaffProfile has the same email (case-insensitive), that profile is
/// recorded as the row's suggestion for an Admin to approve in one click.</item>
/// </list>
/// An email match never attributes on its own: whoever can set an email in the
/// source tool would otherwise decide whose name the work goes under.
/// A step that yields <b>more than one</b> candidate (duplicate roster
/// emails, conflicting links) is ambiguous: it resolves to nobody, is
/// queued with the reason, and counts as unresolved — the first match is
/// never silently taken. Sightings with neither an id nor an email cannot
/// be queued but are counted (<see cref="UnidentifiableSightings"/>) so a
/// "0 unmatched" header is not a false claim of complete coverage.
/// Scoped per request (so per sync run): staff and links are loaded once
/// and cached for the lifetime of the instance — the caches are not
/// tenant-partitioned, so every ResolveAsync call on one instance must pass
/// the same tenantId (true today: a sync always runs for exactly one
/// tenant per request).
/// </summary>
public interface IStaffIdentityResolver
{
    Task<Guid?> ResolveAsync(string externalSource, string? externalUserId, string? email, string? displayName, string context, Guid tenantId);

    /// <summary>Distinct people this instance failed to resolve so far (including ambiguous ones) — reported in the sync result.</summary>
    int UnresolvedCount { get; }

    /// <summary>Distinct people this instance refused to resolve because more than one staff profile matched.</summary>
    int AmbiguousCount { get; }

    /// <summary>Distinct people left unresolved with a suggested email match awaiting an Admin's approval (included in <see cref="UnresolvedCount"/>).</summary>
    int SuggestedCount { get; }

    /// <summary>Sightings with neither an external user id nor an email — uncountable per person, so counted per sighting.</summary>
    int UnidentifiableSightings { get; }
}
