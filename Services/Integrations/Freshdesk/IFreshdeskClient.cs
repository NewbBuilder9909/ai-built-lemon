using ProgrammePulse.Models.Integrations.Freshdesk.Raw;

namespace ProgrammePulse.Services.Integrations.Freshdesk;

/// <summary>
/// Read-only Freshdesk access. Bronze: the only layer that knows
/// Freshdesk's API shape.
///
/// **Why this is watermark pagination rather than a cursor.** Zendesk was
/// the design document's suggested reference adapter because it offers a
/// documented incremental *cursor* export. Freshdesk does not — it
/// offers <c>updated_since</c> plus page numbers. Two consequences the
/// interface has to expose rather than hide:
///
/// 1. The filter is second-granular, so several tickets can share the
///    boundary timestamp. Resuming exactly at the last watermark drops
///    whichever of them the previous page did not reach. The caller
///    therefore rewinds by a small overlap every run, which is safe only
///    because the upsert is idempotent.
/// 2. Page depth is capped. A desk with more history than one run's page
///    budget must advance the watermark and be reported **partial**, not
///    treated as finished.
///
/// Both are why <see cref="FreshdeskPage"/> carries a completeness flag
/// and a watermark instead of an opaque token.
/// </summary>
public interface IFreshdeskClient
{
    /// <summary>
    /// Confirms the credential and returns the account domain it is
    /// valid for, or null. Called at connect time so the admin sees which
    /// desk they actually connected.
    /// </summary>
    Task<string?> VerifyAsync(string apiBaseUrl, string apiToken, CancellationToken cancellationToken);

    /// <summary>
    /// Tickets updated at or after <paramref name="updatedSinceUtc"/>,
    /// oldest first. Null fetches from the start of the desk's history.
    /// </summary>
    Task<FreshdeskPage> GetTicketsUpdatedSinceAsync(
        string apiBaseUrl, string apiToken, DateTime? updatedSinceUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Deleted and spam tickets, which the main list excludes. Fetched as
    /// its own stream so a deletion is recorded as a withdrawal rather
    /// than showing up as a ticket that silently stopped being updated.
    /// </summary>
    Task<FreshdeskPage> GetWithdrawnTicketsAsync(
        string apiBaseUrl, string apiToken, DateTime? updatedSinceUtc, CancellationToken cancellationToken);
}

/// <summary>
/// The credential no longer works, or lost the scope it needs. Distinct
/// from a transient failure: retrying will not help, so coverage is
/// marked <c>PermissionLost</c> and the admin is told.
/// </summary>
public sealed class DeskAccessLostException(string account)
    : InvalidOperationException($"The support desk connection can no longer read '{account}'.");
