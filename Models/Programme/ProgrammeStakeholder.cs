namespace ProgrammePulse.Models.Programme;

/// <summary>
/// A RACI entry against a Programme. StaffKey identifies an internal
/// stakeholder; ExternalName is free text for a client-side sponsor who has
/// no StaffProfile — exactly one of the two is expected to be set, enforced
/// by the controller, not the model.
/// </summary>
public sealed record ProgrammeStakeholder
{
    public required Guid ProgrammeStakeholderKey { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid ProgrammeKey { get; init; }

    public Guid? StaffKey { get; init; }

    public string? ExternalName { get; init; }

    public required StakeholderRole Role { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}
