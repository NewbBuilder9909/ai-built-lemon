using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Services.ContractOps;

namespace ProgrammePulse.Models.ViewModels.ContractOps;

/// <summary>
/// The words the assurance pages use, in one place so both pages say the same
/// thing. Written for a contract owner: none of them reads as "compliant", and
/// unknown and expired are phrased so they can't be mistaken for "fine".
/// </summary>
public static class AssuranceLabels
{
    public static string Status(ObligationAssuranceStatus status) => status switch
    {
        ObligationAssuranceStatus.Gap => "Gap: at least one repository does not meet this",
        ObligationAssuranceStatus.Unknown => "Not shown: at least one repository is neither observed nor currently attested",
        ObligationAssuranceStatus.NoRepositoriesLinked => "Not assessable: no repository is linked to this customer's projects",
        ObligationAssuranceStatus.AllAttestedEnforced => "Every linked repository meets this (observed or attested)",
        ObligationAssuranceStatus.NotAssessedHere => "Recorded; not assessed from repository settings",
        _ => "Not in effect (withdrawn, or the contract is not active today)",
    };

    public static string Repository(RepositoryAssuranceStatus status, AssuranceSource source = AssuranceSource.Attestation) =>
        (status, source) switch
        {
            (RepositoryAssuranceStatus.Enforced, AssuranceSource.Observation) => "In place (observed)",
            (RepositoryAssuranceStatus.NotEnforced, AssuranceSource.Observation) => "Not in place (observed)",
            (RepositoryAssuranceStatus.FindingsOverdue, _) => "Gap: findings past the remediation deadline (observed)",
            (RepositoryAssuranceStatus.Enforced, _) => "In place (attested)",
            (RepositoryAssuranceStatus.NotEnforced, _) => "Not in place (attested)",
            (RepositoryAssuranceStatus.Expired, _) => "Unknown: attestation lapsed",
            _ => "Unknown: not observed, never attested",
        };

    /// <summary>What the scanning tool's gate does today, in a contract owner's words.</summary>
    public static string Gate(Models.SecurityAssurance.RepositoryGateObservation observation)
    {
        if (!observation.GateConfigured)
        {
            return "No pull-request gate configured";
        }

        if (observation.GateMinimumSeverity is not { } threshold)
        {
            return "Gate configured, but it never fails";
        }

        var kinds = new[]
            {
                observation.FailsOnDependencies ? "dependencies" : null,
                observation.FailsOnCode ? "code" : null,
                observation.FailsOnSecrets ? "secrets" : null,
            }
            .Where(k => k is not null)
            .ToList();
        return kinds.Count == 0
            ? $"Gate at {threshold} and above, but no scan type fails it"
            : $"Fails at {threshold} and above on {string.Join(", ", kinds)}";
    }

    public static string Control(RepositoryControl control) => control switch
    {
        RepositoryControl.SecurityCheckRequired => "Security check required before merge",
        _ => "Security scanning enabled",
    };

    public static string Kind(ContractObligation obligation) => obligation.Kind switch
    {
        ObligationKind.SecurityFindingGate => obligation.Action == GateAction.Block
            ? $"block {Severity(obligation)} findings before merge"
            : $"fix {Severity(obligation)} findings within {obligation.RemediationDays} days",
        ObligationKind.DataResidency => "data residency",
        ObligationKind.Encryption => "encryption",
        _ => "design control",
    };

    private static string Severity(ContractObligation obligation) =>
        obligation.SeverityThreshold == FindingSeverity.Critical ? "Critical" : "High and Critical";
}
