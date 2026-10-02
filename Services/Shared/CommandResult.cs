namespace ProgrammePulse.Services.Shared;

public enum CommandStatus
{
    Succeeded,

    /// <summary>The connection (or other target) is not in this tenant, or is the wrong provider.</summary>
    NotFound,

    /// <summary>Refused for a reason the person can act on; see the message.</summary>
    Refused
}

/// <summary>
/// The outcome of an admin command whose refusals are explained ("that
/// token has expired", "choose between 1 and 25 repositories"), so the
/// message travels with the status. Used by the evidence and desk connection
/// flows. A NotFound target is deliberately indistinguishable from another
/// tenant's.
/// </summary>
public sealed record CommandResult(CommandStatus Status, string? Message = null, Guid? ConnectionKey = null)
{
    public static CommandResult NotFound { get; } = new(CommandStatus.NotFound);

    public static CommandResult Refused(string message) => new(CommandStatus.Refused, message);

    public static CommandResult Succeeded(string? message = null, Guid? connectionKey = null) =>
        new(CommandStatus.Succeeded, message, connectionKey);
}
