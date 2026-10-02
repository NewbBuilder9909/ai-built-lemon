using System.Text.Json;
using ProgrammePulse.Tests.Integration;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Personas;

/// <summary>
/// Phase 0 of the persona harness: one persona, one allowed journey, one
/// refused journey, against the real application over real HTTP.
///
/// Its purpose is to **measure**, not to cover. Three unknowns block the
/// authorization matrix (docs/persona-harness-build-gate.md):
///   B2 — what Forbid() actually returns under Umbraco's cookie schemes;
///   B4 — that antiforgery does not turn denials into 400s;
///   B1 — that six personas can sign in without the login limiter firing.
/// Everything measured here is written to
/// artifacts/persona-gate/phase0-observations.json so the matrix is written
/// against observed behaviour rather than an assumption.
///
/// Joins the integration collection: two hosts against one LocalDB race on
/// Umbraco's MainDom lock.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PersonaSpikeTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    // A skipped authorization gate must never read as a passing one (gate
    // document, B6). HasDatabaseOrFail now enforces that in-band: absent an
    // explicit PP_SKIP_SQL_TESTS=1, this fails rather than returning green.
    private const string EvidenceNotProduced = "no persona authorization gate was exercised over real HTTP.";

    /// <summary>
    /// A refusal must be a refusal. A 400 (missing antiforgery token) and a
    /// 429 (rate limiter) both look like "not allowed" to a careless
    /// assertion while proving nothing about authorization.
    /// </summary>
    private static void AssertRefused(string path, HttpResponseMessage response)
    {
        var outcome = PersonaSession.Outcome(response);

        Assert.False(outcome is AccessOutcome.Malformed, $"{path} returned 400 — antiforgery, not authorization. This assertion would prove nothing.");
        Assert.False(outcome is AccessOutcome.RateLimited, $"{path} returned 429 — the rate limiter fired, not the authorization check.");
        Assert.True(outcome is AccessOutcome.Denied, $"{path} expected a denial but observed {outcome} ({(int)response.StatusCode}).");
    }

    [Fact]
    public async Task Developer_reaches_their_own_portal_and_is_refused_the_cost_report()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var persona = NorthstarPersonas.Developer;
        var seeded = await new NorthstarPersonaSeeder(factory.Services).SeedWithEveryModuleOnAsync(NorthstarPersonas.Phase0);
        Assert.Equal(1, seeded);

        var session = await PersonaSignIn.SignInAsync(factory, persona);

        // Allowed journey: his own portal.
        var portal = await session.GetAsync("/staffops");
        Assert.Equal(AccessOutcome.Allowed, PersonaSession.Outcome(portal));
        Assert.Contains("My Leave Requests", await portal.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var myWork = await session.GetAsync("/staffops/my-work");
        Assert.Equal(AccessOutcome.Allowed, PersonaSession.Outcome(myWork));

        // Identity, not just authentication: submit leave and read it back.
        // The row is written against the signed-in member's own StaffKey, so
        // seeing it again proves the session is bound to Alex's profile —
        // which a status-code assertion alone would never establish.
        var marker = $"Phase 0 persona journey {Guid.NewGuid():N}";
        var submitted = await session.PostWithTokenAsync("/staffops", "/staffops/leave/submit", new Dictionary<string, string>
        {
            ["requestedFrom"] = "2026-10-01",
            ["requestedTo"] = "2026-10-02",
            ["type"] = "Holiday",
            ["notes"] = marker
        });

        Assert.NotNull(submitted);
        Assert.Equal(AccessOutcome.Redirected, PersonaSession.Outcome(submitted));

        var portalAfter = await session.GetAsync("/staffops");
        Assert.Contains(marker, await portalAfter.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Refused journeys, one per capability class the Developer must not hold.
        var refusals = new Dictionary<string, HttpResponseMessage>
        {
            ["/staffops/reporting/cost"] = await session.GetAsync("/staffops/reporting/cost"),
            ["/staffops/admin"] = await session.GetAsync("/staffops/admin"),
            ["/staffops/platform/tenants"] = await session.GetAsync("/staffops/platform/tenants"),
            ["/staffops/contracts"] = await session.GetAsync("/staffops/contracts")
        };

        foreach (var (path, response) in refusals)
        {
            AssertRefused(path, response);
        }

        // What a refused member actually lands on. Forbid() sends them to
        // /Account/AccessDenied, which is an ASP.NET Core Identity default —
        // this application never defines that route. Recorded rather than
        // asserted: it is a UX finding for the gate, not a security failure.
        var accessDenied = await session.GetAsync(PersonaSession.AccessDeniedPath);
        output.WriteLine($"{PersonaSession.AccessDeniedPath} -> {(int)accessDenied.StatusCode}");

        await RecordObservationsAsync(persona, portal, refusals, accessDenied);
    }

    /// <summary>
    /// B4 in isolation: posting without an antiforgery token must be
    /// distinguishable from being refused. If this test ever shows the same
    /// status for both, the matrix cannot tell authorization from a
    /// malformed request and must assert on response content as well.
    /// </summary>
    [Fact]
    public async Task A_post_without_an_antiforgery_token_is_distinguishable_from_a_denial()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        await new NorthstarPersonaSeeder(factory.Services).SeedWithEveryModuleOnAsync(NorthstarPersonas.Phase0);
        var session = await PersonaSignIn.SignInAsync(factory, NorthstarPersonas.Developer);

        // Alex may submit his own leave, so a refusal here can only come
        // from the missing token — which is exactly what we want to measure.
        var tokenless = await session.Client.PostAsync("/staffops/leave/submit", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["requestedFrom"] = "2026-10-01",
                ["requestedTo"] = "2026-10-02",
                ["type"] = "Holiday"
            }));

        var tokenlessOutcome = PersonaSession.Outcome(tokenless);
        output.WriteLine($"POST without token: {(int)tokenless.StatusCode} ({tokenlessOutcome})");

        Assert.False(
            tokenlessOutcome is AccessOutcome.Allowed,
            "A POST with no antiforgery token was accepted — antiforgery is not protecting this action.");
    }

    private async Task RecordObservationsAsync(
        PersonaDefinition persona,
        HttpResponseMessage allowed,
        Dictionary<string, HttpResponseMessage> refusals,
        HttpResponseMessage accessDenied)
    {
        var observations = new
        {
            measuredAtUtc = DateTime.UtcNow.ToString("O"),
            persona = persona.Key,
            memberGroup = persona.MemberGroup,
            allowedShape = new
            {
                path = "/staffops",
                status = (int)allowed.StatusCode,
                outcome = PersonaSession.Outcome(allowed).ToString()
            },
            accessDeniedLandingStatus = (int)accessDenied.StatusCode,
            denialShapes = refusals.Select(r => new
            {
                path = r.Key,
                status = (int)r.Value.StatusCode,
                outcome = PersonaSession.Outcome(r.Value).ToString(),
                location = r.Value.Headers.Location?.ToString()
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(json);

        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts", "persona-gate");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "phase0-observations.json"), json);
    }
}
