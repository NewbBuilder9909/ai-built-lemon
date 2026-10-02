using System.Net;
using System.Text.Json;
using ProgrammePulse.Tests.Personas;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ExecutiveReview;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Tests.Integration;

public sealed partial class ExecutiveReviewIntegrationTests
{
    [Fact]
    public async Task Decision_stale_and_unmapped_evidence_cannot_be_accepted_or_decided_in_storage()
    {
        if (!Database()) return;
        var data = await DecisionSetupAsync();
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var repository = provider.GetRequiredService<IExecutiveDecisionRepository>();
        var later = new ExecutiveDecisionRepository(provider.GetRequiredService<IScopeProvider>(),
            provider.GetRequiredService<IExecutivePackRepository>(), provider.GetRequiredService<IStaffRepository>(),
            new DecisionClock(new DateTimeOffset(data.Pack.PublishedAtUtc, TimeSpan.Zero).AddHours(49)));
        await Assert.ThrowsAsync<ReviewValidationException>(() => later.ApplyAsync(data.Actor, data.Draft.DecisionKey, 1,
            new(DecisionAction.AcceptEvidence, "Expired"), 48));
        await repository.ApplyAsync(data.Actor, data.Draft.DecisionKey, 1, new(DecisionAction.AcceptEvidence, "Fresh at review"), 48);
        await Assert.ThrowsAsync<ReviewValidationException>(() => later.ApplyAsync(data.Actor, data.Draft.DecisionKey, 2,
            DecisionCommandFor(data.Owner.StaffKey), 48));
        Assert.Equal(2, (await repository.GetHistoryAsync(data.Actor.TenantId, data.Draft.DecisionKey)).Count);

        var item = await SeedSourceAsync(data.Actor.TenantId);
        await provider.GetRequiredService<IProgrammeRepository>().UpsertWorkItemAsync(item with { Stage = WorkItemLifecycleStage.Unmapped }, data.Actor.TenantId);
        var unmapped = await provider.GetRequiredService<IExecutivePackRepository>().CaptureAsync(data.Actor, "ClickUp", 48);
        var draft = await repository.CreateAsync(data.Actor, data.Draft.Definition with { PackKey = unmapped.PackKey, WorkItemKey = item.WorkItemKey });
        var error = await Assert.ThrowsAsync<ReviewValidationException>(() => repository.ApplyAsync(data.Actor, draft.DecisionKey, 1,
            new(DecisionAction.AcceptEvidence, "Cannot verify"), 48));
        Assert.Equal("Decision.UnmappedEvidence", error.Message);
        Assert.Single(await repository.GetHistoryAsync(data.Actor.TenantId, draft.DecisionKey));
    }

    [Fact]
    public async Task Decision_journal_preserves_lifecycle_history_and_original_pack()
    {
        if (!Database()) return;
        var data = await DecisionSetupAsync();
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IExecutiveDecisionRepository>();
        var original = JsonSerializer.Serialize(data.Pack);
        var current = await repository.ApplyAsync(data.Actor, data.Draft.DecisionKey, 1, new(DecisionAction.AcceptEvidence, "Reviewed source"), 48);
        current = await repository.ApplyAsync(data.Actor, current.DecisionKey, 2, DecisionCommandFor(data.Owner.StaffKey), 48);
        current = await repository.ApplyAsync(data.Actor, current.DecisionKey, 3, new(DecisionAction.Close, "Follow-up", Outcome: "Delivery recovered"), 48);
        current = await repository.ApplyAsync(data.Actor, current.DecisionKey, 4, new(DecisionAction.Reopen, "New information"), 48);
        var replacement = await scope.ServiceProvider.GetRequiredService<IExecutivePackRepository>().CaptureAsync(data.Actor, "ClickUp", 48);
        current = await repository.ApplyAsync(data.Actor, current.DecisionKey, 5,
            new(DecisionAction.Revise, "New publication", current.Definition with { PackKey = replacement.PackKey }), 48);
        var history = await repository.GetHistoryAsync(data.Actor.TenantId, current.DecisionKey);
        Assert.Equal(Enumerable.Range(1, 6), history.Select(r => r.Version));
        Assert.Equal("Delivery recovered", history[3].Outcome);
        Assert.Equal(data.Pack.PackKey, history[0].Definition.PackKey);
        Assert.Equal(replacement.PackKey, current.Definition.PackKey);
        Assert.Equal(EvidenceDisposition.Unreviewed, current.Evidence);
        Assert.Equal(original, JsonSerializer.Serialize(await scope.ServiceProvider.GetRequiredService<IExecutivePackRepository>().GetAsync(data.Actor.TenantId, data.Pack.PackKey)));
        Assert.Single(await repository.GetQueueAsync(data.Actor.TenantId, DecisionStatus.Draft));
        Assert.Empty(await repository.GetQueueAsync(data.Actor.TenantId, DecisionStatus.Closed));
    }

    [Fact]
    public async Task Decision_concurrent_updates_have_one_winner_and_stale_versions_do_not_append()
    {
        if (!Database()) return;
        var data = await DecisionSetupAsync();
        async Task<bool> Update(DecisionAction action)
        {
            using var scope = factory.Services.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IExecutiveDecisionRepository>()
                    .ApplyAsync(data.Actor, data.Draft.DecisionKey, 1, new(action, "Concurrent update"), 48);
                return true;
            }
            catch (ReviewConflictException) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Update(DecisionAction.AcceptEvidence)), Task.Run(() => Update(DecisionAction.DisputeEvidence)));
        Assert.Single(results, result => result);
        using var verify = factory.Services.CreateScope();
        var history = await verify.ServiceProvider.GetRequiredService<IExecutiveDecisionRepository>().GetHistoryAsync(data.Actor.TenantId, data.Draft.DecisionKey);
        Assert.Equal(new[] { 1, 2 }, history.Select(r => r.Version));
    }

    [Fact]
    public async Task Decision_references_reject_foreign_packs_records_and_owners_without_appending()
    {
        if (!Database()) return;
        var data = await DecisionSetupAsync();
        var foreign = await DecisionSetupAsync();
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IExecutiveDecisionRepository>();
        await Assert.ThrowsAsync<ReviewNotFoundException>(() => repository.CreateAsync(data.Actor, data.Draft.Definition with { PackKey = foreign.Pack.PackKey }));
        await Assert.ThrowsAsync<ReviewValidationException>(() => repository.CreateAsync(data.Actor, data.Draft.Definition with { WorkItemKey = foreign.Pack.Items[0].WorkItemKey }));
        await Assert.ThrowsAsync<ReviewValidationException>(() => repository.CreateAsync(data.Actor, data.Draft.Definition with { OwnerStaffKey = foreign.Owner.StaffKey }));
        Assert.Empty(await repository.GetHistoryAsync(foreign.Actor.TenantId, data.Draft.DecisionKey));
        await Assert.ThrowsAsync<ReviewNotFoundException>(() => repository.ApplyAsync(foreign.Actor, data.Draft.DecisionKey, 1, new(DecisionAction.Dismiss, "forged"), 48));
        await repository.ApplyAsync(data.Actor, data.Draft.DecisionKey, 1, new(DecisionAction.AcceptEvidence, "checked"), 48);
        await Assert.ThrowsAsync<ReviewValidationException>(() => repository.ApplyAsync(data.Actor, data.Draft.DecisionKey, 2, DecisionCommandFor(foreign.Owner.StaffKey), 48));
        await scope.ServiceProvider.GetRequiredService<IStaffRepository>().AnonymizeAsync(data.Owner.StaffKey, DateTime.UtcNow);
        await Assert.ThrowsAsync<ReviewValidationException>(() => repository.ApplyAsync(data.Actor, data.Draft.DecisionKey, 2, DecisionCommandFor(data.Owner.StaffKey), 48));
        Assert.Equal(2, (await repository.GetHistoryAsync(data.Actor.TenantId, data.Draft.DecisionKey)).Count);
        Assert.Single(await repository.GetQueueAsync(data.Actor.TenantId, null));
    }

    [Fact]
    public async Task Decision_manager_can_complete_rendered_journey_and_refresh_pack_in_Welsh()
    {
        if (!Database()) return;
        using var enabled = Enable();
        var data = await DecisionSetupAsync();
        var session = await PersonaSignIn.SignInAsync(factory, data.Owner);
        var fields = DecisionFields(data.Draft.Definition);
        fields["Input.Title"] = "<script>alert('unsafe')</script>";
        var created = await session.PostWithTokenAsync($"/staffops/executive/decisions/new?packKey={data.Pack.PackKey}", "/staffops/executive/decisions/create", fields);
        Assert.NotNull(created);
        Assert.Equal(AccessOutcome.Redirected, PersonaSession.Outcome(created));
        var path = created.Headers.Location!.ToString();
        var page = await session.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("&lt;script&gt;", await page.Content.ReadAsStringAsync());
        Assert.DoesNotContain("<script>alert", await page.Content.ReadAsStringAsync());
        var review = await session.PostWithTokenAsync(path, path + "/act", new Dictionary<string, string>
        { ["ExpectedVersion"] = "1", ["Action"] = "AcceptEvidence", ["Note"] = "Reviewed source" });
        Assert.Equal(AccessOutcome.Redirected, PersonaSession.Outcome(review!));
        var decide = await session.PostWithTokenAsync(path, path + "/act", new Dictionary<string, string>
        {
            ["ExpectedVersion"] = "2", ["Action"] = "Decide", ["Note"] = "Committee record",
            ["Decision"] = "Proceed", ["Rationale"] = "Customer exposure", ["ActionOwnerStaffKey"] = data.Owner.StaffKey.ToString(), ["FollowUpOn"] = "2026-10-31"
        });
        Assert.Equal(AccessOutcome.Redirected, PersonaSession.Outcome(decide!));
        var close = await session.PostWithTokenAsync(path, path + "/act", new Dictionary<string, string>
        { ["ExpectedVersion"] = "3", ["Action"] = "Close", ["Note"] = "Completed", ["Outcome"] = "Recovered" });
        Assert.Equal(AccessOutcome.Redirected, PersonaSession.Outcome(close!));
        var reopen = await session.PostWithTokenAsync(path, path + "/act", new Dictionary<string, string>
        { ["ExpectedVersion"] = "4", ["Action"] = "Reopen", ["Note"] = "Fresh evidence" });
        Assert.Equal(AccessOutcome.Redirected, PersonaSession.Outcome(reopen!));
        using var scope = factory.Services.CreateScope();
        var replacement = await scope.ServiceProvider.GetRequiredService<IExecutivePackRepository>().CaptureAsync(data.Actor, "ClickUp", 48);
        await session.GetAsync("/language?culture=cy-GB&formatCulture=en-IE&returnUrl=/staffops/executive/decisions");
        var editor = await session.GetAsync(path + $"/edit?packKey={replacement.PackKey}");
        Assert.Equal(HttpStatusCode.OK, editor.StatusCode);
        var html = await editor.Content.ReadAsStringAsync();
        Assert.Contains("Diwygio", html);
        Assert.Contains(replacement.PackKey.ToString(), html);
        Assert.DoesNotContain("Decision.", html);
        fields["Input.PackKey"] = replacement.PackKey.ToString();
        fields["expectedVersion"] = "5";
        fields["note"] = "New evidence";
        var revised = await session.PostWithTokenAsync(path + "/edit", path + "/revise", fields);
        Assert.Equal(AccessOutcome.Redirected, PersonaSession.Outcome(revised!));
        var download = await session.GetAsync(path + "/download");
        var journal = JsonSerializer.Deserialize<DecisionRevision[]>(await download.Content.ReadAsStringAsync())!;
        Assert.Equal(6, journal.Length);
        Assert.Equal(new DateOnly(2026, 10, 31), journal[2].FollowUpOn);
        Assert.Equal(replacement.PackKey, journal[^1].Definition.PackKey);
        Assert.Equal(data.Pack.PackKey, journal[0].Definition.PackKey);
        Assert.True(download.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/staffops/executive/decisions?status=Draft")).StatusCode);
    }

    [Fact]
    public async Task Decision_invalid_and_stale_forms_preserve_input_and_do_not_mutate_history()
    {
        if (!Database()) return;
        using var enabled = Enable();
        var data = await DecisionSetupAsync();
        var session = await PersonaSignIn.SignInAsync(factory, data.Owner);
        var path = $"/staffops/executive/decisions/{data.Draft.DecisionKey}";
        var fields = new Dictionary<string, string> { ["ExpectedVersion"] = "1", ["Action"] = "Decide", ["Note"] = "Cannot skip review" };
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PostWithTokenAsync(path, path + "/act", fields))!.StatusCode);
        fields["Action"] = "999";
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PostWithTokenAsync(path, path + "/act", fields))!.StatusCode);
        fields["Action"] = "AcceptEvidence";
        Assert.Equal(AccessOutcome.Redirected, PersonaSession.Outcome((await session.PostWithTokenAsync(path, path + "/act", fields))!));
        var stale = await session.PostWithTokenAsync(path, path + "/act", fields);
        Assert.Equal(HttpStatusCode.Conflict, stale!.StatusCode);
        Assert.Contains("Cannot skip review", await stale.Content.ReadAsStringAsync());
        Assert.Contains("value=\"1\"", await stale.Content.ReadAsStringAsync());
        var edit = DecisionFields(data.Draft.Definition);
        edit["expectedVersion"] = "1";
        edit["note"] = "Conflicting revision";
        Assert.Equal(HttpStatusCode.Conflict, (await session.PostWithTokenAsync(path + "/edit", path + "/revise", edit))!.StatusCode);
        edit["expectedVersion"] = "2";
        edit["Input.DueOn"] = "not-a-date";
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PostWithTokenAsync(path + "/edit", path + "/revise", edit))!.StatusCode);
        var noToken = await session.Client.PostAsync(path + "/act", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(2, (await scope.ServiceProvider.GetRequiredService<IExecutiveDecisionRepository>().GetHistoryAsync(data.Actor.TenantId, data.Draft.DecisionKey)).Count);
    }

    [Fact]
    public async Task Decision_board_can_read_but_all_mutations_are_denied_with_valid_antiforgery()
    {
        if (!Database()) return;
        using var enabled = Enable();
        var data = await DecisionSetupAsync();
        var board = await PersonaAsync(NorthstarPersonas.Board, data.Actor.TenantId);
        var session = await PersonaSignIn.SignInAsync(factory, board);
        var path = $"/staffops/executive/decisions/{data.Draft.DecisionKey}";
        foreach (var route in new[] { "/staffops/executive/decisions", path, path + "/download" })
            Assert.Equal(HttpStatusCode.OK, (await session.GetAsync(route)).StatusCode);
        var html = await (await session.GetAsync(path)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("name=\"Action\"", html);
        Assert.DoesNotContain(NorthstarPersonaSeeder.CostSentinel, html);
        foreach (var route in new[] { path + "/edit", $"/staffops/executive/decisions/new?packKey={data.Pack.PackKey}" })
            Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(await session.GetAsync(route)));
        var draft = DecisionFields(data.Draft.Definition);
        draft["expectedVersion"] = "1";
        draft["note"] = "forged";
        foreach (var route in new[] { "/staffops/executive/decisions/create", path + "/revise" })
            Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome((await session.PostWithTokenAsync(path, route, draft))!));
        foreach (var action in Enum.GetValues<DecisionAction>())
        {
            var response = await session.PostWithTokenAsync(path, path + "/act", new Dictionary<string, string>
            { ["ExpectedVersion"] = "1", ["Action"] = action.ToString(), ["Note"] = "forged" });
            Assert.NotNull(response);
            Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(response));
        }
        using var scope = factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<IExecutiveDecisionRepository>().GetHistoryAsync(data.Actor.TenantId, data.Draft.DecisionKey));
        await scope.ServiceProvider.GetRequiredService<IStaffRepository>().AnonymizeAsync(board.StaffKey, DateTime.UtcNow);
        Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(await session.GetAsync(path + "/download")));
    }

    [Fact]
    public async Task Decision_foreign_direct_ids_fail_closed_for_reads_and_writes()
    {
        if (!Database()) return;
        using var enabled = Enable();
        var data = await DecisionSetupAsync();
        var foreign = await DecisionSetupAsync();
        var session = await PersonaSignIn.SignInAsync(factory, data.Owner);
        var path = $"/staffops/executive/decisions/{foreign.Draft.DecisionKey}";
        foreach (var route in new[] { path, path + "/download", path + "/edit", $"/staffops/executive/decisions/new?packKey={foreign.Pack.PackKey}" })
            Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync(route)).StatusCode);
        var form = $"/staffops/executive/decisions/{data.Draft.DecisionKey}";
        var fields = DecisionFields(foreign.Draft.Definition);
        fields["expectedVersion"] = "1";
        fields["note"] = "forged";
        foreach (var route in new[] { path + "/revise", "/staffops/executive/decisions/create" })
            Assert.Equal(HttpStatusCode.NotFound, (await session.PostWithTokenAsync(form, route, fields))!.StatusCode);
        var action = new Dictionary<string, string> { ["ExpectedVersion"] = "1", ["Action"] = "AcceptEvidence", ["Note"] = "forged" };
        Assert.Equal(HttpStatusCode.NotFound, (await session.PostWithTokenAsync(form, path + "/act", action))!.StatusCode);
    }

    [Fact]
    public async Task Decision_flag_platform_and_employee_denials_apply_to_new_routes()
    {
        if (!Database()) return;
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        foreach (var route in new[] { "/staffops/executive/decisions", "/staffops/executive/decisions/new", $"/staffops/executive/decisions/{Guid.NewGuid()}/download" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
        using var enabled = Enable();
        var data = await DecisionSetupAsync();
        foreach (var basis in new[] { NorthstarPersonas.Developer, NorthstarPersonas.PlatformAdmin })
        {
            var persona = await PersonaAsync(basis, data.Actor.TenantId);
            var session = await PersonaSignIn.SignInAsync(factory, persona);
            foreach (var route in new[] { "/staffops/executive/decisions", $"/staffops/executive/decisions/{data.Draft.DecisionKey}", $"/staffops/executive/decisions/{data.Draft.DecisionKey}/download" })
                Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(await session.GetAsync(route)));
        }
    }

    private async Task<(ReviewActor Actor, PersonaDefinition Owner, ExecutivePack Pack, DecisionRevision Draft)> DecisionSetupAsync()
    {
        var tenant = await CreateTenantAsync();
        await SeedSourceAsync(tenant);
        var owner = await PersonaAsync(NorthstarPersonas.ProjectManager, tenant);
        using var scope = factory.Services.CreateScope();
        var staff = await scope.ServiceProvider.GetRequiredService<IStaffRepository>().GetByStaffKeyAsync(owner.StaffKey);
        var actor = new ReviewActor(tenant, staff!.MemberId);
        var pack = await scope.ServiceProvider.GetRequiredService<IExecutivePackRepository>().CaptureAsync(actor, "ClickUp", 48);
        var definition = new DecisionDefinition(pack.PackKey, pack.Items[0].WorkItemKey, "Milestone recovery", "Customer exposure", "Defer or resource", "Resource", owner.StaffKey, new(2026, 10, 1));
        var draft = await scope.ServiceProvider.GetRequiredService<IExecutiveDecisionRepository>().CreateAsync(actor, definition);
        return (actor, owner, pack, draft);
    }

    private static DecisionCommand DecisionCommandFor(Guid owner) => new(DecisionAction.Decide, "Committee record",
        Decision: "Proceed", Rationale: "Customer exposure", ActionOwnerStaffKey: owner, FollowUpOn: new(2026, 10, 31));

    private sealed class DecisionClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Dictionary<string, string> DecisionFields(DecisionDefinition definition) => new()
    {
        ["Input.PackKey"] = definition.PackKey.ToString(), ["Input.WorkItemKey"] = definition.WorkItemKey.ToString(),
        ["Input.Title"] = definition.Title, ["Input.Materiality"] = definition.Materiality, ["Input.Options"] = definition.Options,
        ["Input.Recommendation"] = definition.Recommendation, ["Input.OwnerStaffKey"] = definition.OwnerStaffKey.ToString(),
        ["Input.DueOn"] = definition.DueOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
    };
}
