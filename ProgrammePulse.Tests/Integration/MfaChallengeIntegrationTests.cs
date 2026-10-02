using System.Net;
using ProgrammePulse.Tests.Personas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Services.Security;
using ProgrammePulse.Services.Staff;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Cms.Web.Common.Security;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class MfaChallengeIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "no MFA challenge, enrollment or replay path was exercised over real HTTP.";

    private static readonly PersonaDefinition Persona = NorthstarPersonas.TenantAdmin;

    private async Task<(HttpClient Client, string[] Cookies)> StartAsync(bool enrollment = false)
    {
        var client = PersonaSignIn.CreateClient(factory);
        var session = new PersonaSession(client, Persona.FullName);
        using var response = await session.PostWithTokenAsync(PersonaSignIn.LoginPath, PersonaSignIn.LoginPath,
            new Dictionary<string, string> { ["email"] = Persona.Email, ["password"] = NorthstarPersonaSeeder.Password });
        Assert.Equal(enrollment ? "/staffops/account/mfa/enroll" : PersonaSignIn.MfaChallengePath, response!.Headers.Location!.OriginalString);
        var cookies = response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])
            .GroupBy(c => c.Split('=')[0]).Select(g => g.Last()).Where(c => !c.EndsWith('=')).ToArray();
        return (client, cookies);
    }

    private async Task<HttpResponseMessage> ReplayAsync(IEnumerable<string> cookies)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
        return await client.GetAsync(PersonaSignIn.MfaChallengePath);
    }

    [Fact]
    public async Task Enrollment_shows_seed_only_before_confirmation_then_signs_in_and_consumes_challenge()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        var seeder = new NorthstarPersonaSeeder(factory.Services);
        await seeder.SeedAsync([Persona]);
        using var scope = factory.Services.CreateScope();
        var member = await scope.ServiceProvider.GetRequiredService<MemberManager>().FindByEmailAsync(Persona.Email);
        await scope.ServiceProvider.GetRequiredService<IMfaRepository>().ResetAsync(int.Parse(member!.Id));
        try
        {
            var pending = await StartAsync(enrollment: true);
            using var browser = pending.Client;
            var session = new PersonaSession(browser, Persona.FullName);
            const string path = "/staffops/account/mfa/enroll";
            using var form = await browser.GetAsync(path);
            var html = await form.Content.ReadAsStringAsync();
            var secret = System.Text.RegularExpressions.Regex.Match(html, "<code[^>]*>([A-Z2-7]+)</code>").Groups[1].Value;
            Assert.Equal(32, secret.Length);
            Assert.True(form.Headers.CacheControl!.NoStore);
            using var invalid = await session.PostWithTokenAsync(path, path, new Dictionary<string, string> { ["code"] = "invalid" });
            Assert.Equal(HttpStatusCode.OK, invalid!.StatusCode);
            using var confirmed = await session.PostWithTokenAsync(path, path, new Dictionary<string, string> { ["code"] = PersonaSignIn.CurrentTotpCode(secret) });
            Assert.Equal(HttpStatusCode.OK, confirmed!.StatusCode);
            var confirmedHtml = await confirmed.Content.ReadAsStringAsync();
            Assert.Contains("Save your recovery codes", confirmedHtml);
            Assert.DoesNotContain(secret, confirmedHtml);
            using var allowed = await browser.GetAsync("/staffops/programme/connections");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            using var replay = await ReplayAsync(pending.Cookies);
            Assert.Equal(PersonaSignIn.LoginPath, replay.Headers.Location?.OriginalString);
        }
        finally { await seeder.SeedAsync([Persona]); }
    }

    [Fact]
    public async Task Recovery_login_consumes_its_challenge_and_the_code_cannot_sign_in_again()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([Persona]);
        using var scope = factory.Services.CreateScope();
        var member = await scope.ServiceProvider.GetRequiredService<MemberManager>().FindByEmailAsync(Persona.Email);
        const string recovery = "fixture-recovery-code";
        await scope.ServiceProvider.GetRequiredService<IMfaRepository>().SetRecoveryCodeHashesAsync(int.Parse(member!.Id), [MfaRecoveryCodeGenerator.Hash(recovery)]);
        const string path = "/staffops/account/mfa/challenge/recovery";
        var pending = await StartAsync();
        using var first = pending.Client;
        using var success = await new PersonaSession(first, Persona.FullName).PostWithTokenAsync(path, path, new Dictionary<string, string> { ["code"] = recovery });
        Assert.Equal(HttpStatusCode.OK, success!.StatusCode);
        using var allowed = await first.GetAsync("/staffops/programme/connections");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var replay = await ReplayAsync(pending.Cookies);
        Assert.Equal(PersonaSignIn.LoginPath, replay.Headers.Location?.OriginalString);
        var next = await StartAsync();
        using var second = next.Client;
        using var rejected = await new PersonaSession(second, Persona.FullName).PostWithTokenAsync(path, path, new Dictionary<string, string> { ["code"] = recovery });
        Assert.Equal(HttpStatusCode.OK, rejected!.StatusCode);
        Assert.Contains("invalid or has already been used", await rejected.Content.ReadAsStringAsync());
        using var denied = await second.GetAsync("/staffops/programme/connections");
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
    }

    [Theory]
    [InlineData("mfa-reset")]
    [InlineData("password-reset")]
    [InlineData("staff-deactivation")]
    [InlineData("member-deactivation")]
    public async Task Security_changes_revoke_all_pending_browsers_even_after_reactivation(string change)
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        var seeder = new NorthstarPersonaSeeder(factory.Services);
        await seeder.SeedAsync([Persona]);
        var first = await StartAsync();
        var second = await StartAsync();
        using var firstClient = first.Client;
        using var secondClient = second.Client;
        using var scope = factory.Services.CreateScope();
        var memberManager = scope.ServiceProvider.GetRequiredService<MemberManager>();
        var member = await memberManager.FindByEmailAsync(Persona.Email);
        var id = int.Parse(member!.Id);
        try
        {
            using var before = await ReplayAsync(first.Cookies);
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
            switch (change)
            {
                case "mfa-reset":
                    await scope.ServiceProvider.GetRequiredService<IMfaRepository>().ResetAsync(id);
                    break;
                case "password-reset":
                    var token = await memberManager.GeneratePasswordResetTokenAsync(member);
                    Assert.True((await memberManager.ResetPasswordAsync(member, token, "Changed-Test-P@ssw0rd-2")).Succeeded);
                    break;
                case "staff-deactivation":
                    await scope.ServiceProvider.GetRequiredService<IStaffRepository>().AnonymizeAsync(Persona.StaffKey, DateTime.UtcNow);
                    await seeder.SetProfileActiveAsync(Persona, true);
                    break;
                case "member-deactivation":
                    var members = scope.ServiceProvider.GetRequiredService<IMemberService>();
                    var backing = members.GetById(id)!;
                    backing.IsApproved = false;
                    members.Save(backing);
                    backing.IsApproved = true;
                    members.Save(backing);
                    break;
            }
            using var a = await ReplayAsync(first.Cookies);
            using var b = await ReplayAsync(second.Cookies);
            Assert.Equal(PersonaSignIn.LoginPath, a.Headers.Location?.OriginalString);
            Assert.Equal(PersonaSignIn.LoginPath, b.Headers.Location?.OriginalString);
            using var db = scope.ServiceProvider.GetRequiredService<IScopeProvider>().CreateScope(autoComplete: true);
            Assert.Empty(await db.Database.FetchAsync<MemberMfaChallengeDto>(Sql.Builder.Where("memberId=@0", id)));
        }
        finally { await seeder.SeedAsync([Persona]); }
    }

    [Fact]
    public async Task Copying_pending_cookie_without_the_matching_browser_binding_is_rejected()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([Persona]);
        var first = await StartAsync();
        var second = await StartAsync();
        using var a = first.Client;
        using var b = second.Client;
        var pending = Assert.Single(first.Cookies, c => c.StartsWith("ops-mfa-pending="));
        var otherBinding = Assert.Single(second.Cookies, c => c.StartsWith(MfaChallengeStore.BindingCookieName + "="));
        using var missing = await ReplayAsync([pending]);
        using var mismatched = await ReplayAsync([pending, otherBinding]);
        using var original = await ReplayAsync(first.Cookies);
        Assert.Equal(PersonaSignIn.LoginPath, missing.Headers.Location?.OriginalString);
        Assert.Equal(PersonaSignIn.LoginPath, mismatched.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
    }

    [Fact]
    public async Task Challenge_consumption_is_atomic_and_copied_cookies_cannot_replay_after_consumption()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([Persona]);
        var pending = await StartAsync();
        using var browser = pending.Client;
        using var setup = factory.Services.CreateScope();
        var member = await setup.ServiceProvider.GetRequiredService<MemberManager>().FindByEmailAsync(Persona.Email);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            using var scope = factory.Services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Path = PersonaSignIn.MfaChallengePath;
            context.Request.Scheme = "http";
            context.Request.Headers.Cookie = string.Join("; ", pending.Cookies);
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
            return await scope.ServiceProvider.GetRequiredService<IMfaChallengeStore>().TryConsumeAsync(context, member!.Id);
        })).ToArray();
        gate.SetResult();
        Assert.Single(await Task.WhenAll(attempts), consumed => consumed);
        using var replay = await ReplayAsync(pending.Cookies);
        Assert.Equal(PersonaSignIn.LoginPath, replay.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Expired_or_stale_security_stamp_challenges_fail_closed(bool expired)
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([Persona]);
        var pending = await StartAsync();
        using var browser = pending.Client;
        using var scope = factory.Services.CreateScope();
        var member = await scope.ServiceProvider.GetRequiredService<MemberManager>().FindByEmailAsync(Persona.Email);
        using (var db = scope.ServiceProvider.GetRequiredService<IScopeProvider>().CreateScope())
        {
            if (expired) await db.Database.ExecuteAsync($"UPDATE {MemberMfaChallengeDto.TableName} SET expiresAtUtc=@0 WHERE memberId=@1", DateTime.UtcNow.AddMinutes(-1), int.Parse(member!.Id));
            else await db.Database.ExecuteAsync($"UPDATE {MemberMfaChallengeDto.TableName} SET securityStamp=@0 WHERE memberId=@1", "obsolete-stamp", int.Parse(member!.Id));
            db.Complete();
        }
        using var replay = await ReplayAsync(pending.Cookies);
        Assert.Equal(PersonaSignIn.LoginPath, replay.Headers.Location?.OriginalString);
    }
}
