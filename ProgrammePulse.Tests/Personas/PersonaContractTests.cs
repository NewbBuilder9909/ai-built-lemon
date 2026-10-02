using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Tests.Personas;

/// <summary>
/// Checks the persona contract files against the application's own access
/// model. No HTTP, no database — these are the cheap, always-run guards that
/// keep the expectation files honest, so the expensive matrix in
/// PersonaAuthorizationMatrixTests only has to prove that the running
/// application matches them.
///
/// The forcing function is <see cref="Every_capability_is_decided_by_every_persona"/>:
/// a new capability cannot be added without someone stating, for each class
/// of user, whether they hold it.
/// </summary>
public class PersonaContractTests
{
    public static TheoryData<PersonaContract> Personas()
    {
        var data = new TheoryData<PersonaContract>();
        foreach (var contract in PersonaContracts.All())
        {
            data.Add(contract);
        }

        return data;
    }

    [Fact]
    public void All_six_personas_are_present_and_parse()
    {
        var contracts = PersonaContracts.All();

        Assert.Equal(6, contracts.Count);
        Assert.Equal(
            new[] { "Analyst", "Board", "Developer", "PlatformAdmin", "ProjectManager", "TenantAdmin" }.Order(),
            contracts.Select(c => c.Persona).Order());
    }

    [Theory]
    [MemberData(nameof(Personas))]
    public void Every_capability_is_decided_by_every_persona(PersonaContract contract)
    {
        var decided = contract.Can.Concat(contract.Cannot).ToArray();

        var undecided = Capability.All.Except(decided).ToArray();
        Assert.True(undecided.Length == 0,
            $"{contract.Persona} does not say whether it holds: {string.Join(", ", undecided)}. "
            + "Add each to 'can' or 'cannot' — a new capability needs a decision per persona, not a default.");

        var unknown = decided.Except(Capability.All).ToArray();
        Assert.True(unknown.Length == 0,
            $"{contract.Persona} names capabilities that do not exist: {string.Join(", ", unknown)}.");

        var contradictory = contract.Can.Intersect(contract.Cannot).ToArray();
        Assert.True(contradictory.Length == 0,
            $"{contract.Persona} lists as both allowed and forbidden: {string.Join(", ", contradictory)}.");
    }

    [Theory]
    [MemberData(nameof(Personas))]
    public void Declared_expectations_match_the_application_access_model(PersonaContract contract)
    {
        // RoleCapabilities is the truth; the file is the expectation. Naming
        // it that way round matters — if these disagree the application is
        // not wrong by definition, but somebody has to look.
        var actual = RoleCapabilities.For(contract.MemberGroup);

        var claimedButNotGranted = contract.Can.Where(c => !actual.Contains(c)).ToArray();
        Assert.True(claimedButNotGranted.Length == 0,
            $"{contract.Persona} expects {string.Join(", ", claimedButNotGranted)} but group '{contract.MemberGroup}' does not grant it.");

        var grantedButForbidden = contract.Cannot.Where(actual.Contains).ToArray();
        Assert.True(grantedButForbidden.Length == 0,
            $"{contract.Persona} forbids {string.Join(", ", grantedButForbidden)} but group '{contract.MemberGroup}' grants it. "
            + "Either the matrix widened access unintentionally, or the persona contract needs updating deliberately.");
    }

    [Theory]
    [MemberData(nameof(Personas))]
    public void Every_persona_forbids_something(PersonaContract contract)
    {
        // A persona with an empty 'cannot' list proves nothing: the matrix
        // would only ever assert allowances for it.
        Assert.NotEmpty(contract.Cannot);
    }

    [Fact]
    public void Every_persona_names_a_seeded_user_and_a_real_member_group()
    {
        foreach (var contract in PersonaContracts.All())
        {
            var definition = PersonaContracts.Definition(contract);
            Assert.Equal(definition.MemberGroup, contract.MemberGroup);
            Assert.Contains(contract.MemberGroup, StaffRole.Seeded);
        }
    }

    [Fact]
    public void Platform_administration_is_granted_to_exactly_one_persona_and_it_holds_nothing_else()
    {
        var contracts = PersonaContracts.All();

        var holders = contracts.Where(c => c.Can.Contains(Capability.ManagePlatform)).ToArray();
        Assert.Single(holders);
        Assert.Equal("PlatformAdmin", holders[0].Persona);

        // The section 3 determination, restated where a reviewer will see it:
        // the platform operator's contract allows one capability, full stop.
        Assert.Equal([Capability.ManagePlatform], holders[0].Can);
    }

    [Fact]
    public void Commercial_access_is_confined_to_the_tenant_administrator()
    {
        foreach (var contract in PersonaContracts.All().Where(c => c.Persona != "TenantAdmin"))
        {
            Assert.Contains(Capability.ViewCommercials, contract.Cannot);
        }
    }

    // ---- capability-routes.json ----

    [Fact]
    public void Every_capability_has_a_route_entry_or_a_stated_reason_it_has_none()
    {
        var routes = PersonaContracts.Routes();
        var mapped = routes.Capabilities.ToDictionary(c => c.Capability, StringComparer.Ordinal);

        var missing = Capability.All.Where(c => !mapped.ContainsKey(c)).ToArray();
        Assert.True(missing.Length == 0,
            $"capability-routes.json has no entry for: {string.Join(", ", missing)}.");

        foreach (var entry in routes.Capabilities)
        {
            if (entry.Routes.Length == 0)
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.NoRouteReason),
                    $"'{entry.Capability}' maps to no route and gives no reason. An untestable capability must be declared, not implied.");
            }
        }
    }

    [Fact]
    public void Every_post_route_names_a_form_to_take_its_antiforgery_token_from()
    {
        foreach (var entry in PersonaContracts.Routes().Capabilities)
        {
            foreach (var route in entry.Routes.Where(r => r.IsPost))
            {
                Assert.False(string.IsNullOrWhiteSpace(route.Form),
                    $"'{entry.Capability}' POSTs {route.Path} with no form to lift a token from. "
                    + "A token-less POST returns 400 and would be mistaken for a denial.");
            }
        }
    }

    [Fact]
    public void Route_entries_name_real_capabilities_only()
    {
        foreach (var entry in PersonaContracts.Routes().Capabilities)
        {
            Assert.Contains(entry.Capability, Capability.All);
        }
    }
}
