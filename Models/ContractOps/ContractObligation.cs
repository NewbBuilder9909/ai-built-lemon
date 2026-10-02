using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Models.ContractOps;

/// <summary>What kind of engineering commitment a contract clause makes.</summary>
public enum ObligationKind
{
    /// <summary>Security findings at or above a severity must be blocked, or fixed within a deadline.</summary>
    SecurityFindingGate = 0,

    /// <summary>Data must stay in named regions. Recorded here; not assessed from repositories.</summary>
    DataResidency = 1,

    /// <summary>Encryption at rest or in transit. Recorded here; not assessed from repositories.</summary>
    Encryption = 2,

    /// <summary>A design commitment (e.g. "SSO via the customer's identity provider"). Recorded here; not assessed from repositories.</summary>
    DesignControl = 3,
}

/// <summary>The lowest severity a security gate applies to: High means High and Critical.</summary>
public enum FindingSeverity
{
    High = 0,
    Critical = 1,
}

public enum GateAction
{
    /// <summary>Findings at the threshold must not be merged: the repository needs a required security check.</summary>
    Block = 0,

    /// <summary>Findings at the threshold must be fixed within a number of days: the repository needs scanning enabled.</summary>
    RemediateWithin = 1,
}

/// <summary>
/// One clause of a contract that has an engineering consequence
/// (docs/delivery-evidence-and-contract-assurance.md, step 2). Withdrawn,
/// never edited or deleted, so "what did we owe this customer in March"
/// stays answerable.
/// </summary>
public sealed record ContractObligation
{
    public required Guid ObligationKey { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid ContractKey { get; init; }

    public required ObligationKind Kind { get; init; }

    /// <summary>The clause as the contract numbers it, e.g. "Schedule 3, 4.2".</summary>
    public required string ClauseReference { get; init; }

    public required string Description { get; init; }

    public FindingSeverity? SeverityThreshold { get; init; }

    public GateAction? Action { get; init; }

    /// <summary>For <see cref="GateAction.RemediateWithin"/>: the contractual deadline in days.</summary>
    public int? RemediationDays { get; init; }

    public Guid? RecordedByStaffKey { get; init; }

    public required DateTime RecordedAtUtc { get; init; }

    public DateTime? WithdrawnAtUtc { get; init; }

    public Guid? WithdrawnByStaffKey { get; init; }

    public bool IsLive => WithdrawnAtUtc is null;

    /// <summary>
    /// The repository control this obligation needs, or null when the
    /// obligation isn't something a repository setting can show (residency,
    /// encryption, design controls are recorded but not assessed here).
    /// </summary>
    public RepositoryControl? RequiredControl => Kind == ObligationKind.SecurityFindingGate
        ? Action == GateAction.Block ? RepositoryControl.SecurityCheckRequired : RepositoryControl.SecurityScanningEnabled
        : null;
}

/// <summary>A repository setting that an obligation can depend on.</summary>
public enum RepositoryControl
{
    /// <summary>The default branch requires the security tool's pull-request check to pass before merging.</summary>
    SecurityCheckRequired = 0,

    /// <summary>The repository is scanned by the security tool.</summary>
    SecurityScanningEnabled = 1,
}

public enum ControlState
{
    Enforced = 0,
    NotEnforced = 1,
}

/// <summary>
/// A dated statement that a repository control is, or is not, in place:
/// who recorded it, when, on what evidence, and when it lapses. A new
/// attestation for the same repository and control supersedes the old one,
/// which is kept. Decision 2 of the design doc: attestation first, with
/// automated observation later replacing it per repository.
/// </summary>
public sealed record RepositoryControlAttestation
{
    /// <summary>How long an attestation counts before it must be renewed.</summary>
    public const int ValidityDays = 90;

    public required Guid AttestationKey { get; init; }

    public required Guid TenantId { get; init; }

    public required CodeRepositoryRef Repository { get; init; }

    public required RepositoryControl Control { get; init; }

    public required ControlState State { get; init; }

    /// <summary>Where the evidence is: a branch-rule screenshot, a tool settings export, who confirmed it.</summary>
    public required string EvidenceReference { get; init; }

    public Guid? AttestedByStaffKey { get; init; }

    public required DateTime AttestedAtUtc { get; init; }

    public required DateTime ExpiresAtUtc { get; init; }

    public DateTime? SupersededAtUtc { get; init; }
}
