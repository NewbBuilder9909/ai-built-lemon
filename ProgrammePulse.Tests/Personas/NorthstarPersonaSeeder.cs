using Microsoft.Extensions.DependencyInjection;
using NPoco;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Security;
using ProgrammePulse.Services.Security;

namespace ProgrammePulse.Tests.Personas;

/// <summary>
/// Provisions the Northstar personas against the real application: real
/// Umbraco Members, real Member Group assignment, real StaffOps_Staff rows.
///
/// Lives in the test project only, so it cannot ship — the brief's
/// requirement that test credentials exist only in test environments is met
/// structurally rather than by a configuration flag.
///
/// Idempotent by email, and **state-resetting**: every run re-asserts the
/// password, the group membership and the profile fields. Find-or-create
/// alone is not enough, because a journey test that edits a persona would
/// otherwise poison the next run (gate document, B7).
///
/// Phase 0 deliberately creates members directly through MemberManager
/// rather than through StaffOnboardingService. That is the path
/// Platform Admin will need permanently (B5), and it avoids depending on
/// ITenantContext resolving during a seed where nobody is signed in. Phase 2
/// should route the five tenant personas through StaffOnboardingService so
/// that real onboarding is itself exercised.
/// </summary>
public sealed class NorthstarPersonaSeeder(IServiceProvider services)
{
    /// <summary>
    /// Read from PROGRAMMEPULSE_PERSONA_PASSWORD when set. The fallback is
    /// not a secret and is not a production default: it only ever reaches
    /// accounts on the reserved .test TLD in a disposable LocalDB database,
    /// and the seeder resets it on every run, so a leaked value grants
    /// nothing anywhere. A random per-run value cannot be used here because
    /// personas must remain signable-in across separate test processes.
    /// </summary>
    public static string Password =>
        Environment.GetEnvironmentVariable("PROGRAMMEPULSE_PERSONA_PASSWORD")
        ?? "Northstar-Persona-P@ssw0rd-1";

    /// <summary>
    /// A cost rate no real rate would ever be, seeded against every persona
    /// so it is present in the data a cost-bearing page would render. Any
    /// response body shown to a persona without ViewCommercials that contains
    /// this string is a confidentiality leak — see the gate document, B8.
    ///
    /// Reachability and confidentiality are different claims: a 200 on the
    /// reporting hub says nothing about whether a rate leaked into the HTML.
    /// </summary>
    public const string CostSentinel = "1337.77";

    /// <summary>
    /// Seeds the personas and switches every benched module on for their
    /// tenants, for tests of what each role can reach: they are about the
    /// role gates, and a benched module would refuse everyone alike. The
    /// demo seeder uses <see cref="SeedAsync"/>, so the demo shows the core.
    /// </summary>
    public async Task<int> SeedWithEveryModuleOnAsync(IEnumerable<PersonaDefinition> personas)
    {
        var list = personas.ToList();
        var seeded = await SeedAsync(list);
        foreach (var tenant in list.Select(p => p.TenantKey).Distinct())
        {
            await Integration.TestModules.SwitchOnForTestTenantAsync(services, tenant);
        }

        return seeded;
    }

    public async Task<int> SeedAsync(IEnumerable<PersonaDefinition> personas)
    {
        var seeded = 0;
        foreach (var persona in personas)
        {
            await SeedOneAsync(persona);
            seeded++;
        }

        return seeded;
    }

    /// <summary>
    /// Gives every seeded persona the sentinel hourly cost rate. Idempotent
    /// in effect: StaffRate is append-only history, so re-running adds a row
    /// with the same value rather than corrupting anything.
    /// </summary>
    public async Task SeedCostSentinelAsync(IEnumerable<PersonaDefinition> personas)
    {
        using var scope = services.CreateScope();
        var rates = scope.ServiceProvider.GetRequiredService<IStaffRateRepository>();
        var sentinel = decimal.Parse(CostSentinel, System.Globalization.CultureInfo.InvariantCulture);

        foreach (var persona in personas)
        {
            var current = await rates.GetCurrentAsync(persona.StaffKey);
            if (current?.CostPerHour == sentinel)
            {
                continue;
            }

            await rates.SetCurrentRateAsync(persona.StaffKey, sentinel, "GBP", persona.StaffKey);
        }
    }

    private async Task SeedOneAsync(PersonaDefinition persona)
    {
        using var scope = services.CreateScope();
        var memberManager = scope.ServiceProvider.GetRequiredService<MemberManager>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var staffRepository = scope.ServiceProvider.GetRequiredService<IStaffRepository>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var member = await memberManager.FindByEmailAsync(persona.Email);
        if (member is null)
        {
            var identityUser = MemberIdentityUser.CreateNew(persona.Email, persona.Email, "Member", true, persona.FullName);
            var created = await memberManager.CreateAsync(identityUser, Password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create persona '{persona.Key}': {string.Join("; ", created.Errors.Select(e => e.Description))}");
            }

            member = identityUser;
        }
        else
        {
            // Re-assert the password so a persona whose credentials were
            // changed by an earlier run can still sign in.
            var resetToken = await memberManager.GeneratePasswordResetTokenAsync(member);
            var reset = await memberManager.ResetPasswordAsync(member, resetToken, Password);
            if (!reset.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not reset persona '{persona.Key}' password: {string.Join("; ", reset.Errors.Select(e => e.Description))}");
            }
        }

        await EnsureOnlyGroupAsync(memberManager, member, persona.MemberGroup);

        var memberId = int.Parse(member.Id);
        var existingProfile = await staffRepository.GetByMemberIdAsync(memberId);
        if (existingProfile is null)
        {
            await staffRepository.CreateAsync(new StaffProfile
            {
                StaffKey = persona.StaffKey,
                MemberId = memberId,
                FullName = persona.FullName,
                Email = persona.Email,
                JobTitle = persona.JobTitle,
                Department = "Delivery",
                Team = "Northstar",
                DefaultWorkHoursPerWeek = 37.5m,
                TenantId = persona.TenantKey,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        else
        {
            // Restore the fixture after lifecycle tests (including anonymisation).
            // The previous find-or-create left inactive profiles behind between runs.
            using var databaseScope = scope.ServiceProvider
                .GetRequiredService<Umbraco.Cms.Infrastructure.Scoping.IScopeProvider>().CreateScope();
            await databaseScope.Database.ExecuteAsync(new Sql(
                "UPDATE StaffOps_Staff SET fullName=@0, email=@1, jobTitle=@2, department=@3, team=@4, isActive=@5, tenantId=@6, defaultWorkHoursPerWeek=@7 WHERE memberId=@8",
                persona.FullName, persona.Email, persona.JobTitle, "Delivery", "Northstar", true, persona.TenantKey, 37.5m, memberId));
            databaseScope.Complete();
        }

        await EnsureMfaAsync(scope.ServiceProvider, persona, memberId, now);

        // Unapproved or locked-out members cannot sign in; an earlier run's
        // failed-password test may have locked one.
        var backing = memberService.GetById(memberId);
        if (backing is not null && (!backing.IsApproved || backing.IsLockedOut))
        {
            backing.IsApproved = true;
            backing.IsLockedOut = false;
            backing.FailedPasswordAttempts = 0;
            memberService.Save(backing);
        }
    }

    /// <summary>
    /// Flips the persona's staff profile active flag directly in SQL.
    ///
    /// There is no production "deactivate a member" API yet - StaffOps only
    /// has GDPR anonymisation, which would destroy the persona - so this
    /// writes the column the authorization check reads. The point is to
    /// exercise StaffAuthorizationService's active-profile requirement, which
    /// nothing else covers.
    /// </summary>
    public async Task SetProfileActiveAsync(PersonaDefinition persona, bool isActive)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(
            Integration.ProgrammePulseWebApplicationFactory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE StaffOps_Staff SET isActive = @active WHERE staffKey = @key";
        command.Parameters.AddWithValue("@active", isActive);
        command.Parameters.AddWithValue("@key", persona.StaffKey);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Brings the persona's TOTP enrollment to a known state.
    ///
    /// Resets first, then re-enrolls with the persona's fixed secret, because
    /// StartEnrollmentAsync deliberately returns an existing row unchanged —
    /// without the reset, a secret issued by an earlier run (or by a test of
    /// the real enrollment flow) would persist and every computed code would
    /// be wrong. Same state-resetting principle as the password and the group.
    ///
    /// Personas that must not have MFA are reset too, so a persona that was
    /// enrolled by a previous run does not silently gain a challenge step.
    /// </summary>
    private static async Task EnsureMfaAsync(IServiceProvider scoped, PersonaDefinition persona, int memberId, DateTime now)
    {
        var mfaRepository = scoped.GetRequiredService<IMfaRepository>();

        await mfaRepository.ResetAsync(memberId);

        if (!persona.RequiresMfa)
        {
            return;
        }

        var protection = scoped.GetRequiredService<ProgrammePulse.Services.Security.MfaSecretProtection>();
        var credential = await mfaRepository.StartEnrollmentAsync(memberId, protection.Protect(memberId, persona.TotpSecret), now);
        // Fixture enrollment consumes an older step; the real HTTP sign-in must consume the current step.
        await mfaRepository.TryAcceptTotpAsync(credential, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 - 2, now, []);
    }

    /// <summary>
    /// A persona is in exactly one group. Removing the others matters: a
    /// persona left in a group by a previous run's journey test would make
    /// its "cannot" expectations pass or fail for the wrong reason.
    /// </summary>
    private static async Task EnsureOnlyGroupAsync(MemberManager memberManager, MemberIdentityUser member, string group)
    {
        var currentRoles = await memberManager.GetRolesAsync(member);

        foreach (var stale in currentRoles.Where(r => !string.Equals(r, group, StringComparison.Ordinal)))
        {
            await memberManager.RemoveFromRoleAsync(member, stale);
        }

        if (!currentRoles.Contains(group, StringComparer.Ordinal))
        {
            var added = await memberManager.AddToRoleAsync(member, group);
            if (!added.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not add persona to group '{group}': {string.Join("; ", added.Errors.Select(e => e.Description))}");
            }
        }
    }
}
