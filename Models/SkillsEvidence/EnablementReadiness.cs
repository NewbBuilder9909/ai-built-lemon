namespace ProgrammePulse.Models.SkillsEvidence;

public enum ReadinessResult
{
    Pass = 0,

    /// <summary>Works, but something about this tenant's state needs attention.</summary>
    Attention = 1,

    /// <summary>Blocks customer enablement.</summary>
    Fail = 2,

    /// <summary>Nothing connected that this check applies to.</summary>
    NotApplicable = 3
}

/// <summary>
/// Whether a check is about this tenant's live state or about the
/// product itself.
///
/// The distinction is the honest part. "Tenant isolation is enforced" is
/// a property of the code, proven by a named test, and it would be
/// misleading to present it as though it had been measured in this
/// customer's database today. "No connection has lost its credential" is
/// the opposite. Mixing the two into one green tick is how a readiness
/// page becomes theatre.
/// </summary>
public enum ReadinessKind
{
    /// <summary>Computed from this tenant's actual data, now.</summary>
    Observed = 0,

    /// <summary>A structural property of the product, with the test that proves it named.</summary>
    Structural = 1,

    /// <summary>Something only the customer can answer. The product records it; it cannot verify it.</summary>
    CustomerAttestation = 2
}

/// <summary>One line of the pre-enablement check.</summary>
public sealed record ReadinessCheck
{
    public required string Area { get; init; }

    public required string Question { get; init; }

    public required ReadinessResult Result { get; init; }

    public required ReadinessKind Kind { get; init; }

    /// <summary>What was found, or what the product guarantees, in the customer's words.</summary>
    public required string Detail { get; init; }

    /// <summary>
    /// For a structural check, the tests or documents that enforce it. Kept
    /// off the page (a data protection officer reads <see cref="Detail"/>),
    /// but required, so a structural claim is never an unbacked assertion.
    /// </summary>
    public string? Proof { get; init; }

    /// <summary>What the admin should do about it. Null when nothing is needed.</summary>
    public string? Remedy { get; init; }
}

/// <summary>
/// The pre-enablement check the design document asks for before a
/// customer turns person-level collection on: tenant isolation, least
/// privilege, credential protection, data minimisation, retention,
/// disconnect and deletion, GDPR, access audits, and stale or partial
/// sync — plus the worker-notice, lawful-basis and DPIA decision.
///
/// Deliberately not a score. It is a list of questions with answers and,
/// where an answer is unsatisfactory, what to do. A percentage would
/// invite somebody to enable at eighty per cent without reading which
/// twenty were missing.
/// </summary>
public sealed record EnablementReadinessReport
{
    public required IReadOnlyList<ReadinessCheck> Checks { get; init; }

    public required DateOnly AsOf { get; init; }

    public IReadOnlyList<ReadinessCheck> Blocking =>
        [.. Checks.Where(c => c.Result == ReadinessResult.Fail)];

    public IReadOnlyList<ReadinessCheck> NeedingAttention =>
        [.. Checks.Where(c => c.Result == ReadinessResult.Attention)];

    /// <summary>
    /// Nothing is blocking. Note what this does **not** say: that the
    /// customer is ready, that their DPIA is sound, or that the product
    /// has been validated against their data. It says no check in this
    /// list currently fails.
    /// </summary>
    public bool NothingBlocking => Blocking.Count == 0;

    /// <summary>
    /// Checks the product cannot answer for the customer. Listed
    /// separately so a reader can see how much of the page is the
    /// customer's own word.
    /// </summary>
    public IReadOnlyList<ReadinessCheck> CustomerAttestations =>
        [.. Checks.Where(c => c.Kind == ReadinessKind.CustomerAttestation)];
}
