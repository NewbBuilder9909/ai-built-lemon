namespace ProgrammePulse.Models.Integrations.Freshdesk.Raw;

/// <summary>
/// Bronze shapes — the only place Freshdesk's vocabulary appears
/// alongside the fields this application needs. Everything above the
/// mapper sees Models/ServiceOps types, which
/// ServiceOpsProviderNeutralityTests enforces.
///
/// **No ticket body, no attachments, no requester.** Freshdesk's ticket
/// payload contains `description`, `description_text`, `requester_id`,
/// contact email and custom fields that may hold anything a customer
/// typed. None of it is represented here, so none of it can reach Silver
/// even by accident.
///
/// Field mapping to confirm during pilot setup, as the design document
/// requires: Freshdesk's numeric status and priority values, and which
/// field the customer actually uses for the product/component tag — it
/// may be `product_id`, `type`, `category`, a tag, or a custom field.
/// <see cref="ComponentTag"/> is whatever the adapter was configured to
/// read, and <see cref="RawStatus"/> keeps the original so a mis-mapping
/// is visible rather than silently baked in.
/// </summary>
public sealed record FreshdeskTicket(
    string Id,
    DateTime? CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    int? StatusCode,
    string? RawStatus,
    int? PriorityCode,
    string? ComponentTag,
    string? CaseType,
    bool IsDeleted,
    bool IsSpam,
    DateTime? ResolvedAtUtc,
    DateTime? ClosedAtUtc,
    DateTime? ReopenedAtUtc,
    string? ResponderId,
    string? ResponderName,
    IReadOnlyList<string> LinkedIssueKeys,
    string? SourceUrl);

/// <summary>
/// One page of tickets, plus whether the page was complete.
///
/// <paramref name="IsComplete"/> false means the run stopped early — a
/// rate limit, a failed page, or exhausting its page budget on a desk
/// with deep history. The caller must record partial coverage rather
/// than treating the end of the list as the end of the data.
///
/// <paramref name="HighWatermarkUtc"/> is the newest <c>updated_at</c>
/// seen, which becomes the next run's <c>updated_since</c> — after a
/// deliberate overlap, because Freshdesk's filter is second-granular and
/// ties at the boundary would otherwise be dropped.
/// </summary>
public sealed record FreshdeskPage(
    IReadOnlyList<FreshdeskTicket> Tickets,
    DateTime? HighWatermarkUtc,
    bool IsComplete,
    string? IncompleteReason = null);
