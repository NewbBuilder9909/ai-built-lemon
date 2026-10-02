using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProgrammePulse.Tests.Personas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ExecutiveReview;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Tests.Integration;

public sealed partial class ExecutiveReviewIntegrationTests
{
    [Fact]
    public async Task Data_redaction_removes_every_version_but_preserves_evidence_and_other_tenants()
    {
        if (!Database()) return;
        var data = await DecisionSetupAsync();
        var foreign = await DecisionSetupAsync();
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var decisions = provider.GetRequiredService<IExecutiveDecisionRepository>();
        await decisions.ApplyAsync(data.Actor, data.Draft.DecisionKey, 1, new(DecisionAction.AcceptEvidence, "private-review-note"), 48);
        var repository = provider.GetRequiredService<IExecutiveDataRepository>();
        var request = Redact(data.Draft.DecisionKey);
        var preview = await repository.PreviewAsync(data.Actor.TenantId, request);
        Assert.Equal(new ExecutiveDataCounts(1, 2, 0, 0, 0), preview.Counts);
        Assert.Equal(2, (await decisions.GetHistoryAsync(data.Actor.TenantId, data.Draft.DecisionKey)).Count);
        var receipt = await repository.ApplyAsync(data.Actor, request, preview.ConfirmationToken);
        Assert.Equal(preview.Counts, receipt.Counts);
        Assert.Empty(await decisions.GetHistoryAsync(data.Actor.TenantId, data.Draft.DecisionKey));
        Assert.Empty(await decisions.GetQueueAsync(data.Actor.TenantId, null));
        Assert.Single(await decisions.GetHistoryAsync(foreign.Actor.TenantId, foreign.Draft.DecisionKey));
        Assert.NotNull(await provider.GetRequiredService<IExecutivePackRepository>().GetAsync(data.Actor.TenantId, data.Pack.PackKey));
        var audit = Assert.Single(await repository.GetEventsAsync(data.Actor.TenantId));
        Assert.Equal("RedactDecision", audit.Operation);
        Assert.DoesNotContain("private-review-note", JsonSerializer.Serialize(audit));
        await Assert.ThrowsAsync<ReviewNotFoundException>(() => decisions.ApplyAsync(data.Actor, data.Draft.DecisionKey,
            2, new(DecisionAction.AcceptEvidence, "late request"), 48));
        await Assert.ThrowsAsync<ReviewNotFoundException>(() => repository.ApplyAsync(data.Actor, request, preview.ConfirmationToken));
    }

    [Fact]
    public async Task Data_pack_withdrawal_includes_decisions_that_referenced_it_in_an_older_version()
    {
        if (!Database()) return;
        using var enabled = Enable();
        var data = await DecisionSetupAsync();
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var packs = provider.GetRequiredService<IExecutivePackRepository>();
        var decisions = provider.GetRequiredService<IExecutiveDecisionRepository>();
        var replacement = await packs.CaptureAsync(data.Actor, "ClickUp", 48);
        await decisions.ApplyAsync(data.Actor, data.Draft.DecisionKey, 1,
            new(DecisionAction.Revise, "refreshed", data.Draft.Definition with { PackKey = replacement.PackKey }), 48);
        var repository = provider.GetRequiredService<IExecutiveDataRepository>();
        var request = new ExecutiveDataRequest { Operation = ExecutiveDataOperation.WithdrawPack,
            TargetKey = data.Pack.PackKey, Reason = ExecutiveDataReason.IncorrectEvidence };
        var preview = await repository.PreviewAsync(data.Actor.TenantId, request);
        Assert.Equal(new ExecutiveDataCounts(1, 2, 1, 0, 0), preview.Counts);
        await repository.ApplyAsync(data.Actor, request, preview.ConfirmationToken);
        Assert.Null(await packs.GetAsync(data.Actor.TenantId, data.Pack.PackKey));
        Assert.Equal(replacement.PackKey, Assert.Single(await packs.GetRecentAsync(data.Actor.TenantId)).PackKey);
        Assert.Empty(await decisions.GetHistoryAsync(data.Actor.TenantId, data.Draft.DecisionKey));
        await Assert.ThrowsAsync<ReviewNotFoundException>(() => decisions.CreateAsync(data.Actor, data.Draft.Definition));
        var session = await PersonaSignIn.SignInAsync(factory, data.Owner);
        foreach (var route in new[] { $"/staffops/executive/{data.Pack.PackKey}",
            $"/staffops/executive/{data.Pack.PackKey}/download", $"/staffops/executive/decisions/{data.Draft.DecisionKey}",
            $"/staffops/executive/decisions/{data.Draft.DecisionKey}/download" })
            Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task Data_confirmation_detects_intervening_writes_and_is_tenant_and_operation_bound()
    {
        if (!Database()) return;
        var data = await DecisionSetupAsync();
        var foreign = await DecisionSetupAsync();
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var repository = provider.GetRequiredService<IExecutiveDataRepository>();
        var request = Redact(data.Draft.DecisionKey);
        var preview = await repository.PreviewAsync(data.Actor.TenantId, request);
        await provider.GetRequiredService<IExecutiveDecisionRepository>().ApplyAsync(data.Actor, data.Draft.DecisionKey, 1,
            new(DecisionAction.AcceptEvidence, "concurrent review"), 48);
        await Assert.ThrowsAsync<ReviewConflictException>(() => repository.ApplyAsync(data.Actor, request, preview.ConfirmationToken));
        Assert.Equal(2, (await provider.GetRequiredService<IExecutiveDecisionRepository>().GetHistoryAsync(data.Actor.TenantId, data.Draft.DecisionKey)).Count);
        await Assert.ThrowsAsync<ReviewNotFoundException>(() => repository.PreviewAsync(foreign.Actor.TenantId, request));
        var purge = new ExecutiveDataRequest { Operation = ExecutiveDataOperation.PurgeTenantData, Reason = ExecutiveDataReason.TenantOffboarding };
        var purgePreview = await repository.PreviewAsync(data.Actor.TenantId, purge);
        await Assert.ThrowsAsync<ReviewConflictException>(() => repository.ApplyAsync(foreign.Actor, purge, purgePreview.ConfirmationToken));
        await Assert.ThrowsAsync<ReviewConflictException>(() => repository.ApplyAsync(data.Actor, request, purgePreview.ConfirmationToken));
        Assert.Empty(await repository.GetEventsAsync(data.Actor.TenantId));
    }

    [Fact]
    public async Task Data_retention_preserves_open_decisions_all_their_evidence_and_current_market_settings()
    {
        if (!Database()) return;
        var data = await DecisionSetupAsync();
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var decisions = provider.GetRequiredService<IExecutiveDecisionRepository>();
        var packs = provider.GetRequiredService<IExecutivePackRepository>();
        var expiredPack = await packs.CaptureAsync(data.Actor, "ClickUp", 48);
        var closed = await decisions.CreateAsync(data.Actor, data.Draft.Definition with { PackKey = expiredPack.PackKey });
        await decisions.ApplyAsync(data.Actor, closed.DecisionKey, 1, new(DecisionAction.Dismiss, "No longer needed"), 48);
        var markets = provider.GetRequiredService<IMarketSettingsRepository>();
        await markets.SaveAsync(data.Actor, new(), 0);
        await markets.SaveAsync(data.Actor, new() { MarketCode = "IE", ReportingCurrency = "EUR" }, 1);
        var options = Options.Create(new ExecutiveDataOptions { RetentionDays = 30 });
        var repository = new ExecutiveDataRepository(provider.GetRequiredService<IScopeProvider>(), options,
            new DecisionClock(DateTimeOffset.UtcNow.AddDays(32)));
        var request = new ExecutiveDataRequest { Operation = ExecutiveDataOperation.ApplyRetention, Reason = ExecutiveDataReason.RetentionPolicy };
        var preview = await repository.PreviewAsync(data.Actor.TenantId, request);
        Assert.Equal(new ExecutiveDataCounts(1, 2, 1, 1, 0), preview.Counts);
        options.Value.RetentionDays = 31;
        await Assert.ThrowsAsync<ReviewConflictException>(() => repository.ApplyAsync(data.Actor, request, preview.ConfirmationToken));
        options.Value.RetentionDays = 30;
        await repository.ApplyAsync(data.Actor, request, preview.ConfirmationToken);
        Assert.Single(await decisions.GetHistoryAsync(data.Actor.TenantId, data.Draft.DecisionKey));
        Assert.Empty(await decisions.GetHistoryAsync(data.Actor.TenantId, closed.DecisionKey));
        Assert.NotNull(await packs.GetAsync(data.Actor.TenantId, data.Pack.PackKey));
        Assert.Null(await packs.GetAsync(data.Actor.TenantId, expiredPack.PackKey));
        Assert.Equal(2, (await markets.GetAsync(data.Actor.TenantId)).Version);
        Assert.Equal(0, (await repository.PreviewAsync(data.Actor.TenantId, request)).Counts.DecisionVersions);
    }

    [Fact]
    public async Task Data_subject_export_and_erasure_work_when_executive_feature_is_disabled()
    {
        if (!Database()) return;
        var data = await DecisionSetupAsync();
        var foreign = await DecisionSetupAsync();
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var decisions = provider.GetRequiredService<IExecutiveDecisionRepository>();
        var other = await PersonaAsync(NorthstarPersonas.ProjectManager, data.Actor.TenantId);
        var otherProfile = await provider.GetRequiredService<IStaffRepository>().GetByStaffKeyAsync(other.StaffKey);
        var otherActor = new ReviewActor(data.Actor.TenantId, otherProfile!.MemberId);
        await decisions.ApplyAsync(otherActor, data.Draft.DecisionKey, 1,
            new(DecisionAction.Revise, "new owner", data.Draft.Definition with { OwnerStaffKey = other.StaffKey }), 48);
        var untouched = await decisions.CreateAsync(otherActor, data.Draft.Definition with { OwnerStaffKey = other.StaffKey });
        var markets = provider.GetRequiredService<IMarketSettingsRepository>();
        await markets.SaveAsync(data.Actor, new(), 0);
        var extra = await decisions.CreateAsync(otherActor, untouched.Definition);
        var repository = provider.GetRequiredService<IExecutiveDataRepository>();
        var request = Redact(extra.DecisionKey);
        var preview = await repository.PreviewAsync(data.Actor.TenantId, request);
        await repository.ApplyAsync(data.Actor, request, preview.ConfirmationToken);
        var gdpr = provider.GetRequiredService<IGdprService>();
        Assert.IsType<TransactionalGdprService>(gdpr);
        Assert.Null(await gdpr.BuildExportAsync(data.Owner.StaffKey, foreign.Actor.TenantId));
        var export = await gdpr.BuildExportAsync(data.Owner.StaffKey, data.Actor.TenantId);
        Assert.NotNull(export);
        Assert.Equal(2, export.LinkedRecords.Count(r => r.Section == "Executive decision journal"));
        Assert.Contains(export.LinkedRecords, r => r.Section == "Executive pack capture");
        Assert.Contains(export.LinkedRecords, r => r.Section == "Executive market settings");
        Assert.Contains(export.LinkedRecords, r => r.Section == "Executive data lifecycle");
        await gdpr.EraseAsync(data.Owner.StaffKey);
        Assert.Empty(await decisions.GetHistoryAsync(data.Actor.TenantId, data.Draft.DecisionKey));
        Assert.Single(await decisions.GetHistoryAsync(data.Actor.TenantId, untouched.DecisionKey));
        Assert.Single(await decisions.GetHistoryAsync(foreign.Actor.TenantId, foreign.Draft.DecisionKey));
        Assert.Null((await markets.GetAsync(data.Actor.TenantId)).ChangedByMemberId);
        Assert.False((await provider.GetRequiredService<IStaffRepository>().GetByStaffKeyAsync(data.Owner.StaffKey))!.IsActive);
        Assert.Empty(await repository.ExportSubjectAsync(data.Actor.TenantId, data.Owner.StaffKey, data.Actor.MemberId));
        Assert.All(await repository.GetEventsAsync(data.Actor.TenantId), e => Assert.Null(e.ActorMemberId));
        var count = (await repository.GetEventsAsync(data.Actor.TenantId)).Count;
        await gdpr.EraseAsync(data.Owner.StaffKey);
        Assert.Equal(count, (await repository.GetEventsAsync(data.Actor.TenantId)).Count);
    }

    [Fact]
    public async Task Data_erasure_rolls_back_if_a_later_participant_fails()
    {
        if (!Database()) return;
        var data = await DecisionSetupAsync();
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var participant = provider.GetServices<IStaffDataParticipant>().OfType<ExecutiveDataParticipant>().Single();
        var inner = new GdprService(provider.GetRequiredService<IStaffRepository>(), provider.GetRequiredService<IAvailabilityRepository>(),
            provider.GetRequiredService<ILeaveRequestRepository>(), provider.GetRequiredService<IStaffRateRepository>(),
            provider.GetRequiredService<IWorkHoursHistoryRepository>(), provider.GetRequiredService<IStaffAuditLogRepository>(),
            [participant, new FailingDataParticipant()], TimeProvider.System);
        var service = new TransactionalGdprService(inner, provider.GetRequiredService<IStaffRepository>(), provider.GetRequiredService<IScopeProvider>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EraseAsync(data.Owner.StaffKey));
        Assert.Single(await provider.GetRequiredService<IExecutiveDecisionRepository>().GetHistoryAsync(data.Actor.TenantId, data.Draft.DecisionKey));
        Assert.True((await provider.GetRequiredService<IStaffRepository>().GetByStaffKeyAsync(data.Owner.StaffKey))!.IsActive);
        Assert.Empty(await provider.GetRequiredService<IExecutiveDataRepository>().GetEventsAsync(data.Actor.TenantId));
    }

    [Fact]
    public async Task Data_tenant_purge_removes_only_executive_records_and_keeps_other_tenant_and_source_data()
    {
        if (!Database()) return;
        var data = await DecisionSetupAsync();
        var foreign = await DecisionSetupAsync();
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;
        await provider.GetRequiredService<IMarketSettingsRepository>().SaveAsync(data.Actor, new(), 0);
        var repository = provider.GetRequiredService<IExecutiveDataRepository>();
        var request = new ExecutiveDataRequest { Operation = ExecutiveDataOperation.PurgeTenantData, Reason = ExecutiveDataReason.TenantOffboarding };
        var preview = await repository.PreviewAsync(data.Actor.TenantId, request);
        await repository.ApplyAsync(data.Actor, request, preview.ConfirmationToken);
        Assert.Equal(new ExecutiveDataCounts(0, 0, 0, 0, 0), (await repository.PreviewAsync(data.Actor.TenantId, request)).Counts);
        Assert.Single(await provider.GetRequiredService<IExecutiveDecisionRepository>().GetHistoryAsync(foreign.Actor.TenantId, foreign.Draft.DecisionKey));
        Assert.NotEmpty(await provider.GetRequiredService<IProgrammeRepository>().GetWorkItemsAsync(data.Actor.TenantId));
        Assert.NotNull(await provider.GetRequiredService<IStaffRepository>().GetByStaffKeyAsync(data.Owner.StaffKey));
        Assert.Equal(0, (await provider.GetRequiredService<IMarketSettingsRepository>().GetAsync(data.Actor.TenantId)).Version);
    }

    [Fact]
    public async Task Data_admin_HTTP_preview_confirm_and_Welsh_render_work_while_feature_is_disabled()
    {
        if (!Database()) return;
        var data = await DecisionSetupAsync();
        var admin = await PersonaAsync(NorthstarPersonas.TenantAdmin, data.Actor.TenantId);
        var session = await PersonaSignIn.SignInAsync(factory, admin);
        const string path = "/staffops/executive/data";
        var page = await session.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl?.NoStore);
        var fields = DataFields(Redact(data.Draft.DecisionKey));
        Assert.Equal(HttpStatusCode.BadRequest, (await session.Client.PostAsync(path + "/preview", new FormUrlEncodedContent(fields))).StatusCode);
        var preview = await session.PostWithTokenAsync(path, path + "/preview", fields);
        Assert.Equal(HttpStatusCode.OK, preview!.StatusCode);
        var html = await preview.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Milestone recovery", html);
        Assert.DoesNotContain("Customer exposure", html);
        fields["confirmationToken"] = ConfirmationToken(html);
        fields["confirmed"] = "false";
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PostWithTokenAsync(path, path + "/apply", fields))!.StatusCode);
        fields["confirmed"] = "true";
        var applied = await session.PostWithTokenAsync(path, path + "/apply", fields);
        Assert.Equal(HttpStatusCode.OK, applied!.StatusCode);
        Assert.Contains("Completed. Receipt:", await applied.Content.ReadAsStringAsync());
        await session.GetAsync("/language?culture=cy-GB&returnUrl=/staffops/executive/data");
        var welsh = await (await session.GetAsync(path)).Content.ReadAsStringAsync();
        Assert.Contains("Rheolaethau data gweithredol", welsh);
        Assert.DoesNotContain("Review.Data.", welsh);
    }

    [Fact]
    public async Task Data_HTTP_rejects_readers_platform_operators_foreign_targets_and_stale_confirmation()
    {
        if (!Database()) return;
        var data = await DecisionSetupAsync();
        var foreign = await DecisionSetupAsync();
        const string path = "/staffops/executive/data";
        foreach (var basis in new[] { NorthstarPersonas.Board, NorthstarPersonas.ProjectManager, NorthstarPersonas.Developer, NorthstarPersonas.PlatformAdmin })
        {
            var persona = await PersonaAsync(basis, data.Actor.TenantId);
            var session = await PersonaSignIn.SignInAsync(factory, persona);
            Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(await session.GetAsync(path)));
            foreach (var suffix in new[] { "/preview", "/apply" })
            {
                var denied = await session.PostWithTokenAsync(PersonaSignIn.LoginPath, path + suffix,
                    DataFields(Redact(data.Draft.DecisionKey)));
                Assert.NotNull(denied);
                Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(denied));
            }
        }
        var admin = await PersonaAsync(NorthstarPersonas.TenantAdmin, data.Actor.TenantId);
        var adminSession = await PersonaSignIn.SignInAsync(factory, admin);
        Assert.Equal(HttpStatusCode.NotFound, (await adminSession.PostWithTokenAsync(path, path + "/preview", DataFields(Redact(foreign.Draft.DecisionKey))))!.StatusCode);
        var fields = DataFields(Redact(data.Draft.DecisionKey));
        var preview = await adminSession.PostWithTokenAsync(path, path + "/preview", fields);
        fields["confirmationToken"] = ConfirmationToken(await preview!.Content.ReadAsStringAsync());
        fields["confirmed"] = "true";
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IExecutiveDecisionRepository>().ApplyAsync(data.Actor, data.Draft.DecisionKey, 1,
            new(DecisionAction.AcceptEvidence, "intervening write"), 48);
        Assert.Equal(HttpStatusCode.Conflict, (await adminSession.PostWithTokenAsync(path, path + "/apply", fields))!.StatusCode);
        await scope.ServiceProvider.GetRequiredService<IStaffRepository>().AnonymizeAsync(admin.StaffKey, DateTime.UtcNow);
        Assert.Equal(AccessOutcome.Denied, PersonaSession.Outcome(await adminSession.GetAsync(path)));
    }

    private static ExecutiveDataRequest Redact(Guid key) => new()
    { Operation = ExecutiveDataOperation.RedactDecision, TargetKey = key, Reason = ExecutiveDataReason.PersonalData };

    [Fact]
    public async Task Data_concurrent_purges_have_one_winner_and_cannot_reuse_the_preview()
    {
        if (!Database()) return;
        var data = await DecisionSetupAsync();
        var request = new ExecutiveDataRequest { Operation = ExecutiveDataOperation.PurgeTenantData, Reason = ExecutiveDataReason.TenantOffboarding };
        using var scope = factory.Services.CreateScope();
        var preview = await scope.ServiceProvider.GetRequiredService<IExecutiveDataRepository>().PreviewAsync(data.Actor.TenantId, request);
        async Task<bool> Apply()
        {
            using var concurrent = factory.Services.CreateScope();
            try
            {
                await concurrent.ServiceProvider.GetRequiredService<IExecutiveDataRepository>().ApplyAsync(data.Actor, request, preview.ConfirmationToken);
                return true;
            }
            catch (ReviewConflictException) { return false; }
        }
        var outcomes = await Task.WhenAll(Task.Run(Apply), Task.Run(Apply));
        Assert.Single(outcomes, success => success);
    }

    private static Dictionary<string, string> DataFields(ExecutiveDataRequest request) => new()
    { ["Operation"] = request.Operation.ToString()!, ["TargetKey"] = request.TargetKey?.ToString() ?? "", ["Reason"] = request.Reason.ToString()! };

    private static string ConfirmationToken(string html)
    {
        var match = Regex.Match(html, "name=\"confirmationToken\" value=\"([A-F0-9]{64})\"");
        Assert.True(match.Success, "The preview must contain a server-generated confirmation token.");
        return match.Groups[1].Value;
    }

    private sealed class FailingDataParticipant : IStaffDataParticipant
    {
        public string Section => "Failure fixture";
        public Task<IReadOnlyList<GdprExportLinkedRecordRow>> ExportAsync(Guid staffKey) => throw new NotSupportedException();
        public Task EraseAsync(Guid staffKey, DateTime nowUtc) => throw new InvalidOperationException("Simulated participant failure");
    }
}
