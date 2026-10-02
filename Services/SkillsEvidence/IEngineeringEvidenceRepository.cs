using ProgrammePulse.Services.Shared;
using ProgrammePulse.Models.SkillsEvidence;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// Persistence for the engineering-evidence aggregates: connections,
/// evidence rows, actor links, the unmapped queue, coverage cursors and
/// Bronze captures. One repository across them, following the
/// ProgrammeRepository precedent — they are always read together.
///
/// Every method takes <c>tenantId</c> and applies it inside the query.
/// Two deliberate exceptions, both keyed on something that is already an
/// isolation boundary: <see cref="DeleteEvidenceForStaffAsync"/> and
/// <see cref="DeleteActorLinksForStaffAsync"/> act on a StaffKey during
/// GDPR erasure, and a StaffKey belongs to exactly one tenant. Same
/// carve-out, for the same reason, as
/// IIdentityResolutionRepository.DeleteLinksForStaffAsync.
/// </summary>
public interface IEngineeringEvidenceRepository
{
    // ---- Connections ----

    Task<IReadOnlyList<EvidenceConnection>> GetConnectionsAsync(Guid tenantId);

    Task<EvidenceConnection?> GetConnectionAsync(Guid connectionKey, Guid tenantId);

    Task<EvidenceConnection?> GetConnectionByAccountAsync(string provider, string sourceAccountId, Guid tenantId);

    Task<EvidenceConnection> UpsertConnectionAsync(EvidenceConnection connection);

    Task<EvidenceConnection> SetConnectionStatusAsync(
        Guid connectionKey, EvidenceConnectionStatus status, Guid tenantId, DateTime nowUtc, bool clearCredential);

    // ---- Evidence ----

    /// <summary>
    /// Idempotent upsert on (tenantId, connectionKey, sourceType,
    /// externalId, role). A replayed page updates in place — including
    /// clearing a <c>StaffKey</c> whose link has since been revoked — and
    /// preserves <c>FirstIngestedAtUtc</c> from the original row.
    /// </summary>
    Task<EngineeringEvidence> UpsertEvidenceAsync(EngineeringEvidence evidence);

    Task<IReadOnlyList<EngineeringEvidence>> GetEvidenceForStaffAsync(Guid staffKey, Guid tenantId);

    /// <summary>One page of a person's evidence, most recent first (finding A7).</summary>
    async Task<ResultPage<EngineeringEvidence>> GetEvidencePageForStaffAsync(Guid staffKey, Guid tenantId, PageRequest page) =>
        ResultPage<EngineeringEvidence>.Of(await GetEvidenceForStaffAsync(staffKey, tenantId), page);

    /// <summary>
    /// The totals a portfolio states over a person's whole record, computed
    /// without reading every artefact. The body is the definition; the SQL
    /// repository aggregates.
    /// </summary>
    async Task<EvidenceSummary> GetEvidenceSummaryForStaffAsync(Guid staffKey, Guid tenantId) =>
        EvidenceSummary.Of(await GetEvidenceForStaffAsync(staffKey, tenantId));

    Task<IReadOnlyList<EngineeringEvidence>> GetEvidenceForConnectionAsync(Guid connectionKey, Guid tenantId);

    Task<int> CountUnattributedAsync(Guid tenantId);

    /// <summary>
    /// Applies an approved link to evidence already ingested, so mapping an
    /// actor does not require a re-sync. Returns rows updated.
    /// </summary>
    Task<int> AttributeEvidenceToStaffAsync(Guid connectionKey, string externalActorId, Guid staffKey, Guid tenantId, DateTime nowUtc);

    /// <summary>
    /// Reverses the above when a link is revoked: rows go back to
    /// <see cref="EvidenceAttributionStatus.Unmapped"/> with a null
    /// StaffKey. The evidence is not deleted — it was really observed — it
    /// simply stops being anybody's.
    /// </summary>
    Task<int> DetachEvidenceFromStaffAsync(Guid connectionKey, string externalActorId, Guid tenantId, DateTime nowUtc);

    // ---- Actor links and the unmapped queue ----

    Task<IReadOnlyList<EvidenceActorLink>> GetActorLinksAsync(Guid connectionKey, Guid tenantId);

    Task<IReadOnlyList<EvidenceActorLink>> GetActorLinksForStaffAsync(Guid staffKey);

    Task<EvidenceActorLink> CreateActorLinkAsync(EvidenceActorLink link);

    Task DeleteActorLinkAsync(Guid linkKey, Guid tenantId);

    Task<IReadOnlyList<UnmappedEvidenceActor>> GetUnmappedActorsAsync(Guid tenantId, bool openOnly);

    /// <summary>One page of the open queue, in the same order as <see cref="GetUnmappedActorsAsync"/>.</summary>
    async Task<ResultPage<UnmappedEvidenceActor>> GetUnmappedActorsPageAsync(Guid tenantId, PageRequest page) =>
        ResultPage<UnmappedEvidenceActor>.Of(await GetUnmappedActorsAsync(tenantId, openOnly: true), page);

    /// <summary>How many actors are waiting to be identified, without reading them.</summary>
    async Task<int> CountOpenUnmappedActorsAsync(Guid tenantId, bool includeBots) =>
        (await GetUnmappedActorsAsync(tenantId, openOnly: true)).Count(a => includeBots || !a.IsBot);

    Task<UnmappedEvidenceActor?> GetUnmappedActorAsync(Guid unmappedActorKey, Guid tenantId);

    /// <summary>
    /// Upsert per sighting: a new sighting bumps the count and last-seen; a
    /// first sighting inserts. A row previously resolved but seen again is
    /// re-opened, because the link that resolved it evidently no longer
    /// covers this actor — that must be visible, not absorbed.
    /// </summary>
    Task<UnmappedEvidenceActor> RecordUnmappedSightingAsync(UnmappedEvidenceActor sighting);

    Task MarkUnmappedResolvedAsync(Guid unmappedActorKey, Guid tenantId, DateTime nowUtc);

    // ---- Coverage ----

    Task<IReadOnlyList<EvidenceCoverage>> GetCoverageAsync(Guid tenantId);

    Task<IReadOnlyList<EvidenceCoverage>> GetCoverageForConnectionAsync(Guid connectionKey, Guid tenantId);

    Task<EvidenceCoverage> UpsertCoverageAsync(EvidenceCoverage coverage);

    /// <summary>
    /// Marks every stream of a repository that is no longer in the
    /// selection, or of a disconnected connection, as
    /// <see cref="EvidenceCoverageStatus.OutOfScope"/>. Evidence is kept;
    /// only the claim to current coverage is withdrawn.
    /// </summary>
    Task<int> MarkCoverageOutOfScopeAsync(Guid connectionKey, IReadOnlyCollection<string> stillSelected, Guid tenantId, DateTime nowUtc);

    // ---- Bronze ----

    Task SaveRawAsync(Guid tenantId, Guid connectionKey, string provider, string sourceAccountId, string entityType, string externalId, string payloadJson, DateTime fetchedAtUtc);

    /// <summary>Retention purge of raw captures older than the cutoff; returns rows deleted.</summary>
    Task<int> PurgeRawBefore(DateTime cutoffUtc);

    // ---- GDPR ----

    Task<int> DeleteActorLinksForStaffAsync(Guid staffKey);

    /// <summary>
    /// Erasure. Deletes evidence rows attributed to this person outright —
    /// see SkillsEvidenceDataParticipant for why evidence, like assertions,
    /// is deleted rather than pseudonymised.
    /// </summary>
    Task<int> DeleteEvidenceForStaffAsync(Guid staffKey);

    /// <summary>
    /// Erasure of everything the subject's linked source accounts left behind,
    /// not only the rows already attributed to them: evidence under those
    /// accounts that was never attributed (it still carries their login and
    /// email), their entries in the unmapped-account queue, and the Bronze
    /// pages those artefacts came from. Call after the links are deleted, with
    /// the identities the links held. One transaction.
    /// </summary>
    Task<EvidenceErasureCounts> EraseSubjectTracesAsync(Guid staffKey, IReadOnlyList<(Guid TenantId, Guid ConnectionKey, string ExternalActorId)> identities);
}

/// <summary>
/// Totals over one person's whole evidence record: how many artefacts, how
/// many per role, the languages their changes touched, and the span of the
/// record. Counts only, deliberately not combined into anything.
/// </summary>
public sealed record EvidenceSummary(
    int Count,
    IReadOnlyDictionary<EvidenceRole, int> CountsByRole,
    IReadOnlyList<string> LanguageHints,
    DateTime? EarliestUtc,
    DateTime? LatestUtc)
{
    public static EvidenceSummary Of(IReadOnlyCollection<EngineeringEvidence> evidence) => new(
        evidence.Count,
        evidence.GroupBy(e => e.Role).ToDictionary(g => g.Key, g => g.Count()),
        [.. evidence.SelectMany(e => e.LanguageHints).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
        evidence.Count == 0 ? null : evidence.Min(e => e.OccurredAtUtc),
        evidence.Count == 0 ? null : evidence.Max(e => e.OccurredAtUtc));
}

/// <summary>What an erasure removed beyond the attributed rows, as counts only.</summary>
public sealed record EvidenceErasureCounts(int EvidenceRows, int UnmappedActors, int RawPages);
