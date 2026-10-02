using System.Text.Json;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// This feature area's part in a subject access request and an erasure,
/// registered as <see cref="IStaffDataParticipant"/> by
/// SkillsEvidenceComposer — the same seam Programme Ops uses for identity
/// links, so the Staff domain never has to know this area exists.
///
/// **Export is tenant-scoped even though the interface is not.**
/// IStaffDataParticipant hands over a StaffKey and nothing else, so this
/// resolves the subject's own tenant from their StaffProfile and filters on
/// it. A StaffKey belongs to one tenant, so in a correct database the
/// filter is redundant — which is the reason to have it: the export must
/// not be the one place a mis-stamped row could cross the boundary. A
/// subject with no resolvable tenant exports nothing rather than
/// everything.
///
/// **Erasure deletes; it does not pseudonymise.** The rest of the Staff
/// domain keeps rows and blanks the PII, because cost and leave history
/// have to survive for financial and employment-law reasons. A proficiency
/// assertion has no such second purpose: detached from the person it is
/// meaningless, so there is nothing worth keeping and it goes outright,
/// history included. What that means downstream is stated rather than
/// hidden: the coverage view legitimately drops by one, and a component may
/// become "no reviewed cover". That is the truth for continuity planning —
/// the person is gone — and is preferable to leaving an anonymous ghost
/// maintainer that would make a real staffing gap invisible.
///
/// What survives is the audit trail, consistent with every other
/// <c>*_AuditLog</c> in this codebase (6-year retention, never edited). It
/// carries the now-pseudonymous StaffKey, the skill and the decision, but
/// never the free-text notes — those are on the assertion rows and go with
/// them. See docs/gdpr.md.
///
/// **Engineering evidence (Slice 2) follows the same rule**, and the
/// unattributed remainder is the interesting part. Erasure deletes the
/// subject's approved actor links and every evidence row attributed to
/// them. What is left behind is the *unattributed* evidence — rows whose
/// external account nobody had mapped, which were never personal data
/// about an identified person in this system and remain unidentified after
/// the erasure. That is the honest residue: the repository history still
/// records that a commit happened, this product simply no longer says who
/// it was. Bronze captures age out on their own retention window and are
/// not rewritten, because they are verbatim copies of what the provider
/// served.
/// </summary>
public sealed class SkillsEvidenceDataParticipant(
    ISkillsEvidenceRepository repository,
    IEngineeringEvidenceRepository evidenceRepository,
    ISuggestionRepository suggestionRepository,
    ISkillsEvidenceAuditLogRepository auditLog,
    IStaffRepository staffRepository) : IStaffDataParticipant
{
    public string Section => "Skill assertions";

    /// <summary>Second export section — kept apart so a reader can tell a claim from an observation.</summary>
    public const string EvidenceSection = "Engineering evidence";

    public async Task<IReadOnlyList<GdprExportLinkedRecordRow>> ExportAsync(Guid staffKey)
    {
        var tenantId = await ResolveSubjectTenantAsync(staffKey);
        if (tenantId is null)
        {
            return [];
        }

        var history = await repository.GetHistoryForStaffAsync(staffKey, tenantId.Value);

        var rows = history
            .Select(a => new GdprExportLinkedRecordRow(
                Section,
                Describe(a),
                a.RecordedAtUtc))
            .ToList();

        // The identity links that caused evidence to be attributed to this
        // person, then the evidence itself. Both are their data: the link
        // is a statement that an external account is them, and a subject
        // is entitled to see it and to dispute it.
        foreach (var link in await evidenceRepository.GetActorLinksForStaffAsync(staffKey))
        {
            rows.Add(new GdprExportLinkedRecordRow(
                EvidenceSection,
                $"{link.Provider} account {link.ExternalLogin ?? link.ExternalActorId} was approved as this person"
                + (link.ApprovedByStaffKey is null ? string.Empty : $" by staff {link.ApprovedByStaffKey}"),
                link.ApprovedAtUtc));
        }

        foreach (var evidence in await evidenceRepository.GetEvidenceForStaffAsync(staffKey, tenantId.Value))
        {
            rows.Add(new GdprExportLinkedRecordRow(
                EvidenceSection,
                $"{evidence.Provider} {evidence.SourceType} in {evidence.RepositoryKey} as {evidence.Role}"
                + $": {evidence.Title ?? evidence.ExternalId}"
                + (evidence.SourceUrl is null ? string.Empty : $" ({evidence.SourceUrl})"),
                evidence.OccurredAtUtc));
        }

        // Proposals about the subject are claims about them, even the
        // ones nobody accepted — arguably especially those, since a
        // dismissed suggestion records that the system inferred
        // something about them and a person disagreed.
        foreach (var suggestion in await suggestionRepository.GetForStaffAsync(staffKey, tenantId.Value))
        {
            rows.Add(new GdprExportLinkedRecordRow(
                EvidenceSection,
                $"Suggested '{suggestion.SubjectKey}' ({suggestion.Confidence}, {suggestion.Outcome}): {suggestion.Rationale}",
                suggestion.RaisedAtUtc));
        }

        return rows;
    }

    public async Task EraseAsync(Guid staffKey, DateTime nowUtc)
    {
        var tenantId = await ResolveSubjectTenantAsync(staffKey);

        var deleted = await repository.DeleteAllForStaffAsync(staffKey);

        // Links first, then evidence. If the order were reversed, a
        // concurrent sync could re-attribute freshly ingested rows to a
        // link that still existed — erasure has to remove the thing that
        // does the attributing before the things attributed.
        // The accounts the links held are read first: once the links are gone
        // nothing else says which source identities were this person's.
        var identities = (await evidenceRepository.GetActorLinksForStaffAsync(staffKey))
            .Select(l => (l.TenantId, l.ConnectionKey, l.ExternalActorId))
            .ToList();
        var linksDeleted = await evidenceRepository.DeleteActorLinksForStaffAsync(staffKey);

        // Not only the attributed rows: unattributed evidence under those
        // accounts still carries their login and email, as do the unmapped
        // queue and the Bronze pages (Aikido: incomplete data deletion).
        var traces = await evidenceRepository.EraseSubjectTracesAsync(staffKey, identities);
        var evidenceDeleted = traces.EvidenceRows;

        // Proposals about the subject go with the evidence they were
        // derived from — a suggestion detached from the person it was
        // about means nothing, exactly like an assertion.
        var suggestionsDeleted = await suggestionRepository.DeleteForStaffAsync(staffKey);

        if (tenantId is not null)
        {
            // Recorded after the fact, with counts and no content — proof
            // the erasure happened, holding nothing it just removed.
            await auditLog.LogAsync(
                SkillsEvidenceAuditAction.EntityTypeStaff,
                staffKey.ToString(),
                SkillsEvidenceAuditAction.AssertionsErasedForSubject,
                actorMemberId: null,
                JsonSerializer.Serialize(new { assertionRowsDeleted = deleted }),
                nowUtc,
                tenantId.Value);

            await auditLog.LogAsync(
                SkillsEvidenceAuditAction.EntityTypeStaff,
                staffKey.ToString(),
                SkillsEvidenceAuditAction.EvidenceErasedForSubject,
                actorMemberId: null,
                JsonSerializer.Serialize(new
                {
                    actorLinksDeleted = linksDeleted,
                    evidenceRowsDeleted = evidenceDeleted,
                    unmappedActorsDeleted = traces.UnmappedActors,
                    rawPagesDeleted = traces.RawPages,
                    suggestionsDeleted
                }),
                nowUtc,
                tenantId.Value);
        }
    }

    private async Task<Guid?> ResolveSubjectTenantAsync(Guid staffKey) =>
        (await staffRepository.GetByStaffKeyAsync(staffKey))?.TenantId;

    /// <summary>
    /// One line per historical row, in the words the subject would use.
    /// Includes the free-text notes: this is the subject's own copy of
    /// their own data, which is the whole point of Article 15.
    /// </summary>
    private static string Describe(StaffSkillAssertion a)
    {
        var state = a.IsCurrent ? "current" : $"superseded {a.SupersededAtUtc:yyyy-MM-dd}";
        var review = a.ReviewedAtUtc is null
            ? "not reviewed"
            : $"reviewed {a.ReviewedAtUtc:yyyy-MM-dd} by staff {a.ReviewerStaffKey}"
              + (a.ReviewNote is null ? string.Empty : $" — \"{a.ReviewNote}\"");
        var due = a.ReviewDueOn is null ? string.Empty : $", review due {a.ReviewDueOn:yyyy-MM-dd}";
        var evidence = a.EvidenceNote is null ? string.Empty : $"; own note: \"{a.EvidenceNote}\"";

        return $"{a.SkillKey}: {a.Proficiency} ({a.Status}, {a.Origin}, {state}{due}); {review}{evidence}";
    }
}
