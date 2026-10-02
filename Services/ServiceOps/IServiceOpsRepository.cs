using ProgrammePulse.Models.ServiceOps;

namespace ProgrammePulse.Services.ServiceOps;

/// <summary>
/// Persistence for the Service Ops aggregates: desk connections, support
/// case facts, code links, case participants, agent links, coverage
/// cursors, Bronze captures and this area's audit trail. One repository
/// across them, following the ProgrammeRepository precedent.
///
/// Every method takes <c>tenantId</c> and applies it inside the query.
/// The two GDPR deletes are keyed on a StaffKey instead, which is already
/// an isolation boundary — the same carve-out, for the same reason, as
/// IEngineeringEvidenceRepository.
/// </summary>
public interface IServiceOpsRepository
{
    // ---- Connections ----

    Task<IReadOnlyList<DeskConnection>> GetConnectionsAsync(Guid tenantId);

    Task<DeskConnection?> GetConnectionAsync(Guid connectionKey, Guid tenantId);

    Task<DeskConnection?> GetConnectionByAccountAsync(string provider, string sourceAccountId, Guid tenantId);

    Task<DeskConnection> UpsertConnectionAsync(DeskConnection connection);

    Task<DeskConnection> SetConnectionStatusAsync(
        Guid connectionKey, DeskConnectionStatus status, Guid tenantId, DateTime nowUtc, bool clearCredential);

    // ---- Cases ----

    /// <summary>
    /// Idempotent upsert on (tenantId, connectionKey, externalTicketId),
    /// preserving <c>FirstIngestedAtUtc</c>. This is what makes the
    /// deliberate <c>updated_since</c> overlap free rather than
    /// duplicating demand.
    /// </summary>
    Task<SupportCaseFact> UpsertCaseAsync(SupportCaseFact fact);

    Task<SupportCaseFact?> GetCaseAsync(Guid connectionKey, string externalTicketId, Guid tenantId);

    /// <summary>Cases whose <c>createdAtUtc</c> falls in the window. Withdrawn rows are included; the caller excludes them.</summary>
    Task<IReadOnlyList<SupportCaseFact>> GetCasesCreatedBetweenAsync(Guid tenantId, DateTime fromUtc, DateTime toUtc);

    /// <summary>
    /// Marks a case withdrawn without discarding it. A deleted ticket
    /// that simply vanished would change last month's figures
    /// retrospectively, which is worse than showing it as withdrawn.
    /// </summary>
    Task<int> MarkCaseWithdrawnAsync(Guid connectionKey, string externalTicketId, Guid tenantId, DateTime nowUtc);

    // ---- Links ----

    Task<IReadOnlyList<SupportCodeLink>> GetLinksForCaseAsync(Guid connectionKey, string externalTicketId, Guid tenantId);

    Task<IReadOnlyList<SupportCodeLink>> GetLinksAsync(Guid tenantId);

    Task<SupportCodeLink?> GetLinkAsync(Guid linkKey, Guid tenantId);

    /// <summary>
    /// Upsert on the link identity. An automatic key match must never
    /// overwrite a reviewed verdict — that rule lives in
    /// SupportCodeLinkService, which is the only caller that creates
    /// unreviewed links.
    /// </summary>
    Task<SupportCodeLink> UpsertLinkAsync(SupportCodeLink link);

    Task DeleteLinkAsync(Guid linkKey, Guid tenantId);

    // ---- Participants and agents ----

    Task<IReadOnlyList<SupportCaseParticipant>> GetParticipantsForCaseAsync(Guid connectionKey, string externalTicketId, Guid tenantId);

    Task<IReadOnlyList<SupportCaseParticipant>> GetParticipantsForStaffAsync(Guid staffKey, Guid tenantId);

    Task<SupportCaseParticipant> UpsertParticipantAsync(SupportCaseParticipant participant);

    Task<IReadOnlyList<DeskAgentLink>> GetAgentLinksAsync(Guid connectionKey, Guid tenantId);

    Task<IReadOnlyList<DeskAgentLink>> GetAgentLinksForStaffAsync(Guid staffKey);

    Task<DeskAgentLink> CreateAgentLinkAsync(DeskAgentLink link);

    Task DeleteAgentLinkAsync(Guid linkKey, Guid tenantId);

    /// <summary>Applies an approved agent link to participation already ingested. Returns rows updated.</summary>
    Task<int> AttributeParticipationAsync(Guid connectionKey, string externalAgentId, Guid staffKey, Guid tenantId, DateTime nowUtc);

    Task<int> DetachParticipationAsync(Guid connectionKey, string externalAgentId, Guid tenantId, DateTime nowUtc);

    /// <summary>Distinct agent ids seen in participation with no approved link — the identification backlog.</summary>
    Task<IReadOnlyList<(string ExternalAgentId, string? DisplayName, int Cases)>> GetUnmappedAgentsAsync(Guid tenantId);

    // ---- Coverage ----

    Task<IReadOnlyList<DeskCoverage>> GetCoverageAsync(Guid tenantId);

    Task<DeskCoverage?> GetCoverageAsync(Guid connectionKey, DeskStream stream, Guid tenantId);

    Task<DeskCoverage> UpsertCoverageAsync(DeskCoverage coverage);

    // ---- Bronze and audit ----

    Task SaveRawAsync(Guid tenantId, Guid connectionKey, string provider, string sourceAccountId, string entityType, string externalId, string payloadJson, DateTime fetchedAtUtc);

    Task<int> PurgeRawBefore(DateTime cutoffUtc);

    Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId);

    Task<IReadOnlyList<ServiceOpsAuditLog>> GetRecentAuditAsync(int take, Guid tenantId);

    // ---- GDPR ----

    Task<int> DeleteAgentLinksForStaffAsync(Guid staffKey);

    /// <summary>
    /// Erasure. Detaches the subject from their case participation rather
    /// than deleting the rows: a case's resolution is a fact about the
    /// case and the service, and deleting it would silently rewrite the
    /// organisation's own incident history. What goes is the link between
    /// that work and a named person. See ServiceOpsDataParticipant.
    /// </summary>
    Task<int> DetachParticipationForStaffAsync(Guid staffKey, DateTime nowUtc);

    /// <summary>
    /// Erasure: blanks the display name on participation rows under the
    /// subject's agent accounts that were never attributed to them, which would
    /// otherwise keep their name after the attributed rows were detached.
    /// </summary>
    Task<int> ScrubAgentNamesAsync(IReadOnlyList<(Guid TenantId, Guid ConnectionKey, string ExternalAgentId)> agents, DateTime nowUtc);
}
