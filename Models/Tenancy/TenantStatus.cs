namespace ProgrammePulse.Models.Tenancy;

/// <summary>
/// Commercial lifecycle of a tenant. Stored as its integer value in
/// Tenancy_Tenant.status — append new members at the end, never renumber.
/// What each state means for access is decided in one place,
/// Services/Tenancy/TenantAccessPolicy, not scattered across controllers.
/// </summary>
public enum TenantStatus
{
    /// <summary>Evaluating; usable until <see cref="Tenant.TrialEndsAtUtc"/> passes.</summary>
    Trial = 0,

    /// <summary>Paying / contracted; fully usable.</summary>
    Active = 1,

    /// <summary>Access withdrawn (non-payment, breach, customer request) but data retained — reversible.</summary>
    Suspended = 2,

    /// <summary>Off-boarded; data retained only for the retention window in docs/data-governance.md — not reversible from the UI.</summary>
    Archived = 3
}
