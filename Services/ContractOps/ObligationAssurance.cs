using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.SecurityAssurance;
using ProgrammePulse.Services.SecurityAssurance;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>The state of one repository's required control, as the evidence stands today.</summary>
public enum RepositoryAssuranceStatus
{
    /// <summary>The control is in place: observed from the scanning tool, or attested.</summary>
    Enforced = 0,

    /// <summary>The control is not in place (observed or attested): a gap against the contract.</summary>
    NotEnforced = 1,

    /// <summary>The last attestation has lapsed. Treated as unknown, never as enforced.</summary>
    Expired = 2,

    /// <summary>No observation, and nobody has attested this repository's control.</summary>
    Unknown = 3,

    /// <summary>
    /// Scanned, but at least one finding at the contracted severity is still not
    /// closed past its remediation deadline: a gap. Ignored and snoozed count,
    /// because neither is a contractual risk acceptance.
    /// </summary>
    FindingsOverdue = 4,
}

/// <summary>Where a repository's status came from.</summary>
public enum AssuranceSource
{
    Attestation = 0,

    /// <summary>Read from the scanning tool's own configuration; supersedes any attestation (decision 2).</summary>
    Observation = 1,
}

/// <summary>One obligation's overall position. Deliberately no "compliant": the best is "all attested enforced".</summary>
public enum ObligationAssuranceStatus
{
    /// <summary>Withdrawn, or the contract is not active today.</summary>
    NotInEffect = 0,

    /// <summary>Residency, encryption and design obligations: recorded, not assessed from repository settings.</summary>
    NotAssessedHere = 1,

    /// <summary>The customer's projects have no linked repository, so there is nothing to assess.</summary>
    NoRepositoriesLinked = 2,

    /// <summary>At least one in-scope repository does not meet the control, or has overdue findings.</summary>
    Gap = 3,

    /// <summary>No gap shown, but at least one repository is unknown or expired.</summary>
    Unknown = 4,

    /// <summary>Every in-scope repository meets the control, by observation or current attestation.</summary>
    AllAttestedEnforced = 5,
}

public sealed record InScopeRepository(CodeRepositoryRef Repository, IReadOnlyList<string> Projects);

/// <param name="OutstandingFindings">Not-closed findings at the contracted severity, oldest first. Shown for both controls.</param>
/// <param name="OverdueFindings">How many of those are past the remediation deadline (remediate obligations only).</param>
/// <param name="UnheldChecks">
/// Pull-request checks bypassed or timed out while the contract ran. Shown for
/// "block" obligations and labelled unverified: the check-run fields have not
/// yet been read from a live gated repository.
/// </param>
public sealed record RepositoryAssurance(
    CodeRepositoryRef Repository,
    IReadOnlyList<string> Projects,
    RepositoryAssuranceStatus Status,
    RepositoryControlAttestation? Attestation,
    AssuranceSource Source = AssuranceSource.Attestation,
    RepositoryGateObservation? Observation = null,
    IReadOnlyList<SecurityFinding>? OutstandingFindings = null,
    int OverdueFindings = 0,
    IReadOnlyList<SecurityCheckRun>? UnheldChecks = null)
{
    public bool IsGap => Status is RepositoryAssuranceStatus.NotEnforced or RepositoryAssuranceStatus.FindingsOverdue;

    public bool IsUnknown => Status is RepositoryAssuranceStatus.Unknown or RepositoryAssuranceStatus.Expired;
}

public sealed record ObligationAssessment(
    ContractObligation Obligation,
    ObligationAssuranceStatus Status,
    IReadOnlyList<RepositoryAssurance> Repositories);

/// <summary>
/// Evaluates an obligation against the repositories that serve the
/// contract's customer (step 1's declared links), what the scanning tool
/// observes about them (step 3), and their current attestations. Pure, so
/// each rule is unit-tested:
///
/// - An observation supersedes an attestation for that repository; a
///   repository the tool doesn't list falls back to its attestation.
/// - An observed repository with no gate configuration is "not enforced",
///   never unknown.
/// - An expired or missing attestation is never read as enforced.
/// - A contract with no linked repositories is "no repositories linked", not clean.
/// </summary>
public static class ObligationAssurance
{
    public static ObligationAssessment Evaluate(
        ContractObligation obligation,
        Contract contract,
        IReadOnlyList<InScopeRepository> inScope,
        IReadOnlyList<RepositoryControlAttestation> currentAttestations,
        DateTime nowUtc,
        SecurityAssuranceSnapshot? observed = null)
    {
        var today = DateOnly.FromDateTime(nowUtc);
        if (!obligation.IsLive || contract.Status != ContractStatus.Active || today < contract.StartDate || today > contract.EndDate)
        {
            return new ObligationAssessment(obligation, ObligationAssuranceStatus.NotInEffect, []);
        }

        if (obligation.RequiredControl is not { } control)
        {
            return new ObligationAssessment(obligation, ObligationAssuranceStatus.NotAssessedHere, []);
        }

        if (inScope.Count == 0)
        {
            return new ObligationAssessment(obligation, ObligationAssuranceStatus.NoRepositoriesLinked, []);
        }

        var snapshot = observed is { Connected: true } ? observed : null;
        var contracted = obligation.SeverityThreshold == FindingSeverity.Critical ? SecuritySeverity.Critical : SecuritySeverity.High;
        var contractStartUtc = contract.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var repositories = inScope
            .Select(repository =>
            {
                var observation = snapshot?.Observations.FirstOrDefault(o => o.Repository.SameRepositoryAs(repository.Repository));
                if (snapshot is not null && observation is not null)
                {
                    return Observed(repository, observation, snapshot, control, contracted, obligation.RemediationDays, contractStartUtc, nowUtc);
                }

                var attestation = currentAttestations
                    .Where(a => a.Control == control && a.SupersededAtUtc is null && a.Repository.SameRepositoryAs(repository.Repository))
                    .OrderByDescending(a => a.AttestedAtUtc)
                    .FirstOrDefault();

                var status = attestation is null ? RepositoryAssuranceStatus.Unknown
                    : attestation.ExpiresAtUtc <= nowUtc ? RepositoryAssuranceStatus.Expired
                    : attestation.State == ControlState.Enforced ? RepositoryAssuranceStatus.Enforced
                    : RepositoryAssuranceStatus.NotEnforced;

                return new RepositoryAssurance(repository.Repository, repository.Projects, status, attestation);
            })
            .ToList();

        var overall = repositories.Any(r => r.IsGap) ? ObligationAssuranceStatus.Gap
            : repositories.Any(r => r.IsUnknown) ? ObligationAssuranceStatus.Unknown
            : ObligationAssuranceStatus.AllAttestedEnforced;

        return new ObligationAssessment(obligation, overall, repositories);
    }

    private static RepositoryAssurance Observed(
        InScopeRepository repository, RepositoryGateObservation observation, SecurityAssuranceSnapshot snapshot,
        RepositoryControl control, SecuritySeverity contracted, int? remediationDays, DateTime contractStartUtc, DateTime nowUtc)
    {
        var findings = snapshot.OutstandingFindings
            .Where(f => f.IsOutstanding && f.Severity >= contracted && f.Repository.SameRepositoryAs(repository.Repository))
            .OrderBy(f => f.FirstDetectedAtUtc)
            .ToList();

        if (control == RepositoryControl.SecurityCheckRequired)
        {
            var unheld = snapshot.RecentCheckRuns
                .Where(r => r.Outcome is CheckRunOutcome.Bypassed or CheckRunOutcome.TimedOut
                            && r.StartedAtUtc >= contractStartUtc
                            && r.Repository.SameRepositoryAs(repository.Repository))
                .OrderByDescending(r => r.StartedAtUtc)
                .ToList();

            var status = SecurityGatePolicy.BlocksAt(observation, contracted)
                ? RepositoryAssuranceStatus.Enforced
                : RepositoryAssuranceStatus.NotEnforced;
            return new RepositoryAssurance(repository.Repository, repository.Projects, status, null,
                AssuranceSource.Observation, observation, findings, 0, unheld);
        }

        // "Fix within N days" needs the repository scanned, and nothing at the
        // contracted severity left open past its deadline.
        var overdue = remediationDays is { } days
            ? findings.Count(f => f.FirstDetectedAtUtc.AddDays(days) < nowUtc)
            : 0;
        var remediationStatus = !SecurityGatePolicy.IsScanned(observation) ? RepositoryAssuranceStatus.NotEnforced
            : overdue > 0 ? RepositoryAssuranceStatus.FindingsOverdue
            : RepositoryAssuranceStatus.Enforced;
        return new RepositoryAssurance(repository.Repository, repository.Projects, remediationStatus, null,
            AssuranceSource.Observation, observation, findings, overdue, []);
    }
}
