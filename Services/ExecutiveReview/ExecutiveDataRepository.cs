using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Staff;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ExecutiveReview;

public interface IExecutiveDataRepository
{
    Task<IReadOnlyList<ExecutiveDataEvent>> GetEventsAsync(Guid tenantId);
    Task<ExecutiveDataPreview> PreviewAsync(Guid tenantId, ExecutiveDataRequest request);
    Task<ExecutiveDataReceipt> ApplyAsync(ReviewActor actor, ExecutiveDataRequest request, string confirmationToken);
    Task<IReadOnlyList<GdprExportLinkedRecordRow>> ExportSubjectAsync(Guid tenantId, Guid staffKey, int memberId);
    Task EraseSubjectAsync(Guid tenantId, Guid staffKey, int memberId, DateTime nowUtc);
}

public sealed class ExecutiveDataRepository(IScopeProvider scopes, IOptions<ExecutiveDataOptions> options,
    TimeProvider clock) : IExecutiveDataRepository
{
    public async Task<IReadOnlyList<ExecutiveDataEvent>> GetEventsAsync(Guid tenantId)
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        return (await scope.Database.FetchAsync<ExecutiveDataEventDto>(
            "SELECT TOP 100 * FROM ExecutiveReview_DataEvent WHERE tenantId=@0 ORDER BY id DESC", tenantId))
            .Select(Map).ToArray();
    }

    public async Task<ExecutiveDataPreview> PreviewAsync(Guid tenantId, ExecutiveDataRequest request)
    {
        using var scope = scopes.CreateScope(isolationLevel: IsolationLevel.Serializable);
        await MarketSettingsRepository.LockTenantAsync(scope, tenantId);
        var preview = Plan(await LoadAsync(scope, tenantId), request).Preview;
        scope.Complete();
        return preview;
    }

    public async Task<ExecutiveDataReceipt> ApplyAsync(ReviewActor actor, ExecutiveDataRequest request, string confirmationToken)
    {
        using var scope = scopes.CreateScope(isolationLevel: IsolationLevel.Serializable);
        await MarketSettingsRepository.LockTenantAsync(scope, actor.TenantId);
        var plan = Plan(await LoadAsync(scope, actor.TenantId), request);
        if (!string.Equals(plan.Preview.ConfirmationToken, confirmationToken, StringComparison.Ordinal))
            throw new ReviewConflictException();
        foreach (var row in plan.Decisions) await scope.Database.DeleteAsync(row);
        foreach (var row in plan.Packs) await scope.Database.DeleteAsync(row);
        foreach (var row in plan.Markets) await scope.Database.DeleteAsync(row);
        foreach (var row in plan.Events) await scope.Database.DeleteAsync(row);
        var now = clock.GetUtcNow().UtcDateTime;
        var key = Guid.NewGuid();
        // A tenant purge leaves no executive records behind, including this module's audit.
        if (request.Operation != ExecutiveDataOperation.PurgeTenantData)
            await RecordAsync(scope, actor.TenantId, key, request.Operation!.Value.ToString(), request.TargetKey,
                request.Reason!.Value.ToString(), actor.MemberId, plan.Preview.Counts, now);
        scope.Complete();
        return new(key, request.Operation!.Value, plan.Preview.Counts, now);
    }

    public async Task<IReadOnlyList<GdprExportLinkedRecordRow>> ExportSubjectAsync(Guid tenantId, Guid staffKey, int memberId)
    {
        using var scope = scopes.CreateScope(isolationLevel: IsolationLevel.Serializable);
        await MarketSettingsRepository.LockTenantAsync(scope, tenantId);
        var state = await LoadAsync(scope, tenantId);
        var revisions = state.Decisions.Select(Read).ToArray();
        var keys = revisions.Where(r => ExecutiveDataPolicy.ReferencesSubject(r, staffKey, memberId))
            .Select(r => r.DecisionKey).ToHashSet();
        var result = revisions.Where(r => keys.Contains(r.DecisionKey))
            .Select(r => Export("Executive decision journal", r, r.RecordedAtUtc)).ToList();
        result.AddRange(state.Packs.Where(p => p.CapturedByMemberId == memberId)
            .Select(p => Export("Executive pack capture", new { p.PackKey, p.CapturedByMemberId }, p.CapturedAtUtc)));
        result.AddRange(state.Markets.Where(m => m.ChangedByMemberId == memberId)
            .Select(m => Export("Executive market settings", new { m.Version, m.ChangedByMemberId,
                Settings = JsonSerializer.Deserialize<MarketSettings>(m.SettingsJson) }, m.EffectiveAtUtc)));
        result.AddRange(state.Events.Where(e => e.ActorMemberId == memberId)
            .Select(e => Export("Executive data lifecycle", Map(e), e.RecordedAtUtc)));
        scope.Complete();
        return result;
    }

    public async Task EraseSubjectAsync(Guid tenantId, Guid staffKey, int memberId, DateTime nowUtc)
    {
        using var scope = scopes.CreateScope(isolationLevel: IsolationLevel.Serializable);
        await MarketSettingsRepository.LockTenantAsync(scope, tenantId);
        var state = await LoadAsync(scope, tenantId);
        var keys = state.Decisions.Select(Read).Where(r => ExecutiveDataPolicy.ReferencesSubject(r, staffKey, memberId))
            .Select(r => r.DecisionKey).ToHashSet();
        // Erase the entire affected journal: retaining another version could retain the same free text.
        var decisions = state.Decisions.Where(d => keys.Contains(d.DecisionKey)).ToArray();
        foreach (var row in decisions) await scope.Database.DeleteAsync(row);
        var packs = state.Packs.Where(p => p.CapturedByMemberId == memberId).ToArray();
        foreach (var row in packs) { row.CapturedByMemberId = 0; await scope.Database.UpdateAsync(row); }
        var markets = state.Markets.Where(m => m.ChangedByMemberId == memberId).ToArray();
        foreach (var row in markets) { row.ChangedByMemberId = 0; await scope.Database.UpdateAsync(row); }
        var events = state.Events.Where(e => e.ActorMemberId == memberId).ToArray();
        foreach (var row in events) { row.ActorMemberId = null; await scope.Database.UpdateAsync(row); }
        if (decisions.Length + packs.Length + markets.Length + events.Length > 0)
            await RecordAsync(scope, tenantId, Guid.NewGuid(), "SubjectErasure", null, "PersonalData", null,
                new(keys.Count, decisions.Length, packs.Length, markets.Length, events.Length), nowUtc);
        scope.Complete();
    }

    private DataPlan Plan(DataState state, ExecutiveDataRequest request)
    {
        var days = options.Value.RetentionDays;
        ExecutiveDataPolicy.Validate(request, days);
        var decisions = Array.Empty<ExecutiveDecisionVersionDto>();
        var packs = Array.Empty<ExecutivePackDto>();
        var markets = Array.Empty<MarketSettingsVersionDto>();
        var events = Array.Empty<ExecutiveDataEventDto>();
        DateTime? cutoff = null;
        switch (request.Operation)
        {
            case ExecutiveDataOperation.RedactDecision:
                decisions = state.Decisions.Where(d => d.DecisionKey == request.TargetKey).ToArray();
                if (decisions.Length == 0) throw new ReviewNotFoundException();
                break;
            case ExecutiveDataOperation.WithdrawPack:
                packs = state.Packs.Where(p => p.PackKey == request.TargetKey).ToArray();
                if (packs.Length == 0) throw new ReviewNotFoundException();
                var affected = state.Decisions.Where(d => Read(d).Definition.PackKey == request.TargetKey)
                    .Select(d => d.DecisionKey).ToHashSet();
                decisions = state.Decisions.Where(d => affected.Contains(d.DecisionKey)).ToArray();
                break;
            case ExecutiveDataOperation.ApplyRetention:
                // A UTC day boundary gives preview and confirmation the same cutoff; midnight invalidates a preview.
                cutoff = clock.GetUtcNow().UtcDateTime.Date.AddDays(-days);
                var expired = ExecutiveDataPolicy.ExpiredDecisions(state.Decisions.Select(Read), cutoff.Value);
                decisions = state.Decisions.Where(d => expired.Contains(d.DecisionKey)).ToArray();
                var retainedPacks = state.Decisions.Where(d => !expired.Contains(d.DecisionKey))
                    .Select(d => Read(d).Definition.PackKey).ToHashSet();
                packs = state.Packs.Where(p => p.CapturedAtUtc < cutoff && !retainedPacks.Contains(p.PackKey)).ToArray();
                var latestMarket = state.Markets.MaxBy(m => m.Version)?.Id;
                markets = state.Markets.Where(m => m.Id != latestMarket && m.EffectiveAtUtc < cutoff).ToArray();
                events = state.Events.Where(e => e.RecordedAtUtc < cutoff).ToArray();
                break;
            case ExecutiveDataOperation.PurgeTenantData:
                decisions = state.Decisions; packs = state.Packs; markets = state.Markets; events = state.Events;
                break;
        }
        var counts = new ExecutiveDataCounts(decisions.Select(d => d.DecisionKey).Distinct().Count(),
            decisions.Length, packs.Length, markets.Length, events.Length);
        // Include the whole tenant state, not just selected rows: new revisions/dependencies must invalidate confirmation.
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { state, request, days, cutoff }))));
        return new(new(request, counts, cutoff, token), decisions, packs, markets, events);
    }

    private static async Task<DataState> LoadAsync(IScope scope, Guid tenantId) => new(tenantId,
        (await scope.Database.FetchAsync<ExecutiveDecisionVersionDto>(Sql.Builder.Where("tenantId=@0", tenantId).OrderBy("id"))).ToArray(),
        (await scope.Database.FetchAsync<ExecutivePackDto>(Sql.Builder.Where("tenantId=@0", tenantId).OrderBy("id"))).ToArray(),
        (await scope.Database.FetchAsync<MarketSettingsVersionDto>(Sql.Builder.Where("tenantId=@0", tenantId).OrderBy("id"))).ToArray(),
        (await scope.Database.FetchAsync<ExecutiveDataEventDto>(Sql.Builder.Where("tenantId=@0", tenantId).OrderBy("id"))).ToArray());

    private static async Task RecordAsync(IScope scope, Guid tenant, Guid key, string operation, Guid? target,
        string reason, int? actor, ExecutiveDataCounts counts, DateTime now) =>
        await scope.Database.InsertAsync(new ExecutiveDataEventDto
        {
            TenantId = tenant, OperationKey = key, Operation = operation, TargetKey = target, Reason = reason,
            ActorMemberId = actor, CountsJson = JsonSerializer.Serialize(counts), RecordedAtUtc = now
        });

    private static DecisionRevision Read(ExecutiveDecisionVersionDto row) => JsonSerializer.Deserialize<DecisionRevision>(row.PayloadJson)!;
    private static ExecutiveDataEvent Map(ExecutiveDataEventDto row) => new(row.OperationKey, row.Operation, row.TargetKey,
        row.Reason, row.ActorMemberId, JsonSerializer.Deserialize<ExecutiveDataCounts>(row.CountsJson)!,
        DateTime.SpecifyKind(row.RecordedAtUtc, DateTimeKind.Utc));
    private static GdprExportLinkedRecordRow Export(string section, object value, DateTime at) => new(section,
        JsonSerializer.Serialize(value), DateTime.SpecifyKind(at, DateTimeKind.Utc));
    private sealed record DataState(Guid TenantId, ExecutiveDecisionVersionDto[] Decisions, ExecutivePackDto[] Packs,
        MarketSettingsVersionDto[] Markets, ExecutiveDataEventDto[] Events);
    private sealed record DataPlan(ExecutiveDataPreview Preview, ExecutiveDecisionVersionDto[] Decisions,
        ExecutivePackDto[] Packs, MarketSettingsVersionDto[] Markets, ExecutiveDataEventDto[] Events);
}
