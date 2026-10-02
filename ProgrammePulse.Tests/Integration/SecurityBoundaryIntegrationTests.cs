using System.Net;
using ProgrammePulse.Tests.Personas;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Security;
using Umbraco.Cms.Web.Common.Security;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class SecurityBoundaryIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "no tenant-isolation or MFA boundary was exercised against a real database.";

    private bool Ready() => ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output);

    [Fact]
    public async Task Source_credentials_and_sync_state_are_isolated_in_real_database()
    {
        if (!Ready()) return;
        using var scope = factory.Services.CreateScope();
        var connections = scope.ServiceProvider.GetRequiredService<ISourceConnectionRepository>();
        var protector = scope.ServiceProvider.GetRequiredService<ISourceCredentialProtector>();
        var runs = scope.ServiceProvider.GetRequiredService<ISyncRunRepository>();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await connections.SetCredentialAsync(a, "ClickUp", protector.Protect(new SourceCredential("a-secret", "a")), now);
        Assert.Null(await connections.GetActiveForTenantAsync(b, "ClickUp"));
        await connections.SetCredentialAsync(b, "ClickUp", protector.Protect(new SourceCredential("b-secret", "b")), now);
        await connections.SetCredentialAsync(b, "ClickUp", null, now);
        var connection = await connections.GetActiveForTenantAsync(a, "ClickUp");
        Assert.Equal("a-secret", protector.Unprotect(connection!.ProtectedCredentialJson)!.ApiToken);
        var run = await runs.TryAcquireAsync(a, "ClickUp", "security-test", null, now, TimeSpan.FromMinutes(1));
        Assert.NotNull(run);
        try
        {
            Assert.Null(await runs.GetLatestAsync(b, "ClickUp"));
            Assert.Null(await runs.GetRunningAsync(b, "ClickUp"));
            Assert.False(await runs.HeartbeatAsync(run.RunKey, b, "ClickUp", "security-test", "forged", now, TimeSpan.FromMinutes(1)));
            Assert.False(await runs.CompleteAsync(run.RunKey, b, "ClickUp", "security-test", "forged", "{}", now));
            Assert.NotNull(await runs.GetRunningAsync(a, "ClickUp"));
        }
        finally
        {
            await runs.FailAsync(run.RunKey, "test", "test finished", now);
            await runs.ReleaseAsync(a, "ClickUp", "security-test", run.RunKey);
        }
    }

    [Fact]
    public async Task Invalid_mfa_cannot_create_a_member_session_but_valid_mfa_can()
    {
        if (!Ready()) return;
        var persona = NorthstarPersonas.TenantAdmin;
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([persona]);
        using var client = PersonaSignIn.CreateClient(factory);
        var session = new PersonaSession(client, persona.FullName);
        using var login = await session.PostWithTokenAsync(PersonaSignIn.LoginPath, PersonaSignIn.LoginPath,
            new Dictionary<string, string> { ["email"] = persona.Email, ["password"] = NorthstarPersonaSeeder.Password });
        Assert.Equal(PersonaSignIn.MfaChallengePath, login!.Headers.Location!.OriginalString);
        using var invalid = await session.PostWithTokenAsync(PersonaSignIn.MfaChallengePath, PersonaSignIn.MfaChallengePath,
            new Dictionary<string, string> { ["code"] = "invalid-code" });
        Assert.Equal(HttpStatusCode.OK, invalid!.StatusCode);
        using var denied = await client.GetAsync("/staffops/programme/connections");
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        using var valid = await session.PostWithTokenAsync(PersonaSignIn.MfaChallengePath, PersonaSignIn.MfaChallengePath,
            new Dictionary<string, string> { ["code"] = PersonaSignIn.CurrentTotpCode(persona.TotpSecret) });
        Assert.Equal(HttpStatusCode.Redirect, valid!.StatusCode);
        using var allowed = await client.GetAsync("/staffops/programme/connections");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Concurrent_recovery_code_redemption_succeeds_only_once()
    {
        if (!Ready()) return;
        var persona = NorthstarPersonas.TenantAdmin;
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([persona]);
        using var scope = factory.Services.CreateScope();
        var member = await scope.ServiceProvider.GetRequiredService<MemberManager>().FindByEmailAsync(persona.Email);
        var id = int.Parse(member!.Id);
        var repository = scope.ServiceProvider.GetRequiredService<IMfaRepository>();
        var hash = MfaRecoveryCodeGenerator.Hash("test-recovery-code");
        await repository.SetRecoveryCodeHashesAsync(id, [hash]);
        async Task<int?> Redeem()
        {
            using var attempt = factory.Services.CreateScope();
            return await attempt.ServiceProvider.GetRequiredService<IMfaRepository>().RedeemRecoveryCodeAsync(id, stored => MfaRecoveryCodeGenerator.Verify("test-recovery-code", stored), DateTime.UtcNow);
        }
        var results = await Task.WhenAll(Task.Run(Redeem), Task.Run(Redeem));
        Assert.Single(results, r => r.HasValue);
        Assert.Null(await Redeem());
    }

    [Fact]
    public async Task Account_deactivated_during_mfa_cannot_finish_signing_in()
    {
        if (!Ready()) return;
        var persona = NorthstarPersonas.TenantAdmin;
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([persona]);
        using var client = PersonaSignIn.CreateClient(factory);
        var session = new PersonaSession(client, persona.FullName);
        using var login = await session.PostWithTokenAsync(PersonaSignIn.LoginPath, PersonaSignIn.LoginPath,
            new Dictionary<string, string> { ["email"] = persona.Email, ["password"] = NorthstarPersonaSeeder.Password });
        Assert.Equal(PersonaSignIn.MfaChallengePath, login!.Headers.Location!.OriginalString);
        try
        {
            var token = await session.TryGetAntiForgeryTokenAsync(PersonaSignIn.MfaChallengePath);
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IStaffRepository>().AnonymizeAsync(persona.StaffKey, DateTime.UtcNow);
            using var result = await client.PostAsync(PersonaSignIn.MfaChallengePath, new FormUrlEncodedContent(
                new Dictionary<string, string> { ["code"] = PersonaSignIn.CurrentTotpCode(persona.TotpSecret), ["__RequestVerificationToken"] = token! }));
            Assert.Equal(PersonaSignIn.LoginPath, result!.Headers.Location!.OriginalString);
            using var denied = await client.GetAsync("/staffops/programme/connections");
            Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        }
        finally { await new NorthstarPersonaSeeder(factory.Services).SeedAsync([persona]); }
    }
}
