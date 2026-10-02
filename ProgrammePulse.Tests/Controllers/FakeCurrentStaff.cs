using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.Controllers;

/// <summary>A fixed signed-in caller (or nobody), standing in for the request-scoped ICurrentStaff.</summary>
public sealed class FakeCurrentStaff(StaffProfile? profile = null) : ICurrentStaff
{
    public Task<int?> GetMemberIdAsync() => Task.FromResult(profile?.MemberId);

    public Task<StaffProfile?> GetProfileAsync() => Task.FromResult(profile);
}
