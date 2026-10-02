namespace ProgrammePulse.Models.Staff;

/// <summary>
/// The Umbraco Member Group names staff accounts are assigned to.
/// Kept as named constants (not an enum) because the Member Groups API
/// (IMemberGroupService, IMemberManager.IsMemberAuthorizedAsync) works in
/// terms of group name strings, not a typed role — this is the one place
/// those literal names are defined, so nothing else in the codebase should
/// hardcode them.
///
/// <see cref="All"/> is the five tenant-level roles an Admin may assign
/// through /staffops/admin/create. <see cref="PlatformAdmin"/> is
/// deliberately not in that list: it is the product operator's role (tenant
/// lifecycle, plans — see StaffTenantAdminController) and is only granted
/// from the Umbraco backoffice Members section, so a tenant Admin can't
/// promote an account into it. Membership of the Admin group is still
/// required for tenant-scoped admin pages; a platform operator who needs
/// both holds both groups.
/// </summary>
public static class StaffRole
{
    public const string Admin = "Admin";
    public const string HolidayApprover = "Holiday Approver";
    public const string TeamLead = "Team Lead";
    public const string Staff = "Staff";
    public const string Board = "Board";

    /// <summary>
    /// Read-only operational reporting. Added so an analyst does not have to
    /// be made a Team Lead — which would hand them RAID edits, baseline locks
    /// and change-request decisions purely to let them read a report. See
    /// Models/Staff/RoleCapabilities and docs/persona-harness-build-gate.md.
    /// </summary>
    public const string Analyst = "Analyst";

    public const string PlatformAdmin = "Platform Admin";

    /// <summary>Tenant-assignable roles. Board and Analyst are read-only; neither receives Admin access.</summary>
    public static readonly IReadOnlyList<string> All = [Admin, HolidayApprover, TeamLead, Staff, Board, Analyst];

    /// <summary>
    /// Roles that oversee delivery rather than carry it: Board and Analyst are
    /// read-only, and Platform Admin operates the product. Someone holding only
    /// these roles, and recording no time in the period, is left out of
    /// delivery capacity, utilisation and load — counting them at 0% made the
    /// team look under-used. Anyone left out is named on the page, never
    /// silently dropped, and recorded time always brings a person back in.
    /// Admin is deliberately not here: in a small firm the admin usually
    /// delivers too.
    /// </summary>
    public static readonly IReadOnlyList<string> OversightOnly = [Board, Analyst, PlatformAdmin];

    /// <summary>Every group StaffGroupSeeder guarantees exists.</summary>
    public static readonly IReadOnlyList<string> Seeded = [Admin, HolidayApprover, TeamLead, Staff, Board, Analyst, PlatformAdmin];
}
