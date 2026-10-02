using ProgrammePulse.Tests.Integration;
using ProgrammePulse.Models.Staff;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Personas;

/// <summary>
/// The authorization matrix: every persona against every capability that has
/// a route, driven by the contract files in TestScenarios/Northstar and run
/// against the real application over real HTTP.
///
/// Two claims are proved per persona, and they are different claims:
///
///   Reachability — a persona reaches the routes for capabilities it holds
///   and is refused the routes for capabilities it does not.
///
///   Confidentiality — no response body shown to a persona without
///   ViewCommercials contains the seeded cost sentinel. A 200 on the
///   reporting hub says nothing about whether a rate leaked into the HTML,
///   which is why the status-code half alone would be a weaker guarantee
///   than it looks (gate document, B8).
///
/// One sign-in per persona, reused across that persona's assertions: signing
/// in per assertion would be slower and would spend the login rate limit for
/// no additional coverage.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PersonaAuthorizationMatrixTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the persona authorization matrix was never run, so neither reachability nor cost-sentinel confidentiality was checked.";

    public static TheoryData<string> PersonaKeys()
    {
        var data = new TheoryData<string>();
        foreach (var contract in PersonaContracts.All())
        {
            data.Add(contract.Persona);
        }

        return data;
    }

    private sealed record Failure(string Capability, CapabilityRoute Route, string Reason);

    [Theory]
    [MemberData(nameof(PersonaKeys))]
    public async Task Persona_reaches_exactly_what_its_contract_allows(string personaKey)
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var contract = PersonaContracts.All().Single(c => c.Persona == personaKey);
        var definition = PersonaContracts.Definition(contract);
        var routes = PersonaContracts.Routes().Capabilities.ToDictionary(c => c.Capability, StringComparer.Ordinal);

        var seeder = new NorthstarPersonaSeeder(factory.Services);
        await seeder.SeedWithEveryModuleOnAsync([definition]);
        await seeder.SeedCostSentinelAsync([definition]);

        var session = await PersonaSignIn.SignInAsync(factory, definition);
        var maySeeCost = contract.Can.Contains(Capability.ViewCommercials);
        var failures = new List<Failure>();

        foreach (var capability in Capability.All)
        {
            if (!routes.TryGetValue(capability, out var mapping) || mapping.Routes.Length == 0)
            {
                continue;
            }

            var shouldBeAllowed = contract.Can.Contains(capability);

            foreach (var route in mapping.Routes)
            {
                var response = await Request(session, route);
                if (response is null)
                {
                    // No token because the form page was refused. That is a
                    // consistent denial for a persona that should not hold the
                    // capability, and a genuine failure for one that should.
                    if (shouldBeAllowed)
                    {
                        failures.Add(new Failure(capability, route, "the form page needed for an antiforgery token was refused"));
                    }

                    continue;
                }

                var outcome = PersonaSession.Outcome(response);

                if (outcome is AccessOutcome.Malformed)
                {
                    failures.Add(new Failure(capability, route, "400 — antiforgery, not authorization; this assertion would prove nothing"));
                    continue;
                }

                if (outcome is AccessOutcome.RateLimited)
                {
                    failures.Add(new Failure(capability, route, "429 — the rate limiter fired, not the authorization check"));
                    continue;
                }

                if (outcome is AccessOutcome.ServerError)
                {
                    failures.Add(new Failure(capability, route,
                        $"{(int)response.StatusCode} - the application errored rather than making an access decision"));
                    continue;
                }

                if (shouldBeAllowed && outcome is AccessOutcome.Denied)
                {
                    failures.Add(new Failure(capability, route, $"refused, but the contract allows it ({(int)response.StatusCode})"));
                }
                else if (!shouldBeAllowed && outcome is not AccessOutcome.Denied)
                {
                    failures.Add(new Failure(capability, route, $"permitted ({outcome}, {(int)response.StatusCode}), but the contract forbids it"));
                }

                // Confidentiality, on every body this persona is served.
                if (!maySeeCost && response.Content.Headers.ContentType?.MediaType is not null)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    if (body.Contains(NorthstarPersonaSeeder.CostSentinel, StringComparison.Ordinal))
                    {
                        failures.Add(new Failure(capability, route,
                            $"response body leaked the cost sentinel {NorthstarPersonaSeeder.CostSentinel} to a persona without ViewCommercials"));
                    }
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"{contract.Persona} ({contract.MemberGroup}) failed {failures.Count} matrix assertion(s):\n"
            + string.Join("\n", failures.Select(f => $"  {f.Capability} [{f.Route}]: {f.Reason}")));
    }

    private static async Task<HttpResponseMessage?> Request(PersonaSession session, CapabilityRoute route) =>
        route.IsPost
            ? await session.PostWithTokenAsync(route.Form!, route.Path, route.Fields ?? [])
            : await session.GetAsync(route.Path);

    /// <summary>
    /// The positive control for the leak check, and the reason it is worth
    /// anything.
    ///
    /// "No persona without ViewCommercials sees the cost sentinel" passes
    /// trivially if the sentinel is never rendered to anybody — a leak
    /// detector wired to a value the application never emits detects nothing.
    /// This proves the string really does reach a page, for the one persona
    /// entitled to it, so the absence asserted everywhere else is meaningful.
    /// </summary>
    [Fact]
    public async Task The_cost_sentinel_really_is_visible_to_the_one_persona_entitled_to_it()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var admin = NorthstarPersonas.TenantAdmin;
        var seeder = new NorthstarPersonaSeeder(factory.Services);
        await seeder.SeedWithEveryModuleOnAsync([admin]);
        await seeder.SeedCostSentinelAsync([admin]);

        var session = await PersonaSignIn.SignInAsync(factory, admin);

        // The staff detail page is where a raw hourly rate is rendered, as
        // opposed to the cost summary, which reports derived totals.
        var detail = await session.GetAsync($"/staffops/admin/{admin.StaffKey}");
        Assert.Equal(AccessOutcome.Allowed, PersonaSession.Outcome(detail));

        var body = await detail.Content.ReadAsStringAsync();
        Assert.True(body.Contains(NorthstarPersonaSeeder.CostSentinel, StringComparison.Ordinal),
            $"The cost sentinel {NorthstarPersonaSeeder.CostSentinel} was not rendered to the tenant administrator. "
            + "Until it is, the matrix's leak assertions are vacuous — they would pass against an application that leaked a different value.");
    }

    /// <summary>
    /// The matrix above drives itself from the contract files, so a mistake
    /// that emptied them would make it pass trivially. This pins the amount
    /// of work it is actually doing.
    /// </summary>
    [Fact]
    public void The_matrix_covers_a_meaningful_number_of_routes()
    {
        var routed = PersonaContracts.Routes().Capabilities.Where(c => c.Routes.Length > 0).ToArray();
        var routeCount = routed.Sum(c => c.Routes.Length);

        Assert.True(routed.Length >= 15, $"Only {routed.Length} capabilities have routes; the matrix is thinner than expected.");
        Assert.True(routeCount >= 20, $"Only {routeCount} routes are exercised; the matrix is thinner than expected.");
        Assert.Equal(6, PersonaContracts.All().Count);
    }
}
