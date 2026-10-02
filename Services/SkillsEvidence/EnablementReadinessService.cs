using ProgrammePulse.Models.SkillsEvidence;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// The pre-enablement check the design document asks for before a
/// customer turns person-level collection on.
///
/// Its usefulness depends entirely on distinguishing three kinds of
/// answer, which <see cref="ReadinessKind"/> does: what was *observed*
/// in this tenant's data now, what is *structural* in the product and
/// proven by a named test, and what is the *customer's own attestation*
/// and cannot be verified here at all. A page that collapses those into
/// one row of green ticks is reassurance theatre, and the design
/// document's whole posture is against that.
/// </summary>
public interface IEnablementReadinessService
{
    Task<EnablementReadinessReport> BuildAsync(Guid tenantId);
}

public sealed class EnablementReadinessService(
    IContinuityRepository continuity,
    IEngineeringEvidenceRepository evidence,
    TimeProvider timeProvider) : IEnablementReadinessService
{
    public async Task<EnablementReadinessReport> BuildAsync(Guid tenantId)
    {
        var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var checks = new List<ReadinessCheck>();

        var decision = await continuity.GetLiveProcessingDecisionAsync(tenantId);
        var connections = await evidence.GetConnectionsAsync(tenantId);
        var coverage = await evidence.GetCoverageAsync(tenantId);
        var unmappedPeople = await evidence.CountOpenUnmappedActorsAsync(tenantId, includeBots: false);
        var unattributed = await evidence.CountUnattributedAsync(tenantId);

        // ---- The customer's own decisions ----

        checks.Add(decision is null
            ? new ReadinessCheck
            {
                Area = "Lawful basis",
                Question = "Has a lawful basis for person-level evidence been recorded?",
                Result = ReadinessResult.Fail,
                Kind = ReadinessKind.CustomerAttestation,
                Detail = "No data-processing decision exists for this tenant.",
                Remedy = "Record the lawful basis, worker notice and DPIA decision before enabling collection."
            }
            : new ReadinessCheck
            {
                Area = "Lawful basis",
                Question = "Has a lawful basis for person-level evidence been recorded?",
                Result = ReadinessResult.Pass,
                Kind = ReadinessKind.CustomerAttestation,
                Detail = $"{decision.LawfulBasis}, decided {decision.DecidedAtUtc:yyyy-MM-dd}, due for review {decision.ReviewDueOn:yyyy-MM-dd}. "
                    + "The product records this; it cannot assess whether the basis is sound."
            });

        checks.Add(Attestation(
            "Worker notice",
            "Have staff been told their repository activity is collected?",
            decision?.WorkerNoticeGiven == true,
            decision?.WorkerNoticeReference is { } reference ? $"Recorded as: {reference}" : "Recorded, with no reference given.",
            "Transparency is the baseline in the ICO's monitoring-workers guidance. Record the notice before enabling collection."));

        checks.Add(Attestation(
            "DPIA",
            "Has a Data Protection Impact Assessment been completed?",
            decision?.DpiaCompleted == true,
            decision?.DpiaReference is { } dpia ? $"Reference: {dpia}" : "Recorded, with no reference given.",
            "Systematic monitoring of workers is usually high risk, which calls for a DPIA."));

        if (decision is not null && decision.BlockingReason(asOf) is { } blocking)
        {
            checks.Add(new ReadinessCheck
            {
                Area = "Decision currency",
                Question = "Is the recorded decision still current?",
                Result = ReadinessResult.Fail,
                Kind = ReadinessKind.Observed,
                Detail = blocking,
                Remedy = "Renew the decision. Collection is blocked until it is."
            });
        }
        else if (decision is not null)
        {
            checks.Add(new ReadinessCheck
            {
                Area = "Decision currency",
                Question = "Is the recorded decision still current?",
                Result = ReadinessResult.Pass,
                Kind = ReadinessKind.Observed,
                Detail = $"Current until {decision.ReviewDueOn:yyyy-MM-dd}."
            });
        }

        // ---- This tenant's live state ----

        var usable = connections.Where(c => c.IsUsable).ToList();
        var accessLost = connections.Where(c => c.Status == EvidenceConnectionStatus.AccessLost).ToList();

        checks.Add(connections.Count == 0
            ? NotApplicable("Credential protection", "Are source credentials encrypted and tenant-owned?",
                "No evidence source is connected.")
            : new ReadinessCheck
            {
                Area = "Credential protection",
                Question = "Are source credentials encrypted and tenant-owned?",
                Result = accessLost.Count > 0 ? ReadinessResult.Attention : ReadinessResult.Pass,
                Kind = ReadinessKind.Observed,
                Detail = $"{usable.Count} of {connections.Count} connections hold a usable encrypted credential"
                    + (accessLost.Count > 0 ? $"; {accessLost.Count} have lost access and need reconnecting." : ". "
                        + "Each is encrypted under its own Data Protection purpose, with no deployment-wide fallback."),
                Remedy = accessLost.Count > 0 ? "Reconnect the sources that have lost access." : null
            });

        var stale = coverage.Where(c => c.Status is not EvidenceCoverageStatus.Complete and not EvidenceCoverageStatus.OutOfScope).ToList();

        checks.Add(coverage.Count == 0
            ? NotApplicable("Sync completeness", "Is evidence coverage complete rather than partial or stale?",
                "No sync has run.")
            : new ReadinessCheck
            {
                Area = "Sync completeness",
                Question = "Is evidence coverage complete rather than partial or stale?",
                Result = stale.Count > 0 ? ReadinessResult.Attention : ReadinessResult.Pass,
                Kind = ReadinessKind.Observed,
                Detail = stale.Count > 0
                    ? $"{stale.Count} of {coverage.Count} streams are partial or without permission. "
                        + "Evidence missing from a portfolio may simply not have been collected."
                    : $"All {coverage.Count} streams completed cleanly.",
                Remedy = stale.Count > 0 ? "Re-run the sync, and check the connection still reads every selected repository." : null
            });

        checks.Add(unmappedPeople == 0
            ? new ReadinessCheck
            {
                Area = "Identity mapping",
                Question = "Is every source account either identified or excluded?",
                Result = ReadinessResult.Pass,
                Kind = ReadinessKind.Observed,
                Detail = "No source account is waiting to be identified."
            }
            : new ReadinessCheck
            {
                Area = "Identity mapping",
                Question = "Is every source account either identified or excluded?",
                Result = ReadinessResult.Attention,
                Kind = ReadinessKind.Observed,
                Detail = $"{unmappedPeople} accounts are unidentified, covering {unattributed} evidence rows that reach nobody's record.",
                Remedy = "Work through the unidentified-accounts queue, or accept the gap knowingly."
            });

        // ---- Structural properties of the product ----
        //
        // Detail is written for the customer's data protection officer: no
        // type, table or test names. Proof names what enforces each claim.

        checks.Add(Structural(
            "Tenant isolation",
            "Can one customer's data reach another?",
            "Every record of skills, engineering evidence and support activity belongs to one organisation, every "
            + "read and write is scoped to it, and a link to another organisation's record is refused when it is "
            + "written. Covered by automated tests against a real database.",
            "SkillsEvidenceRepositoryIntegrationTests, EngineeringEvidenceRepositoryIntegrationTests, ServiceOpsRepositoryIntegrationTests (non-nullable tenantId, cross-tenant writes refused)."));

        checks.Add(Structural(
            "Least privilege",
            "Is person-level evidence behind a narrower grant than aggregates?",
            "Seeing one person's record needs its own permission. The team-level coverage and service health views "
            + "are built from summaries that have nowhere to hold a person's name. Covered by automated tests.",
            "SkillCoverageQueryServiceTests, SupportCausalityTests (ViewStaffSkillEvidence vs ViewTeamSkillCoverage / ViewServiceHealth)."));

        checks.Add(Structural(
            "Data minimisation",
            "Is anything beyond metadata collected?",
            "Only metadata is stored. There is nowhere to store code changes or file contents from a repository, or "
            + "the text, attachments or requester of a support ticket, and an automated test fails if such a field "
            + "is ever added.",
            "EvidenceContractTests, SupportCausalityTests."));

        checks.Add(Structural(
            "No automatic judgement",
            "Can the product rank people or assign blame?",
            "No. Nothing produces a score or ranking of people; activity can never raise anyone's recorded skill "
            + "level; there is nowhere to record blame; and a support case is only called the cause of a problem "
            + "after a named reviewer has recorded that verdict. Automated tests fail if any of this changes.",
            "EvidenceContractTests, SupportCausalityTests, ContinuityTests (no score or rank on the portfolio, evidence services cannot reference ISkillAssertionService, nowhere to record blame, only a reviewed ConfirmedRootCause is causal)."));

        checks.Add(Structural(
            "Disconnect and deletion",
            "Does disconnecting a source leave an apparently current claim?",
            "Disconnecting a source destroys its stored credential and marks its data out of scope. Evidence already "
            + "collected is kept, but stops counting as current coverage.",
            "EvidenceGdprTests, ServiceOpsGdprTests."));

        checks.Add(Structural(
            "Subject access and erasure",
            "Does a subject access request cover this data?",
            "Yes. A subject access export from Admin includes this person's skills, engineering evidence and support "
            + "participation, and erasing their record erases or detaches all three.",
            "The three IStaffDataParticipant implementations; docs/gdpr.md."));

        checks.Add(Structural(
            "Access audit",
            "Is every identity link and reviewed judgement audited?",
            "Every connection, identity link, skill review, root-cause verdict and erasure is logged with who did it. "
            + "Free-text notes are left out of the log on purpose, so it never keeps what an erasure removed.",
            "SkillAssertionServiceTests.Audit_detail_never_carries_the_free_text_notes and the erasure-audit tests in EvidenceGdprTests and ServiceOpsGdprTests; docs/data-governance.md."));

        checks.Add(Structural(
            "Retention",
            "Are raw captures bounded?",
            "Raw data captured from a source is deleted after every successful collection once it is older than the "
            + "retention period (30 days by default). Processed records are kept until deleted by an administrator.",
            "SkillsEvidence:RawPayloadRetentionDays and ServiceOps:RawPayloadRetentionDays; docs/data-governance.md."));

        return new EnablementReadinessReport { Checks = checks, AsOf = asOf };
    }

    private static ReadinessCheck Attestation(string area, string question, bool satisfied, string detail, string remedy) => new()
    {
        Area = area,
        Question = question,
        Result = satisfied ? ReadinessResult.Pass : ReadinessResult.Fail,
        Kind = ReadinessKind.CustomerAttestation,
        Detail = satisfied ? detail : "Not recorded.",
        Remedy = satisfied ? null : remedy
    };

    private static ReadinessCheck Structural(string area, string question, string detail, string proof) => new()
    {
        Area = area,
        Question = question,
        Result = ReadinessResult.Pass,
        Kind = ReadinessKind.Structural,
        Detail = detail,
        Proof = proof
    };

    private static ReadinessCheck NotApplicable(string area, string question, string detail) => new()
    {
        Area = area,
        Question = question,
        Result = ReadinessResult.NotApplicable,
        Kind = ReadinessKind.Observed,
        Detail = detail
    };
}
