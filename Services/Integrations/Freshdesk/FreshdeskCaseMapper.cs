using ProgrammePulse.Models.Integrations.Freshdesk.Raw;
using ProgrammePulse.Models.ServiceOps;

namespace ProgrammePulse.Services.Integrations.Freshdesk;

/// <summary>
/// The seam where Freshdesk Bronze DTOs and provider-neutral Silver
/// records both appear — the equivalent of ClickUpMappingService, and the
/// only type in this feature allowed to reference both.
///
/// Pure: no HTTP, no database, no clock beyond what it is handed. That is
/// what lets reopens, deletions, unknown component tags and unmapped
/// statuses be tested as data rather than against a live desk.
/// </summary>
public static class FreshdeskCaseMapper
{
    public const string ProviderName = DeskHostPolicy.FreshdeskProvider;

    /// <summary>
    /// Freshdesk's documented numeric statuses. A customer can add their
    /// own, which is exactly why an unrecognised value maps to
    /// <see cref="SupportCaseState.Active"/> with the raw value preserved
    /// rather than being dropped: a case in an unknown state is still an
    /// open case, and losing it would understate demand.
    /// </summary>
    public static SupportCaseState MapState(int? statusCode, bool isDeleted, bool isSpam)
    {
        if (isDeleted || isSpam)
        {
            return SupportCaseState.Withdrawn;
        }

        return statusCode switch
        {
            2 => SupportCaseState.Active,
            3 => SupportCaseState.Pending,
            4 => SupportCaseState.Resolved,
            5 => SupportCaseState.Closed,
            _ => SupportCaseState.Active
        };
    }

    public static SupportCasePriority MapPriority(int? priorityCode) => priorityCode switch
    {
        1 => SupportCasePriority.Low,
        2 => SupportCasePriority.Medium,
        3 => SupportCasePriority.High,
        4 => SupportCasePriority.Urgent,
        _ => SupportCasePriority.Unknown
    };

    /// <summary>
    /// Maps one ticket. <paramref name="approvedComponents"/> is the
    /// tenant's own list: a tag that is not on it leaves
    /// <see cref="SupportCaseFact.ComponentKey"/> null and survives only
    /// as <see cref="SupportCaseFact.RawComponentTag"/>, so the case
    /// counts towards demand but lands in the visible "unmapped
    /// component" bucket instead of inventing a category.
    /// </summary>
    public static SupportCaseFact Map(
        FreshdeskTicket ticket,
        DeskConnection connection,
        IReadOnlyCollection<string> approvedComponents,
        Guid? runKey,
        DateTime nowUtc)
    {
        var withdrawn = ticket.IsDeleted || ticket.IsSpam;
        var normalized = DeskHostPolicy.NormalizeComponent(ticket.ComponentTag);
        var component = normalized.Length > 0 && approvedComponents.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : null;

        // A reopen is worth recording even if the desk gave us only the
        // timestamp: it is the clearest signal a fix did not hold.
        var reopened = ticket.ReopenedAtUtc is not null;

        return new SupportCaseFact
        {
            CaseKey = Guid.NewGuid(),
            TenantId = connection.TenantId,
            ConnectionKey = connection.ConnectionKey,
            Provider = ProviderName,
            SourceAccountId = connection.SourceAccountId,
            ExternalTicketId = ticket.Id,
            CreatedAtUtc = ticket.CreatedAtUtc ?? nowUtc,
            UpdatedAtUtc = ticket.UpdatedAtUtc ?? ticket.CreatedAtUtc ?? nowUtc,
            ResolvedAtUtc = ticket.ResolvedAtUtc,
            ClosedAtUtc = ticket.ClosedAtUtc,
            ReopenedAtUtc = ticket.ReopenedAtUtc,
            State = MapState(ticket.StatusCode, ticket.IsDeleted, ticket.IsSpam),
            ProviderStatus = ticket.RawStatus,
            Priority = MapPriority(ticket.PriorityCode),
            ComponentKey = component,
            RawComponentTag = string.IsNullOrWhiteSpace(ticket.ComponentTag) ? null : ticket.ComponentTag.Trim(),
            CaseType = ticket.CaseType,
            SourceUrl = ticket.SourceUrl,
            IsReopened = reopened,
            IsWithdrawn = withdrawn,
            ObservedInRunKey = runKey,
            SchemaVersion = SupportCaseSchema.CurrentVersion,
            FirstIngestedAtUtc = nowUtc,
            IngestedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// The agent who restored service, as a participant with the
    /// <see cref="SupportCaseRole.Resolver"/> role. Null when the desk
    /// named nobody, or when the case was never resolved — an assignee on
    /// an open ticket has not resolved anything yet.
    ///
    /// <c>StaffKey</c> is deliberately left null here. It is set only by
    /// an approved <see cref="DeskAgentLink"/>, the same discipline as
    /// engineering evidence: a desk agent id is not a person until
    /// somebody says it is.
    /// </summary>
    public static SupportCaseParticipant? MapResolver(
        FreshdeskTicket ticket, DeskConnection connection, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(ticket.ResponderId) || ticket.ResolvedAtUtc is null)
        {
            return null;
        }

        return new SupportCaseParticipant
        {
            ParticipantKey = Guid.NewGuid(),
            TenantId = connection.TenantId,
            ConnectionKey = connection.ConnectionKey,
            ExternalTicketId = ticket.Id,
            ExternalAgentId = ticket.ResponderId!.Trim(),
            AgentDisplayName = ticket.ResponderName,
            Role = SupportCaseRole.Resolver,
            StaffKey = null,
            OccurredAtUtc = ticket.ResolvedAtUtc.Value,
            IngestedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// Relationship links suggested by issue keys the desk recorded
    /// against the ticket.
    ///
    /// These are <see cref="SupportLinkMethod.IssueKeyMatch"/> and
    /// nothing stronger. Two strings matching means the ticket and the
    /// change mention each other; it does not mean the change caused the
    /// ticket, and only a reviewer can say that.
    /// </summary>
    public static IReadOnlyList<SupportCodeLink> MapSuggestedLinks(
        FreshdeskTicket ticket, DeskConnection connection, DateTime nowUtc) =>
        [.. ticket.LinkedIssueKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key => new SupportCodeLink
            {
                LinkKey = Guid.NewGuid(),
                TenantId = connection.TenantId,
                ConnectionKey = connection.ConnectionKey,
                ExternalTicketId = ticket.Id,
                ArtifactType = LinkedArtifactType.Issue,
                ArtifactExternalId = key,
                ArtifactSource = null,
                ArtifactUrl = null,
                Method = SupportLinkMethod.IssueKeyMatch,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            })];
}
