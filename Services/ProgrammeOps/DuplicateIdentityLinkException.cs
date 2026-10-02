namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// An ExternalIdentityLink already exists for this (source, external user
/// id) or (source, email). One external identity maps to at most one
/// StaffProfile — enforced here before the insert and by the filtered
/// unique indexes AddIdentityLinkUniqueness creates, so two admins racing
/// on the queue can't produce a conflicting pair either.
/// </summary>
public sealed class DuplicateIdentityLinkException(string externalSource, string? externalUserId, string? email, Guid existingStaffKey)
    : InvalidOperationException(
        $"{externalSource} {(externalUserId is not null ? $"user {externalUserId}" : $"email {email}")} is already linked to a staff profile. Remove that link first if it is wrong.")
{
    public string ExternalSource { get; } = externalSource;

    public string? ExternalUserId { get; } = externalUserId;

    public string? Email { get; } = email;

    public Guid ExistingStaffKey { get; } = existingStaffKey;
}
