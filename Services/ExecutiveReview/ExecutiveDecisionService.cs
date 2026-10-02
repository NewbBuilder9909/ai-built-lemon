using Microsoft.Extensions.Options;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ExecutiveReview;

public sealed class ExecutiveDecisionService(IReviewAccess access, IExecutiveDecisionRepository decisions,
    IExecutivePackRepository packs, IStaffRepository staff, IStaffAuthorizationService authorization,
    IOptions<ExecutiveReviewOptions> options, TimeProvider clock)
{
    public bool Enabled => options.Value.Enabled;

    public async Task<DecisionQueue> GetQueueAsync(DecisionStatus? status)
    {
        var actor = await RequireAsync(Capability.ViewDeliveryReporting);
        return new(await decisions.GetQueueAsync(actor.TenantId, status), status);
    }

    public async Task<DecisionDetail> GetDetailAsync(Guid decisionKey)
    {
        var actor = await RequireAsync(Capability.ViewDeliveryReporting);
        var history = await decisions.GetHistoryAsync(actor.TenantId, decisionKey);
        if (history.Count == 0) throw new ReviewNotFoundException();
        var pack = await packs.GetAsync(actor.TenantId, history[^1].Definition.PackKey) ?? throw new ReviewNotFoundException();
        var canManage = await authorization.HasAsync(Capability.ManageExecutiveDecisions);
        var people = await PeopleAsync(actor.TenantId);
        if (!canManage)
        {
            var owners = history.SelectMany(r => new Guid?[] { r.Definition.OwnerStaffKey, r.ActionOwnerStaffKey }).ToHashSet();
            var actors = history.Select(r => r.ActorMemberId).ToHashSet();
            people = people.Where(p => owners.Contains(p.StaffKey) || actors.Contains(p.MemberId)).ToArray();
        }
        return new(history, pack, people, canManage, await authorization.HasAsync(Capability.ReviewExecutiveEvidence),
            ExecutiveDecisionPolicy.IsFresh(pack, clock.GetUtcNow().UtcDateTime, options.Value.MaximumPublicationAgeHours));
    }

    public async Task<DecisionEditor> GetEditorAsync(Guid packKey, Guid? decisionKey = null)
    {
        var actor = await RequireAsync(Capability.ManageExecutiveDecisions);
        await RequireAsync(Capability.ViewDeliveryReporting);
        DecisionRevision? current = null;
        if (decisionKey is Guid key)
        {
            current = (await decisions.GetHistoryAsync(actor.TenantId, key)).LastOrDefault() ?? throw new ReviewNotFoundException();
            if (!ExecutiveDecisionPolicy.CanApply(current.Status, DecisionAction.Revise)) throw new ReviewValidationException("Decision.InvalidTransition");
            if (packKey == Guid.Empty) packKey = current.Definition.PackKey;
        }
        var pack = await packs.GetAsync(actor.TenantId, packKey) ?? throw new ReviewNotFoundException();
        var input = current is null ? new DecisionDraftInput { PackKey = packKey } : DecisionDraftInput.From(current.Definition);
        input.PackKey = packKey;
        var availablePacks = (await packs.GetRecentAsync(actor.TenantId))
            .Where(p => p.Source == pack.Source).Append(pack).DistinctBy(p => p.PackKey)
            .OrderByDescending(p => p.CapturedAtUtc).ToArray();
        return new(input, (await PeopleAsync(actor.TenantId)).Where(p => p.IsActive).ToArray(), pack,
            availablePacks, decisionKey, current?.Version);
    }

    public async Task<DecisionRevision> CreateAsync(DecisionDefinition definition)
    {
        var actor = await RequireAsync(Capability.ManageExecutiveDecisions);
        await RequireAsync(Capability.ViewDeliveryReporting);
        return await decisions.CreateAsync(actor, definition);
    }

    public async Task<DecisionRevision> ApplyAsync(Guid decisionKey, int version, DecisionCommand command)
    {
        var capability = command.Action is DecisionAction.AcceptEvidence or DecisionAction.DisputeEvidence
            ? Capability.ReviewExecutiveEvidence : Capability.ManageExecutiveDecisions;
        var actor = await RequireAsync(capability);
        await RequireAsync(Capability.ViewDeliveryReporting);
        return await decisions.ApplyAsync(actor, decisionKey, version, command, options.Value.MaximumPublicationAgeHours);
    }

    private async Task<IReadOnlyList<DecisionPerson>> PeopleAsync(Guid tenantId) =>
        (await staff.GetByTenantAsync(tenantId)).Select(s => new DecisionPerson(s.StaffKey, s.MemberId, s.FullName, s.IsActive)).ToArray();

    private Task<ReviewActor> RequireAsync(string capability)
    {
        if (!Enabled) throw new UnauthorizedAccessException();
        return access.RequireAsync(capability);
    }
}
