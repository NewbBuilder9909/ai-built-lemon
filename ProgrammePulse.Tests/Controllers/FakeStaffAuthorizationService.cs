using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.Controllers;

/// <summary>
/// In-memory stand-in for IStaffAuthorizationService, following this
/// project's hand-rolled fake convention.
///
/// Holds no capability by default — the "logged out, or deactivated" state —
/// so an action that gains a new capability check refuses until a test opts
/// in, rather than silently passing.
///
/// Prefer <see cref="ForRole"/> over hand-listing capabilities: it reads the
/// real matrix, so a test that says "a Team Lead" keeps meaning whatever the
/// product says a Team Lead is. Hand-listing lets a test keep passing while
/// describing a role that no longer exists — which is how a refusal test
/// quietly stops testing the boundary it was written for.
/// </summary>
public sealed class FakeStaffAuthorizationService : IStaffAuthorizationService
{
    public HashSet<string> Capabilities { get; } = new(StringComparer.Ordinal);

    /// <summary>Exactly what the given StaffRole group grants in production.</summary>
    public static FakeStaffAuthorizationService ForRole(string role) =>
        new FakeStaffAuthorizationService().Granting([.. RoleCapabilities.For(role)]);

    /// <summary>A signed-out member, or one whose staff profile is inactive.</summary>
    public static FakeStaffAuthorizationService Nobody() => new();

    public FakeStaffAuthorizationService Granting(params string[] capabilities)
    {
        foreach (var capability in capabilities)
        {
            Capabilities.Add(capability);
        }

        return this;
    }

    public FakeStaffAuthorizationService Without(params string[] capabilities)
    {
        foreach (var capability in capabilities)
        {
            Capabilities.Remove(capability);
        }

        return this;
    }

    public Task<bool> HasAsync(string capability) => Task.FromResult(Capabilities.Contains(capability));
}
