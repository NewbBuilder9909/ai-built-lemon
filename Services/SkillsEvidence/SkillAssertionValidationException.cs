namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// A skills request was well-formed but not allowed by the domain rules —
/// an empty skill key, a rejection with no rationale, a challenge against
/// something never reviewed.
///
/// Distinct from Services/Shared/CrossTenantReferenceException, which
/// this feature also throws: that one means "the key you named is not
/// yours", and callers turn it into 404 so another tenant's keys are
/// indistinguishable from keys that never existed. This one is safe to show
/// the user, and callers turn it into a message on their own page.
/// </summary>
public sealed class SkillAssertionValidationException(string message) : InvalidOperationException(message);
