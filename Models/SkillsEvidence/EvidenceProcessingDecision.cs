namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// The lawful basis a customer is relying on to collect person-level
/// engineering evidence. UK GDPR Article 6; the two that realistically
/// apply to an employer monitoring work output.
///
/// Consent is deliberately absent. The ICO's position is that consent is
/// rarely a valid basis in an employment context because the power
/// imbalance makes it hard to show it was freely given — offering it
/// here would invite customers to pick the one that is easiest to defend
/// in a meeting and hardest to defend afterwards.
/// </summary>
public enum EvidenceLawfulBasis
{
    /// <summary>Necessary for the employment contract — Article 6(1)(b).</summary>
    ContractualNecessity = 0,

    /// <summary>The employer's legitimate interests, balanced against the worker's rights — Article 6(1)(f).</summary>
    LegitimateInterests = 1,

    /// <summary>A legal obligation the customer is subject to — Article 6(1)(c).</summary>
    LegalObligation = 2
}

/// <summary>
/// A tenant's recorded decision to collect person-level engineering
/// evidence: the lawful basis, whether staff were told, and the DPIA
/// outcome.
///
/// **This is a gate, not a form.** The design document requires the
/// customer's worker-notice, lawful-basis and DPIA decision to be
/// recorded *before* person-level evidence is collected, and
/// GitHubEvidenceIngestionService refuses to run without a current one.
/// A requirement that lives only in a document is a requirement that
/// gets skipped by the person who did not read the document.
///
/// It does **not** gate the skills matrix. That is self-declared data
/// the subject can see and challenge, which is the mild end of worker
/// monitoring; repository evidence is collected about people from a
/// third-party system, which is not. Drawing the line here rather than
/// everywhere keeps the gate meaningful instead of something customers
/// learn to click through.
///
/// The product does not and cannot decide whether a customer's basis is
/// sound. What it can do is refuse to collect until somebody has put
/// their name to the question.
/// </summary>
public sealed record EvidenceProcessingDecision
{
    public required Guid DecisionKey { get; init; }

    public required Guid TenantId { get; init; }

    public required EvidenceLawfulBasis LawfulBasis { get; init; }

    /// <summary>
    /// Whether the customer has told the affected staff. The ICO's
    /// monitoring-workers guidance treats transparency as the baseline,
    /// so this being false blocks collection on its own.
    /// </summary>
    public required bool WorkerNoticeGiven { get; init; }

    /// <summary>How staff were told — a policy reference, a date, a meeting.</summary>
    public string? WorkerNoticeReference { get; init; }

    /// <summary>
    /// Whether a Data Protection Impact Assessment has been completed.
    /// The guidance calls for one where processing is likely high risk,
    /// and systematic monitoring of workers usually is.
    /// </summary>
    public required bool DpiaCompleted { get; init; }

    /// <summary>The customer's own DPIA reference, so an auditor can find it.</summary>
    public string? DpiaReference { get; init; }

    public DateTime? DpiaCompletedAtUtc { get; init; }

    /// <summary>What the customer says they are collecting it for. Their words, on the record.</summary>
    public required string Purpose { get; init; }

    /// <summary>The privacy or HR owner who signed this off. A person, not a role.</summary>
    public required Guid DecidedByStaffKey { get; init; }

    public required DateTime DecidedAtUtc { get; init; }

    /// <summary>
    /// When the decision falls due for another look. A DPIA is not a
    /// once-and-for-all artefact — the processing changes, and so does
    /// the balance. Past this date collection stops until it is renewed.
    /// </summary>
    public required DateOnly ReviewDueOn { get; init; }

    public DateTime? WithdrawnAtUtc { get; init; }

    /// <summary>
    /// Whether person-level evidence collection is permitted right now.
    /// All four conditions, and the caller is expected to read
    /// <see cref="BlockingReason"/> rather than guess which failed.
    /// </summary>
    public bool PermitsCollectionOn(DateOnly asOf) =>
        WithdrawnAtUtc is null && WorkerNoticeGiven && DpiaCompleted && ReviewDueOn >= asOf;

    /// <summary>A sentence an admin can act on, or null when collection is permitted.</summary>
    public string? BlockingReason(DateOnly asOf)
    {
        if (WithdrawnAtUtc is not null)
        {
            return "The data-processing decision for engineering evidence has been withdrawn.";
        }

        if (!WorkerNoticeGiven)
        {
            return "Staff have not been told that their repository activity is collected. Record the notice before syncing.";
        }

        if (!DpiaCompleted)
        {
            return "No DPIA has been recorded for person-level evidence collection.";
        }

        return ReviewDueOn < asOf
            ? $"The data-processing decision fell due for review on {ReviewDueOn:yyyy-MM-dd} and must be renewed before collection continues."
            : null;
    }

    /// <summary>The message shown when no decision exists at all.</summary>
    public const string MissingReason =
        "Person-level evidence collection needs a recorded lawful basis, worker notice and DPIA decision. "
        + "An administrator records it at /staffops/skills/continuity/processing.";
}
