namespace ProgrammePulse.Services.Shared;

/// <summary>
/// The result of an application-service command that a person can get wrong
/// (an empty title, a missing name). The caller shows <see cref="Error"/> to
/// them instead of the command being silently skipped. Several Reporting
/// forms used to do exactly that: a blank title redirected back with no
/// risk created and no explanation.
///
/// Not for authorization or tenancy failures. Those are refused before the
/// command runs (RequireCapability, CurrentTenant) or surface as
/// CrossTenantReferenceException, which becomes a 404.
/// </summary>
public sealed record CommandOutcome(bool Succeeded, string? Error)
{
    public static CommandOutcome Ok { get; } = new(true, null);

    public static CommandOutcome Invalid(string error) => new(false, error);
}
