using System.Net;
using ProgrammePulse.Tests.Personas;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.FileImport;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// The weekly review loop over real HTTP and real SQL Server: a delivery lead
/// records the check, names an owner and a decision, the data is fixed, and
/// the next recorded check shows the finding cleared and the earlier review
/// frozen. A read-only analyst can read but not record; another tenant sees
/// none of it; erasure removes the owner without losing the decision.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EvidenceReviewIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string LoopNotProduced = "the recorded review loop was never exercised over real HTTP and SQL.";
    private const string Check = "/staffops/programme/evidence-check";

    [Fact]
    public async Task A_recorded_review_takes_decisions_carries_them_forward_and_freezes_once_superseded()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(LoopNotProduced, output)) return;

        var tenant = await CreateTenantAsync();
        var other = await CreateTenantAsync();
        var marker = Guid.NewGuid().ToString("N")[..10];
        var lateId = $"LATE-{marker}";
        var oddId = $"ODD-{marker}";
        await ImportAsync(tenant,
            "Project,WorkItemId,Title,Status,DueDate,EstimatedHours\n" +
            $"Rollout,{lateId},Late item,In progress,2020-01-31,4\n" +
            $"Rollout,{oddId},Waiting item,Waiting on client,,\n");

        var lead = await PersonaAsync(NorthstarPersonas.ProjectManager, tenant);
        var analyst = await PersonaAsync(NorthstarPersonas.Analyst, tenant);
        var foreigner = await PersonaAsync(NorthstarPersonas.ProjectManager, other);
        var session = await PersonaSignIn.SignInAsync(factory, lead);

        var livePage = await (await session.GetAsync(Check)).Content.ReadAsStringAsync();
        Assert.Contains("Record for the review", livePage);
        Assert.Contains("Record this check before the review meeting", livePage);

        // ---- Record, then decide.
        var recorded = await session.PostWithTokenAsync(Check, $"{Check}/record", new Dictionary<string, string>());
        Assert.NotNull(recorded);
        Assert.Equal(HttpStatusCode.Redirect, recorded.StatusCode);
        var reviewPath = recorded.Headers.Location!.ToString();
        Assert.StartsWith($"{Check}/reviews/", reviewPath);
        var firstKey = Guid.Parse(reviewPath[(reviewPath.LastIndexOf('/') + 1)..]);

        var reviewHtml = await (await session.GetAsync(reviewPath)).Content.ReadAsStringAsync();
        Assert.Contains("Open work past its due date", reviewHtml);
        Assert.Contains("Save decision", reviewHtml);

        var target = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd");
        var unowned = await DecideAsync(session, reviewPath, "overdue", "FixAtSource", owner: null, target, "Replan it");
        Assert.Equal(HttpStatusCode.BadRequest, unowned.StatusCode);
        Assert.Contains("needs a named owner", await unowned.Content.ReadAsStringAsync());

        var foreignOwner = await DecideAsync(session, reviewPath, "overdue", "FixAtSource", foreigner.StaffKey, target, "Replan it");
        Assert.Equal(HttpStatusCode.BadRequest, foreignOwner.StatusCode);

        var saved = await DecideAsync(session, reviewPath, "overdue", "FixAtSource", analyst.StaffKey, target, "Replan with the client");
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("saved=overdue", saved.Headers.Location!.ToString());

        var export = await (await session.GetAsync($"{reviewPath}/export")).Content.ReadAsStringAsync();
        Assert.Contains(analyst.FullName, export);
        Assert.Contains("Fix at source", export);
        Assert.Contains(lateId, export);

        var pack = await session.GetAsync($"{reviewPath}/pack");
        Assert.Equal(HttpStatusCode.OK, pack.StatusCode);
        var packHtml = await pack.Content.ReadAsStringAsync();
        Assert.Contains("Delivery evidence pack", packHtml);
        Assert.Contains("Not decision-ready", packHtml);
        Assert.Contains(analyst.FullName, packHtml);
        Assert.Contains(oddId, packHtml);
        Assert.DoesNotContain("<script", packHtml, StringComparison.OrdinalIgnoreCase);

        // ---- Fix the data at source and record the next check. The export is
        // the whole period, as each upload replaces the last: the waiting item
        // is unchanged, the late one is now done.
        await ImportAsync(tenant,
            "Project,WorkItemId,Title,Status,DueDate,EstimatedHours\n" +
            $"Rollout,{lateId},Late item,Done,2020-01-31,4\n" +
            $"Rollout,{oddId},Waiting item,Waiting on client,,\n");
        var liveAfterFix = await (await session.GetAsync(Check)).Content.ReadAsStringAsync();
        Assert.Contains("Since the last recorded review", liveAfterFix);
        Assert.Contains("Cleared", liveAfterFix);

        var second = await session.PostWithTokenAsync(Check, $"{Check}/record", new Dictionary<string, string>());
        var secondPath = second!.Headers.Location!.ToString();
        Assert.NotEqual(reviewPath, secondPath);
        var secondHtml = await (await session.GetAsync(secondPath)).Content.ReadAsStringAsync();
        Assert.Contains("Since the previous review", secondHtml);
        Assert.Contains("Cleared", secondHtml);

        // The first review's pack still shows the evidence as it stood, although the item is now Done.
        var frozenPack = await (await session.GetAsync($"{reviewPath}/pack")).Content.ReadAsStringAsync();
        Assert.Contains(lateId, frozenPack);
        Assert.Contains("Open work past its due date", frozenPack);

        var frozen = await DecideAsync(session, reviewPath, "overdue", "Resolved", owner: null, target: "", note: "");
        Assert.Equal(HttpStatusCode.Conflict, frozen.StatusCode);
        Assert.Contains("now history", await frozen.Content.ReadAsStringAsync());

        var history = await (await session.GetAsync($"{Check}/reviews")).Content.ReadAsStringAsync();
        Assert.Contains(firstKey.ToString(), history);

        // ---- A read-only analyst reads, but cannot record or decide.
        var reader = await PersonaSignIn.SignInAsync(factory, analyst);
        var readerHtml = await (await reader.GetAsync(secondPath)).Content.ReadAsStringAsync();
        Assert.Contains("Since the previous review", readerHtml);
        Assert.DoesNotContain("Save decision", readerHtml);
        var readerRecord = await reader.PostWithTokenAsync(secondPath, $"{Check}/record", new Dictionary<string, string>());
        Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(readerRecord!));
        var readerDecide = await reader.PostWithTokenAsync(secondPath, $"{secondPath}/decide",
            new Dictionary<string, string> { ["findingKey"] = "unmapped-status", ["disposition"] = "Resolved" });
        Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(readerDecide!));

        // ---- Another tenant sees none of it.
        var outsider = await PersonaSignIn.SignInAsync(factory, foreigner);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(reviewPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"{reviewPath}/pack")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"{reviewPath}/export")).StatusCode);
        Assert.DoesNotContain(firstKey.ToString(), await (await outsider.GetAsync($"{Check}/reviews")).Content.ReadAsStringAsync());

        // ---- Erasure removes the person and keeps the decision.
        using (var scope = factory.Services.CreateScope())
        {
            var participant = scope.ServiceProvider.GetServices<IStaffDataParticipant>().OfType<EvidenceReviewDataParticipant>().Single();
            var exported = await participant.ExportAsync(analyst.StaffKey);
            Assert.Contains(exported, row => row.Summary.Contains("Named owner"));

            await participant.EraseAsync(analyst.StaffKey, DateTime.UtcNow);

            Assert.Empty(await participant.ExportAsync(analyst.StaffKey));
            var reviews = scope.ServiceProvider.GetRequiredService<IEvidenceReviewRepository>();
            var first = await reviews.GetAsync(tenant, firstKey);
            var decision = first!.Findings.Single(f => f.FindingKey == "overdue");
            Assert.Null(decision.OwnerStaffKey);
            Assert.Equal(FindingDisposition.FixAtSource, decision.Disposition);
            Assert.Equal("Replan with the client", decision.Note);
        }
    }

    private static async Task<HttpResponseMessage> DecideAsync(PersonaSession session, string reviewPath, string findingKey, string disposition,
        Guid? owner, string target, string note)
    {
        var response = await session.PostWithTokenAsync(reviewPath, $"{reviewPath}/decide", new Dictionary<string, string>
        {
            ["findingKey"] = findingKey,
            ["disposition"] = disposition,
            ["ownerStaffKey"] = owner?.ToString() ?? "",
            ["targetDate"] = target,
            ["note"] = note
        });
        Assert.NotNull(response);
        return response;
    }

    private async Task ImportAsync(Guid tenant, string workItems)
    {
        using var scope = factory.Services.CreateScope();
        var import = scope.ServiceProvider.GetRequiredService<IDeliveryExportImportService>();
        var result = await import.ImportAsync(tenant, DeliveryExportKind.WorkItems, workItems, null);
        Assert.True(result.Imported, result.Summary);
    }

    private async Task<PersonaDefinition> PersonaAsync(PersonaDefinition basis, Guid tenant)
    {
        var key = Guid.NewGuid();
        var persona = basis with
        {
            StaffKey = key, TenantKey = tenant, Email = $"review-{key:N}@northstar.test",
            FullName = $"{basis.FullName} {key.ToString("N")[..6]}"
        };
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([persona]);
        return persona;
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var key = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantRepository>().CreateAsync(new Tenant
        {
            TenantKey = key, Name = "Evidence review test", ShortCode = "er-" + key.ToString("N")[..20],
            IsActive = true, CreatedAtUtc = DateTime.UtcNow
        });
        return key;
    }
}
