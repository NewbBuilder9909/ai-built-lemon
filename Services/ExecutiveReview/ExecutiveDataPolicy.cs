using ProgrammePulse.Models.ExecutiveReview;

namespace ProgrammePulse.Services.ExecutiveReview;

public static class ExecutiveDataPolicy
{
    public static void Validate(ExecutiveDataRequest request, int retentionDays)
    {
        if (request.Operation is not { } operation || !Enum.IsDefined(operation)
            || request.Reason is not { } reason || !Enum.IsDefined(reason))
            throw new ReviewValidationException("Review.Data.InvalidRequest");
        var targeted = operation is ExecutiveDataOperation.RedactDecision or ExecutiveDataOperation.WithdrawPack;
        if (targeted ? request.TargetKey is null || request.TargetKey == Guid.Empty : request.TargetKey is not null)
            throw new ReviewValidationException("Review.Data.InvalidRequest");
        if (operation == ExecutiveDataOperation.ApplyRetention)
        {
            if (retentionDays is < 1 or > 3650) throw new ReviewValidationException("Review.Data.RetentionDisabled");
            if (reason != ExecutiveDataReason.RetentionPolicy) throw new ReviewValidationException("Review.Data.InvalidRequest");
        }
        else if (operation == ExecutiveDataOperation.PurgeTenantData)
        {
            if (reason != ExecutiveDataReason.TenantOffboarding) throw new ReviewValidationException("Review.Data.InvalidRequest");
        }
        else if (reason is not (ExecutiveDataReason.PersonalData or ExecutiveDataReason.IncorrectEvidence))
            throw new ReviewValidationException("Review.Data.InvalidRequest");
    }

    public static IReadOnlySet<Guid> ExpiredDecisions(IEnumerable<DecisionRevision> revisions, DateTime cutoffUtc) =>
        revisions.GroupBy(r => r.DecisionKey).Select(g => g.MaxBy(r => r.Version)!)
            .Where(r => r.Status is DecisionStatus.Closed or DecisionStatus.Dismissed && r.RecordedAtUtc < cutoffUtc)
            .Select(r => r.DecisionKey).ToHashSet();

    public static bool ReferencesSubject(DecisionRevision revision, Guid staffKey, int memberId) =>
        revision.Definition.OwnerStaffKey == staffKey || revision.ActionOwnerStaffKey == staffKey
        || revision.ActorMemberId == memberId;
}
