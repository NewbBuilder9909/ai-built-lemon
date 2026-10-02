using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;

namespace ProgrammePulse.Tests.Personas;

/// <summary>
/// The deterministic Northstar Digital cast. Fixed emails and staff keys so
/// a run is reproducible and a second run of the seeder updates the same
/// rows rather than creating a second set — see
/// docs/persona-harness-build-gate.md B7.
///
/// Phase 0 seeds <see cref="Developer"/> only; the remaining personas are
/// declared here so the seeder and the persona contract files grow against
/// one list rather than three.
///
/// Emails are on the reserved-for-testing .test TLD, which cannot resolve
/// publicly. Passwords are never declared here — see
/// <see cref="NorthstarPersonaSeeder.Password"/>.
/// </summary>
public sealed record PersonaDefinition(
    string Key,
    string FullName,
    string Email,
    string MemberGroup,
    Guid StaffKey,
    Guid TenantKey,
    string JobTitle,
    bool RequiresMfa)
{
    /// <summary>
    /// A Base32 TOTP secret for the personas that must clear an MFA challenge,
    /// generated once per test process unless PROGRAMMEPULSE_PERSONA_TOTP_SECRET
    /// supplies one, which is how a separate process (the accessibility audit)
    /// signs in as the same personas. The seeder resets the enrollment each
    /// run, so this is always the secret in force, and no secret-looking
    /// literal sits in source (Aikido: generic API key). Only ever used by
    /// .test accounts in a disposable database.
    /// </summary>
    public string TotpSecret { get; init; } = ProcessTotpSecret;

    private static readonly string ProcessTotpSecret =
        Environment.GetEnvironmentVariable("PROGRAMMEPULSE_PERSONA_TOTP_SECRET") is { Length: > 0 } configured
            ? configured
            : ProgrammePulse.Services.Security.TotpAuthenticator.GenerateSecret();

    public override string ToString() => Key;
}

public static class NorthstarPersonas
{
    /// <summary>The customer under test.</summary>
    public static readonly Guid NorthstarTenantKey = Tenant.DefaultTenantKey;

    /// <summary>
    /// The cross-tenant foil. Every persona in Northstar must be refused
    /// Meridian's data; nobody signs in as a Meridian member in Phase 0.
    /// </summary>
    public static readonly Guid MeridianTenantKey = Guid.Parse("b2c31d40-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

    public static readonly PersonaDefinition Developer = new(
        Key: "developer",
        FullName: "Alex Morgan",
        Email: "dev.alex@northstar.test",
        MemberGroup: StaffRole.Staff,
        StaffKey: Guid.Parse("a1000000-0000-4000-8000-000000000001"),
        TenantKey: NorthstarTenantKey,
        JobTitle: "Software Developer",
        RequiresMfa: false);

    public static readonly PersonaDefinition ProjectManager = new(
        Key: "project-manager",
        FullName: "Sarah Evans",
        Email: "pm.sarah@northstar.test",
        MemberGroup: StaffRole.TeamLead,
        StaffKey: Guid.Parse("a1000000-0000-4000-8000-000000000002"),
        TenantKey: NorthstarTenantKey,
        JobTitle: "Project Manager",
        RequiresMfa: false);

    public static readonly PersonaDefinition Board = new(
        Key: "board",
        FullName: "James Wilson",
        Email: "board.james@northstar.test",
        MemberGroup: StaffRole.Board,
        StaffKey: Guid.Parse("a1000000-0000-4000-8000-000000000003"),
        TenantKey: NorthstarTenantKey,
        JobTitle: "Non-Executive Director",
        RequiresMfa: false);

    public static readonly PersonaDefinition TenantAdmin = new(
        Key: "tenant-admin",
        FullName: "Emma Davies",
        Email: "admin.emma@northstar.test",
        MemberGroup: StaffRole.Admin,
        StaffKey: Guid.Parse("a1000000-0000-4000-8000-000000000004"),
        TenantKey: NorthstarTenantKey,
        JobTitle: "Operations Director",
        RequiresMfa: true);

    /// <summary>
    /// Decision 1 of 19 September 2026: read-only operational reporting,
    /// without the delivery write access that being made a Team Lead would
    /// have carried. Seedable now that StaffRole.Analyst exists and
    /// StaffGroupSeeder creates the group.
    /// </summary>
    public static readonly PersonaDefinition Analyst = new(
        Key: "analyst",
        FullName: "Priya Shah",
        Email: "analyst.priya@northstar.test",
        MemberGroup: StaffRole.Analyst,
        StaffKey: Guid.Parse("a1000000-0000-4000-8000-000000000005"),
        TenantKey: NorthstarTenantKey,
        JobTitle: "Delivery Analyst",
        RequiresMfa: false);

    /// <summary>
    /// Platform Admin is deliberately outside StaffRole.All, so it cannot be
    /// created through StaffOnboardingService — see the gate document, B5.
    ///
    /// Its tenant is deliberately Northstar's, which is the *adversarial*
    /// configuration rather than the realistic one: it makes the section 3
    /// determination as strong as it can be, by proving that even a platform
    /// operator whose staff profile sits inside the customer's own tenant is
    /// still refused that tenant's business data. A platform operator in a
    /// separate tenant is refused for the additional, weaker reason that the
    /// data is not theirs.
    ///
    /// Requires MFA: StaffAccountController extends the TOTP requirement to
    /// Platform Admin as well as Admin. SignInAsync asserts the redirect in
    /// both directions, so if that requirement is ever dropped this persona
    /// fails loudly instead of quietly signing in without a challenge.
    /// </summary>
    public static readonly PersonaDefinition PlatformAdmin = new(
        Key: "platform-admin",
        FullName: "Operator Platform Admin",
        Email: "platform.operator@programmepulse.test",
        MemberGroup: StaffRole.PlatformAdmin,
        StaffKey: Guid.Parse("a1000000-0000-4000-8000-000000000009"),
        TenantKey: NorthstarTenantKey,
        JobTitle: "Platform Operator",
        RequiresMfa: true);

    /// <summary>Everything Phase 0 seeds. Phase 2 extends this to the full cast.</summary>
    public static readonly IReadOnlyList<PersonaDefinition> Phase0 = [Developer];

    /// <summary>The full cast, all of which can now sign in.</summary>
    public static readonly IReadOnlyList<PersonaDefinition> Signable =
        [Developer, Analyst, ProjectManager, Board, TenantAdmin, PlatformAdmin];
}
