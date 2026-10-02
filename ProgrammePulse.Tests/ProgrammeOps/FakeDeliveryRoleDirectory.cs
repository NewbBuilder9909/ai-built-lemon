using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>Stands in for Member Group lookups: the given member ids hold only oversight roles.</summary>
public sealed class FakeDeliveryRoleDirectory(params int[] oversightOnlyMemberIds) : IDeliveryRoleDirectory
{
    public Task<IReadOnlySet<int>> GetOversightOnlyMemberIdsAsync() =>
        Task.FromResult<IReadOnlySet<int>>(oversightOnlyMemberIds.ToHashSet());
}
