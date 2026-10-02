namespace ProgrammePulse.Models.Programme;

/// <summary>
/// Read-only projection of "which StaffRole groups is this staff member in"
/// — not a table. Umbraco Member Groups are the single source of truth for
/// role membership (see StaffAuthorizationService); this record exists so
/// Gold-layer code can reason about a staff member's roles without querying
/// MemberManager directly. Built by IStaffRoleAssignmentService.
/// </summary>
public sealed record StaffRoleAssignment
{
    public required Guid StaffKey { get; init; }

    public required int MemberId { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }
}
