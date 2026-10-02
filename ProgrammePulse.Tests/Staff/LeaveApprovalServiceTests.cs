using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.Staff;

public class LeaveApprovalServiceTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeLeaveRequestRepository : ILeaveRequestRepository
    {
        public readonly Dictionary<Guid, LeaveRequest> Requests = [];

        public Task<LeaveRequest?> GetByRequestKeyAsync(Guid requestKey, Guid tenantId) =>
            Task.FromResult(Requests.GetValueOrDefault(requestKey) is { } r && r.TenantId == tenantId ? r : null);

        public Task<IReadOnlyList<LeaveRequest>> GetForStaffAsync(Guid staffKey, Guid tenantId) =>
            Task.FromResult<IReadOnlyList<LeaveRequest>>(Requests.Values.Where(r => r.StaffKey == staffKey && r.TenantId == tenantId).ToList());

        public Task<IReadOnlyList<LeaveRequest>> GetPendingAsync(Guid tenantId) =>
            Task.FromResult<IReadOnlyList<LeaveRequest>>(Requests.Values.Where(r => r.Status == LeaveRequestStatus.Pending && r.TenantId == tenantId).ToList());

        public Task<LeaveRequest> CreateAsync(LeaveRequest request, Guid tenantId)
        {
            request = request with { TenantId = tenantId };
            Requests[request.RequestId] = request;
            return Task.FromResult(request);
        }

        public Task UpdateAsync(LeaveRequest request, Guid tenantId)
        {
            if (!Requests.TryGetValue(request.RequestId, out var existing) || existing.TenantId != tenantId)
            {
                throw new CrossTenantReferenceException("LeaveRequest", request.RequestId);
            }

            Requests[request.RequestId] = request;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAvailabilityRepository : IAvailabilityRepository
    {
        public readonly List<Availability> Created = [];

        public Task<IReadOnlyList<Availability>> GetForStaffAsync(Guid staffKey, DateOnly from, DateOnly to) =>
            Task.FromResult<IReadOnlyList<Availability>>(Created.Where(a => a.StaffKey == staffKey).ToList());

        public Task<Availability> CreateAsync(Availability availability)
        {
            Created.Add(availability);
            return Task.FromResult(availability);
        }
    }

    private sealed class FakeStaffAuthorizationService(bool isApprover) : IStaffAuthorizationService
    {
        public Task<bool> IsLoggedInAsync() => Task.FromResult(true);
        public Task<bool> IsAdminAsync() => Task.FromResult(isApprover);
        public Task<bool> IsBoardOrAdminAsync() => Task.FromResult(isApprover);
        public Task<bool> IsHolidayApproverOrAdminAsync() => Task.FromResult(isApprover);
        public Task<bool> IsTeamLeadOrAboveAsync() => Task.FromResult(isApprover);
        public Task<bool> IsStaffAsync() => Task.FromResult(true);
        public Task<bool> IsPlatformAdminAsync() => Task.FromResult(false);

        // This local fake models one axis only — "is this member an approver".
        // ApproveLeave follows that flag; everything else stays false so a
        // capability check added here later has to be considered explicitly.
        public Task<bool> HasAsync(string capability) =>
            Task.FromResult(isApprover && capability == Capability.ApproveLeave);
    }

    private static readonly Guid StaffKey = Guid.NewGuid();
    private static readonly Guid ApproverKey = Guid.NewGuid();

    private static LeaveApprovalService BuildSut(
        FakeLeaveRequestRepository leaveRequests,
        FakeAvailabilityRepository availability,
        bool isApprover,
        DateTimeOffset? now = null) =>
        new(leaveRequests, availability, new FakeStaffAuthorizationService(isApprover),
            new FixedTimeProvider(now ?? DateTimeOffset.UtcNow));

    [Fact]
    public async Task SubmitAsync_creates_a_pending_request()
    {
        var leaveRequests = new FakeLeaveRequestRepository();
        var sut = BuildSut(leaveRequests, new FakeAvailabilityRepository(), isApprover: false);

        var result = await sut.SubmitAsync(StaffKey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), LeaveType.Holiday, "Family trip", TestTenantId);

        Assert.Equal(LeaveRequestStatus.Pending, result.Status);
        Assert.Equal(StaffKey, result.StaffKey);
        Assert.Single(leaveRequests.Requests);
    }

    [Fact]
    public async Task SubmitAsync_rejects_end_date_before_start_date()
    {
        var sut = BuildSut(new FakeLeaveRequestRepository(), new FakeAvailabilityRepository(), isApprover: false);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.SubmitAsync(StaffKey, new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 1), LeaveType.Holiday, null, TestTenantId));
    }

    [Fact]
    public async Task ApproveAsync_throws_when_caller_is_not_an_approver()
    {
        var leaveRequests = new FakeLeaveRequestRepository();
        var sut = BuildSut(leaveRequests, new FakeAvailabilityRepository(), isApprover: false);
        var submitted = await sut.SubmitAsync(StaffKey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1), LeaveType.Holiday, null, TestTenantId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.ApproveAsync(submitted.RequestId, ApproverKey, TestTenantId));
    }

    [Fact]
    public async Task ApproveAsync_marks_the_request_approved_and_records_the_approver()
    {
        var leaveRequests = new FakeLeaveRequestRepository();
        var sut = BuildSut(leaveRequests, new FakeAvailabilityRepository(), isApprover: true);
        var submitted = await sut.SubmitAsync(StaffKey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1), LeaveType.Holiday, null, TestTenantId);

        var approved = await sut.ApproveAsync(submitted.RequestId, ApproverKey, TestTenantId);

        Assert.Equal(LeaveRequestStatus.Approved, approved.Status);
        Assert.Equal(ApproverKey, approved.ApprovedByStaffKey);
        Assert.NotNull(approved.DecidedAtUtc);
    }

    [Fact]
    public async Task ApproveAsync_creates_one_availability_row_per_day_of_the_leave()
    {
        var leaveRequests = new FakeLeaveRequestRepository();
        var availability = new FakeAvailabilityRepository();
        var sut = BuildSut(leaveRequests, availability, isApprover: true);
        var submitted = await sut.SubmitAsync(StaffKey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), LeaveType.Holiday, null, TestTenantId);

        await sut.ApproveAsync(submitted.RequestId, ApproverKey, TestTenantId);

        Assert.Equal(3, availability.Created.Count);
        Assert.All(availability.Created, a =>
        {
            Assert.Equal(AvailabilityStatus.Holiday, a.Status);
            Assert.Equal(AvailabilitySource.LeavePolicy, a.Source);
        });
    }

    [Fact]
    public async Task RejectAsync_marks_the_request_rejected_and_keeps_the_reason_as_notes()
    {
        var leaveRequests = new FakeLeaveRequestRepository();
        var sut = BuildSut(leaveRequests, new FakeAvailabilityRepository(), isApprover: true);
        var submitted = await sut.SubmitAsync(StaffKey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1), LeaveType.Holiday, null, TestTenantId);

        var rejected = await sut.RejectAsync(submitted.RequestId, ApproverKey, "Not enough cover that week", TestTenantId);

        Assert.Equal(LeaveRequestStatus.Rejected, rejected.Status);
        Assert.Equal("Not enough cover that week", rejected.Notes);
    }

    [Fact]
    public async Task ApproveAsync_throws_when_the_request_does_not_exist()
    {
        var sut = BuildSut(new FakeLeaveRequestRepository(), new FakeAvailabilityRepository(), isApprover: true);

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => sut.ApproveAsync(Guid.NewGuid(), ApproverKey, TestTenantId));
    }

    [Fact]
    public async Task ApproveAsync_throws_when_the_request_belongs_to_a_different_tenant()
    {
        var leaveRequests = new FakeLeaveRequestRepository();
        var sut = BuildSut(leaveRequests, new FakeAvailabilityRepository(), isApprover: true);
        var submitted = await sut.SubmitAsync(StaffKey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1), LeaveType.Holiday, null, TestTenantId);

        var otherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => sut.ApproveAsync(submitted.RequestId, ApproverKey, otherTenantId));
    }
}
