using System.Data;
using System.Text.Json;
using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Services.Staff;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ExecutiveReview;

public interface IExecutiveDecisionRepository
{
    Task<IReadOnlyList<DecisionRevision>> GetQueueAsync(Guid tenantId, DecisionStatus? status);
    Task<IReadOnlyList<DecisionRevision>> GetHistoryAsync(Guid tenantId, Guid decisionKey);
    Task<DecisionRevision> CreateAsync(ReviewActor actor, DecisionDefinition definition);
    Task<DecisionRevision> ApplyAsync(ReviewActor actor, Guid decisionKey, int expectedVersion, DecisionCommand command, int maximumAgeHours);
}

public sealed class ExecutiveDecisionRepository(IScopeProvider scopes, IExecutivePackRepository packs,
    IStaffRepository staff, TimeProvider clock) : IExecutiveDecisionRepository
{
    public async Task<IReadOnlyList<DecisionRevision>> GetQueueAsync(Guid tenantId, DecisionStatus? status)
    {
        if (status is not null && !Enum.IsDefined(status.Value)) throw new ReviewValidationException("Decision.InvalidFields");
        using var scope = scopes.CreateScope(autoComplete: true);
        var sql = Sql.Builder.Append("SELECT TOP 100 d.* FROM ExecutiveReview_DecisionVersion d")
            .Where("d.tenantId = @0", tenantId)
            .Where("NOT EXISTS (SELECT 1 FROM ExecutiveReview_DecisionVersion n WHERE n.tenantId=d.tenantId AND n.decisionKey=d.decisionKey AND n.version>d.version)");
        if (status is not null) sql.Where("d.status = @0", (int)status.Value);
        sql.OrderBy("d.dueOn, d.id DESC");
        return (await scope.Database.FetchAsync<ExecutiveDecisionVersionDto>(sql)).Select(Read).ToArray();
    }

    public async Task<IReadOnlyList<DecisionRevision>> GetHistoryAsync(Guid tenantId, Guid decisionKey)
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        var rows = await scope.Database.FetchAsync<ExecutiveDecisionVersionDto>(
            Sql.Builder.Where("tenantId = @0 AND decisionKey = @1", tenantId, decisionKey).OrderBy("version"));
        return rows.Select(Read).ToArray();
    }

    public async Task<DecisionRevision> CreateAsync(ReviewActor actor, DecisionDefinition definition)
    {
        definition = ExecutiveDecisionPolicy.Validate(definition);
        using var scope = scopes.CreateScope(isolationLevel: IsolationLevel.Serializable);
        await MarketSettingsRepository.LockTenantAsync(scope, actor.TenantId);
        await RequireEvidenceAsync(actor.TenantId, definition);
        await RequireOwnerAsync(actor.TenantId, definition.OwnerStaffKey);
        var revision = new DecisionRevision(Guid.NewGuid(), 1, definition, DecisionStatus.Draft,
            EvidenceDisposition.Unreviewed, null, null, null, null, null, null, null,
            "Created", "", actor.MemberId, clock.GetUtcNow().UtcDateTime);
        await InsertAsync(scope, actor.TenantId, revision);
        scope.Complete();
        return revision;
    }

    public async Task<DecisionRevision> ApplyAsync(ReviewActor actor, Guid decisionKey, int expectedVersion,
        DecisionCommand command, int maximumAgeHours)
    {
        using var scope = scopes.CreateScope(isolationLevel: IsolationLevel.Serializable);
        await MarketSettingsRepository.LockTenantAsync(scope, actor.TenantId);
        var current = (await GetHistoryAsync(actor.TenantId, decisionKey)).LastOrDefault()
            ?? throw new ReviewNotFoundException();
        if (expectedVersion != current.Version) throw new ReviewConflictException();
        var definition = command.Action == DecisionAction.Revise
            ? command.Definition ?? throw new ReviewValidationException("Decision.InvalidFields") : current.Definition;
        var pack = await RequireEvidenceAsync(actor.TenantId, definition);
        if (command.Action is DecisionAction.Revise or DecisionAction.AcceptEvidence or DecisionAction.Decide)
            await RequireOwnerAsync(actor.TenantId, definition.OwnerStaffKey);
        if (command.Action == DecisionAction.Decide && command.ActionOwnerStaffKey is Guid actionOwner)
            await RequireOwnerAsync(actor.TenantId, actionOwner);
        var revision = ExecutiveDecisionPolicy.Apply(current, command, pack, actor.MemberId, clock.GetUtcNow().UtcDateTime, maximumAgeHours);
        await InsertAsync(scope, actor.TenantId, revision);
        scope.Complete();
        return revision;
    }

    private async Task<ExecutivePack> RequireEvidenceAsync(Guid tenantId, DecisionDefinition definition)
    {
        var pack = await packs.GetAsync(tenantId, definition.PackKey) ?? throw new ReviewNotFoundException();
        if (!pack.Items.Any(i => i.WorkItemKey == definition.WorkItemKey)) throw new ReviewValidationException("Decision.InvalidEvidence");
        return pack;
    }

    private async Task RequireOwnerAsync(Guid tenantId, Guid staffKey)
    {
        if (!(await staff.GetByTenantAsync(tenantId)).Any(s => s.StaffKey == staffKey && s.IsActive))
            throw new ReviewValidationException("Decision.InvalidOwner");
    }

    private static async Task InsertAsync(IScope scope, Guid tenantId, DecisionRevision revision) =>
        await scope.Database.InsertAsync(new ExecutiveDecisionVersionDto
        {
            TenantId = tenantId, DecisionKey = revision.DecisionKey, Version = revision.Version,
            Status = (int)revision.Status, DueOn = (revision.Status == DecisionStatus.Decided ? revision.FollowUpOn!.Value : revision.Definition.DueOn).ToDateTime(TimeOnly.MinValue),
            RecordedAtUtc = revision.RecordedAtUtc, ActorMemberId = revision.ActorMemberId,
            PayloadJson = JsonSerializer.Serialize(revision)
        });

    private static DecisionRevision Read(ExecutiveDecisionVersionDto dto) => JsonSerializer.Deserialize<DecisionRevision>(dto.PayloadJson)!;
}

public sealed class ReviewNotFoundException : Exception;
