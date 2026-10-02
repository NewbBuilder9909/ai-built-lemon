using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.FileImport;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Tenancy;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// A declared review scope over real SQL Server (GTM review 28 Sept, finding
/// 1): a programme scope reads only that programme's work and the period's
/// time, states the unlinked time it can't place, round-trips through the
/// review table, and compares only with earlier reviews of the same scope,
/// where a review from before scopes existed counts as whole-organisation.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EvidenceScopeIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "a scoped Evidence Check was never built, recorded and compared against SQL Server.";

    [Fact]
    public async Task A_programme_scope_reads_its_own_work_and_period_and_is_compared_like_with_like()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        var tenant = await CreateTenantAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        string Day(int daysAgo) => today.AddDays(-daysAgo).ToString("yyyy-MM-dd");

        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var import = services.GetRequiredService<IDeliveryExportImportService>();
        Assert.True((await import.ImportAsync(tenant, DeliveryExportKind.WorkItems,
            "Programme,Project,WorkItemId,Title,Status,EstimatedHours\n" +
            "Portal,Portal build,P-1,Login,In progress,4\n" +
            "Claims,Claims API,C-1,Pricing,In progress,4\n", null)).Imported);
        Assert.True((await import.ImportAsync(tenant, DeliveryExportKind.TimeEntries,
            "WorkItemId,Date,Hours\n" +
            $"P-1,{Day(1)},2\n" +      // in scope, in period
            $"P-1,{Day(30)},5\n" +     // in scope, before the period: lifetime only
            $"C-1,{Day(1)},9\n" +      // another programme
            $",{Day(2)},3\n", null)).Imported); // linked to nothing

        var check = services.GetRequiredService<IEvidenceCheckService>();
        var portalKey = (await services.GetRequiredService<IProgrammeRepository>().GetProgrammesAsync(tenant)).Single(p => p.Name == "Portal").ProgrammeKey;
        var resolved = await check.ResolveScopeAsync(tenant, new EvidenceScopeRequest(ProgrammeKey: portalKey, ExtractedOn: today));
        var portal = resolved.Scope!;
        Assert.Equal((today.AddDays(-6), today), (portal.PeriodFrom, portal.PeriodTo));

        var report = await check.BuildAsync(tenant, portal);

        Assert.Equal(1, report.WorkItems);                   // Claims is out of scope
        Assert.Equal(2m, report.RecordedHours);              // only this period's Portal time
        Assert.Contains(report.SourceNotes, n => n.StartsWith("3 hours recorded in the period aren't linked"));
        var overrun = report.Findings.Single(f => f.Key == "over-estimate"); // 7 h ever against 4 h
        Assert.StartsWith("7 h against 4 h", overrun.Records.Single().Detail);

        // A review recorded before scopes existed, then a scoped one of each kind.
        var reviews = services.GetRequiredService<IEvidenceReviewRepository>();
        var legacy = EvidenceReviewCalculator.Record(report with { Scope = null }, null, tenant, null, Guid.NewGuid());
        await reviews.AddAsync(legacy);
        var service = services.GetRequiredService<IEvidenceReviewService>();
        var portalReview = (await service.RecordAsync(tenant, portal with { Decision = "Is the portal release on track?" }, new EvidenceReviewActor(null, null)))!;
        var whole = (await check.ResolveScopeAsync(tenant, new EvidenceScopeRequest())).Scope!;

        var stored = (await reviews.GetAsync(tenant, portalReview.ReviewKey))!;
        Assert.Equal(portal with { Decision = "Is the portal release on track?" }, stored.Scope);
        Assert.Null(await reviews.GetPreviousAsync(tenant, portalReview.ReviewKey)); // the legacy review is another scope
        Assert.Equal(legacy.ReviewKey, (await reviews.GetLatestAsync(tenant, whole.Key))!.ReviewKey); // but it is the whole organisation's
        Assert.Equal(portalReview.ReviewKey, (await reviews.GetLatestAsync(tenant, portal.Key))!.ReviewKey);

        // A foreign programme key is refused, not silently widened.
        var foreign = await check.ResolveScopeAsync(tenant, new EvidenceScopeRequest(ProgrammeKey: Guid.NewGuid()));
        Assert.Null(foreign.Scope);
        Assert.NotNull(foreign.Error);
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var key = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantRepository>().CreateAsync(new Tenant
        {
            TenantKey = key, Name = "Evidence scope test", ShortCode = "es-" + key.ToString("N")[..20],
            IsActive = true, CreatedAtUtc = DateTime.UtcNow
        });
        return key;
    }
}
