using System.Text.Json;
using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ServiceOps;

/// <summary>
/// The lifecycle of a support-to-code link, and the one place the product
/// is allowed to turn a relationship into a causal claim.
///
/// Four rules live here, and each exists because the alternative is a
/// product that tells a manager which developer caused which outage on
/// evidence that amounts to two strings matching:
///
/// 1. An automatic <see cref="SupportLinkMethod.IssueKeyMatch"/> can
///    never overwrite a reviewed verdict. A reviewer who ruled something
///    out must not find it re-suggested and re-promoted by the next sync.
/// 2. Confirming or ruling out requires a named reviewer, a timestamp
///    and a rationale. All three, always.
/// 3. A reviewer cannot confirm a root cause against a case that is not
///    their tenant's. That reads as not found, never as forbidden.
/// 4. Nothing here accepts an author, a committer or a blame target.
///    Root cause is a property of a change, not of a person.
/// </summary>
public interface ISupportCodeLinkService
{
    /// <summary>
    /// Records relationships the sync noticed. Never upgrades an
    /// existing reviewed link, and never creates a causal claim.
    /// Returns how many were newly suggested.
    /// </summary>
    Task<int> SuggestLinksAsync(IReadOnlyList<SupportCodeLink> suggestions, Guid tenantId, DateTime nowUtc);

    /// <summary>A person says these are related. Still not causal.</summary>
    Task<SupportCodeLink> LinkManuallyAsync(
        Guid connectionKey, string externalTicketId, LinkedArtifactType artifactType, string artifactExternalId,
        string? artifactSource, string? artifactUrl, Guid actorStaffKey, Guid tenantId, int? actorMemberId);

    /// <summary>
    /// The reviewed act: this change caused this case. Requires a
    /// rationale, records the reviewer and the time, and is audited.
    /// </summary>
    Task<SupportCodeLink> ConfirmRootCauseAsync(
        Guid linkKey, Guid reviewerStaffKey, string reviewNote, Guid tenantId, int? actorMemberId);

    /// <summary>
    /// The other reviewed act, and just as valuable: this change did not
    /// cause it. Recorded rather than deleted so the same suggestion is
    /// not re-raised every sync.
    /// </summary>
    Task<SupportCodeLink> RuleOutAsync(
        Guid linkKey, Guid reviewerStaffKey, string reviewNote, Guid tenantId, int? actorMemberId);

    Task RemoveLinkAsync(Guid linkKey, Guid tenantId, int? actorMemberId);
}

public sealed class SupportCodeLinkService(
    IServiceOpsRepository repository,
    TimeProvider timeProvider) : ISupportCodeLinkService
{
    private const int MaxNoteLength = 1000;

    public async Task<int> SuggestLinksAsync(IReadOnlyList<SupportCodeLink> suggestions, Guid tenantId, DateTime nowUtc)
    {
        var created = 0;

        foreach (var suggestion in suggestions)
        {
            if (suggestion.TenantId != tenantId)
            {
                throw new CrossTenantReferenceException("SupportCodeLink", suggestion.LinkKey);
            }

            var existing = (await repository.GetLinksForCaseAsync(suggestion.ConnectionKey, suggestion.ExternalTicketId, tenantId))
                .FirstOrDefault(l =>
                    l.ArtifactType == suggestion.ArtifactType
                    && string.Equals(l.ArtifactExternalId, suggestion.ArtifactExternalId, StringComparison.OrdinalIgnoreCase));

            // A reviewer's verdict outranks anything the sync notices. If
            // they confirmed it, re-suggesting adds nothing; if they ruled
            // it out, re-suggesting would undo their work every night.
            if (existing is { IsReviewed: true })
            {
                continue;
            }

            if (existing is not null)
            {
                continue;
            }

            await repository.UpsertLinkAsync(suggestion with { CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc });
            created++;
        }

        return created;
    }

    public async Task<SupportCodeLink> LinkManuallyAsync(
        Guid connectionKey, string externalTicketId, LinkedArtifactType artifactType, string artifactExternalId,
        string? artifactSource, string? artifactUrl, Guid actorStaffKey, Guid tenantId, int? actorMemberId)
    {
        if (string.IsNullOrWhiteSpace(artifactExternalId))
        {
            throw new ServiceOpsValidationException("Name the issue, pull request, commit or release you are linking to.");
        }

        if (!Enum.IsDefined(artifactType))
        {
            throw new ServiceOpsValidationException("Choose one of the defined artefact types.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var existing = (await repository.GetLinksForCaseAsync(connectionKey, externalTicketId, tenantId))
            .FirstOrDefault(l =>
                l.ArtifactType == artifactType
                && string.Equals(l.ArtifactExternalId, artifactExternalId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (existing is { IsReviewed: true })
        {
            throw new ServiceOpsValidationException(
                "A reviewer has already made a decision about this artefact. Change their decision rather than re-linking it.");
        }

        var link = await repository.UpsertLinkAsync(new SupportCodeLink
        {
            LinkKey = existing?.LinkKey ?? Guid.NewGuid(),
            TenantId = tenantId,
            ConnectionKey = connectionKey,
            ExternalTicketId = externalTicketId,
            ArtifactType = artifactType,
            ArtifactExternalId = artifactExternalId.Trim(),
            ArtifactSource = Clean(artifactSource),
            ArtifactUrl = Clean(artifactUrl),
            // A person saying "these are related" is exactly that, and no
            // more. Promoting it is a separate, reviewed act.
            Method = SupportLinkMethod.ManuallyLinked,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now
        });

        await repository.LogAsync(
            ServiceOpsAuditAction.EntityTypeLink, link.LinkKey.ToString(), ServiceOpsAuditAction.LinkCreated,
            actorMemberId,
            JsonSerializer.Serialize(new { link.ExternalTicketId, artifactType = link.ArtifactType.ToString(), link.ArtifactExternalId }),
            now, tenantId);

        return link;
    }

    public Task<SupportCodeLink> ConfirmRootCauseAsync(
        Guid linkKey, Guid reviewerStaffKey, string reviewNote, Guid tenantId, int? actorMemberId) =>
        ReviewAsync(linkKey, reviewerStaffKey, reviewNote, SupportLinkMethod.ConfirmedRootCause, tenantId, actorMemberId);

    public Task<SupportCodeLink> RuleOutAsync(
        Guid linkKey, Guid reviewerStaffKey, string reviewNote, Guid tenantId, int? actorMemberId) =>
        ReviewAsync(linkKey, reviewerStaffKey, reviewNote, SupportLinkMethod.RuledOut, tenantId, actorMemberId);

    private async Task<SupportCodeLink> ReviewAsync(
        Guid linkKey, Guid reviewerStaffKey, string reviewNote, SupportLinkMethod verdict, Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var existing = await repository.GetLinkAsync(linkKey, tenantId)
            ?? throw new CrossTenantReferenceException("SupportCodeLink", linkKey);

        var note = Clean(reviewNote) ?? throw new ServiceOpsValidationException(
            verdict == SupportLinkMethod.ConfirmedRootCause
                ? "Say what the assessment found. Calling a change the cause of a customer-affecting case is the strongest claim this product makes, and it needs to be readable by someone who disagrees."
                : "Say why this change was ruled out, so the same suggestion is not investigated twice.");

        var reviewed = await repository.UpsertLinkAsync(existing with
        {
            Method = verdict,
            ReviewedByStaffKey = reviewerStaffKey,
            ReviewedAtUtc = now,
            ReviewNote = note,
            UpdatedAtUtc = now
        });

        await repository.LogAsync(
            ServiceOpsAuditAction.EntityTypeLink, linkKey.ToString(),
            verdict == SupportLinkMethod.ConfirmedRootCause
                ? ServiceOpsAuditAction.RootCauseConfirmed
                : ServiceOpsAuditAction.RootCauseRuledOut,
            actorMemberId,
            // The reviewer and the verdict are the accountability
            // skeleton and survive; the rationale lives on the link row.
            JsonSerializer.Serialize(new
            {
                reviewed.ExternalTicketId,
                artifactType = reviewed.ArtifactType.ToString(),
                reviewed.ArtifactExternalId,
                reviewerStaffKey
            }),
            now, tenantId);

        return reviewed;
    }

    public async Task RemoveLinkAsync(Guid linkKey, Guid tenantId, int? actorMemberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var existing = await repository.GetLinkAsync(linkKey, tenantId)
            ?? throw new CrossTenantReferenceException("SupportCodeLink", linkKey);

        await repository.DeleteLinkAsync(linkKey, tenantId);

        await repository.LogAsync(
            ServiceOpsAuditAction.EntityTypeLink, linkKey.ToString(), ServiceOpsAuditAction.LinkRemoved,
            actorMemberId,
            JsonSerializer.Serialize(new { existing.ExternalTicketId, method = existing.Method.ToString() }),
            now, tenantId);
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxNoteLength ? trimmed : trimmed[..MaxNoteLength];
    }
}
