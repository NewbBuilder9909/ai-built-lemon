using ProgrammePulse.Models.SecurityAssurance;

namespace ProgrammePulse.Services.SecurityAssurance;

/// <summary>
/// Whether an observed repository gate meets a contract's "block findings at
/// or above this severity before merge". Pure, so each rule is unit-tested.
///
/// A gate meets it only if it is configured, fails the check at the
/// contracted severity or lower, and fails on new dependency, code and secret
/// findings: the three kinds a security clause is about. A gate that fails
/// only on Critical does not meet "block High and Critical"; a missing
/// configuration is "not enforced", never unknown.
/// </summary>
public static class SecurityGatePolicy
{
    public static bool BlocksAt(RepositoryGateObservation observation, SecuritySeverity contractedSeverity) =>
        observation.GateConfigured
        && observation.GateMinimumSeverity is { } threshold
        && threshold <= contractedSeverity
        && observation.FailsOnDependencies
        && observation.FailsOnCode
        && observation.FailsOnSecrets;

    /// <summary>Whether the tool has scanned the repository at all: the "scanning enabled" control.</summary>
    public static bool IsScanned(RepositoryGateObservation observation) => observation.LastScannedAtUtc is not null;
}
