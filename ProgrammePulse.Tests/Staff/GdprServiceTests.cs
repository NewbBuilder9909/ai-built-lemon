using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Tests.ProgrammeOps;

namespace ProgrammePulse.Tests.Staff;

public class GdprServiceTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FakeAvailabilityRepository : IAvailabilityRepository
    {
        public readonly List<Availability> Rows = [];
        public DateOnly? LastFrom;
        public DateOnly? LastTo;

        public Task<IReadOnlyList<Availability>> GetForStaffAsync(Guid staffKey, DateOnly from, DateOnly to)
        {
            LastFrom = from;
            LastTo = to;
            return Task.FromResult<IReadOnlyList<Availability>>(Rows.Where(a => a.StaffKey == staffKey && a.Date >= from && a.Date <= to).ToList());
        }

        public Task<Availability> CreateAsync(Availability availability)
        {
            Rows.Add(availability);
            return Task.FromResult(availability);
        }
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
            Requests[request.RequestId] = request;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStaffRateRepository : IStaffRateRepository
    {
        public readonly List<StaffRate> Rates = [];

        public Task<StaffRate?> GetCurrentAsync(Guid staffKey) =>
            Task.FromResult(Rates.Where(r => r.StaffKey == staffKey && r.EffectiveToUtc is null).OrderByDescending(r => r.EffectiveFromUtc).FirstOrDefault());

        public Task<IReadOnlyList<StaffRate>> GetHistoryAsync(Guid staffKey) =>
            Task.FromResult<IReadOnlyList<StaffRate>>(Rates.Where(r => r.StaffKey == staffKey).ToList());

        public Task<StaffRate> SetCurrentRateAsync(Guid staffKey, decimal costPerHour, string rateCurrency, Guid changedByStaffKey) =>
            throw new NotSupportedException("Not needed for these tests.");
    }

    private sealed class FakeWorkHoursHistoryRepository : IWorkHoursHistoryRepository
    {
        public readonly List<WorkHoursHistory> Rows = [];

        public Task<WorkHoursHistory?> GetCurrentAsync(Guid staffKey) =>
            Task.FromResult(Rows.Where(r => r.StaffKey == staffKey && r.EffectiveToUtc is null).OrderByDescending(r => r.EffectiveFromUtc).FirstOrDefault());

        public Task<IReadOnlyList<WorkHoursHistory>> GetHistoryAsync(Guid staffKey) =>
            Task.FromResult<IReadOnlyList<WorkHoursHistory>>(Rows.Where(r => r.StaffKey == staffKey).ToList());

        public Task<WorkHoursHistory> SetCurrentHoursAsync(Guid staffKey, decimal hoursPerWeek, Guid changedByStaffKey) =>
            throw new NotSupportedException("Not needed for these tests.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static StaffProfile MakeStaff() => new()
    {
        StaffKey = Guid.NewGuid(),
        MemberId = 1,
        FullName = "Jamie Gardner",
        Email = "jamie@example.com",
        JobTitle = "Producer",
        Department = "Drama",
        Team = "Drama",
        IsActive = true,
        DefaultWorkHoursPerWeek = 37.5m,
        CreatedAtUtc = Now.UtcDateTime,
        UpdatedAtUtc = Now.UtcDateTime
    };

    [Fact]
    public async Task BuildExportAsync_returns_null_for_an_unknown_staff_key()
    {
        var sut = new GdprService(new FakeStaffRepository(), new FakeAvailabilityRepository(), new FakeLeaveRequestRepository(), new FakeStaffRateRepository(), new FakeWorkHoursHistoryRepository(), new FakeStaffAuditLogRepository(), [], new FixedTimeProvider(Now));

        var export = await sut.BuildExportAsync(Guid.NewGuid(), TestTenantId);

        Assert.Null(export);
    }

    /// <summary>
    /// Regression test for a live bug caught by an actual run against SQL
    /// Server (not caught by any fake-backed test, since the fakes don't
    /// enforce column-type range limits): GdprService.BuildExportAsync used
    /// to pass DateOnly.MinValue (0001-01-01) as the "since forever" lower
    /// bound for availability rows. AvailabilityRepository converts that to
    /// a DateTime for the SQL parameter, and SQL Server's datetime column
    /// type only goes back to 1753-01-01 — anything earlier throws
    /// SqlTypeException at the ADO.NET layer, a 500 on every GDPR export.
    /// </summary>
    [Fact]
    public async Task BuildExportAsync_never_queries_availability_with_a_date_before_year_1753()
    {
        var staffRepository = new FakeStaffRepository();
        var availability = new FakeAvailabilityRepository();
        var staff = MakeStaff();
        staffRepository.Staff.Add(staff);

        var sut = new GdprService(staffRepository, availability, new FakeLeaveRequestRepository(), new FakeStaffRateRepository(), new FakeWorkHoursHistoryRepository(), new FakeStaffAuditLogRepository(), [], new FixedTimeProvider(Now));
        await sut.BuildExportAsync(staff.StaffKey, TestTenantId);

        Assert.NotNull(availability.LastFrom);
        Assert.True(availability.LastFrom >= new DateOnly(1753, 1, 1),
            $"GetForStaffAsync was called with from={availability.LastFrom}, which SQL Server's datetime column type can't represent (minimum is 1753-01-01) — this reproduces a live 500 error.");
    }

    [Fact]
    public async Task BuildExportAsync_includes_profile_availability_leave_and_rate_history()
    {
        var staffRepository = new FakeStaffRepository();
        var availability = new FakeAvailabilityRepository();
        var leaveRequests = new FakeLeaveRequestRepository();
        var rates = new FakeStaffRateRepository();

        var staff = MakeStaff();
        staffRepository.Staff.Add(staff);

        availability.Rows.Add(new Availability { StaffKey = staff.StaffKey, Date = new DateOnly(2026, 6, 1), StartTime = TimeOnly.MinValue, EndTime = TimeOnly.MaxValue, Status = AvailabilityStatus.Holiday, Source = AvailabilitySource.LeavePolicy });

        var leaveRequest = new LeaveRequest { RequestId = Guid.NewGuid(), TenantId = TestTenantId, StaffKey = staff.StaffKey, RequestedFrom = new DateOnly(2026, 6, 1), RequestedTo = new DateOnly(2026, 6, 1), Type = LeaveType.Holiday, Status = LeaveRequestStatus.Approved, CreatedAtUtc = Now.UtcDateTime };
        leaveRequests.Requests[leaveRequest.RequestId] = leaveRequest;

        rates.Rates.Add(new StaffRate { StaffKey = staff.StaffKey, CostPerHour = 45m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staff.StaffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });

        var workHours = new FakeWorkHoursHistoryRepository();
        workHours.Rows.Add(new WorkHoursHistory { StaffKey = staff.StaffKey, HoursPerWeek = 37.5m, EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staff.StaffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });

        var sut = new GdprService(staffRepository, availability, leaveRequests, rates, workHours, new FakeStaffAuditLogRepository(), [], new FixedTimeProvider(Now));
        var export = await sut.BuildExportAsync(staff.StaffKey, TestTenantId);

        Assert.NotNull(export);
        Assert.Equal("Jamie Gardner", export!.Profile.FullName);
        Assert.Single(export.Availability);
        Assert.Single(export.LeaveRequests);
        Assert.Single(export.RateHistory);
        Assert.Equal(45m, export.RateHistory[0].CostPerHour);
        Assert.Single(export.WorkHoursHistory);
        Assert.Equal(37.5m, export.WorkHoursHistory[0].HoursPerWeek);
    }

    [Fact]
    public async Task BuildExportAsync_includes_the_audit_trail_recorded_against_the_subject()
    {
        var staffRepository = new FakeStaffRepository();
        var staff = MakeStaff();
        staffRepository.Staff.Add(staff);

        var audit = new FakeStaffAuditLogRepository();
        await audit.LogAsync("StaffRate", staff.StaffKey.ToString(), "RateChanged", 42, "{\"costPerHour\":45}", Now.UtcDateTime, TestTenantId);
        await audit.LogAsync("Staff", Guid.NewGuid().ToString(), "Created", 42, null, Now.UtcDateTime, TestTenantId);

        var sut = new GdprService(staffRepository, new FakeAvailabilityRepository(), new FakeLeaveRequestRepository(), new FakeStaffRateRepository(), new FakeWorkHoursHistoryRepository(), audit, [], new FixedTimeProvider(Now));
        var export = await sut.BuildExportAsync(staff.StaffKey, TestTenantId);

        var row = Assert.Single(export!.AuditTrail);
        Assert.Equal("RateChanged", row.Action);
        Assert.Equal(42, row.ActorMemberId);
    }

    [Fact]
    public async Task BuildExportAsync_leaves_out_another_tenants_audit_rows_about_the_same_key()
    {
        // The export is the subject's copy of what this tenant did with their
        // data. Another tenant's rows name its own actors and detail, and a
        // shared StaffKey is not a licence to read them.
        var staffRepository = new FakeStaffRepository();
        var staff = MakeStaff();
        staffRepository.Staff.Add(staff);

        var audit = new FakeStaffAuditLogRepository();
        await audit.LogAsync("Staff", staff.StaffKey.ToString(), "GdprExported", 42, null, Now.UtcDateTime, TestTenantId);
        await audit.LogAsync("Staff", staff.StaffKey.ToString(), "RateChanged", 99, "{\"costPerHour\":80}", Now.UtcDateTime, Guid.NewGuid());

        var sut = new GdprService(staffRepository, new FakeAvailabilityRepository(), new FakeLeaveRequestRepository(), new FakeStaffRateRepository(), new FakeWorkHoursHistoryRepository(), audit, [], new FixedTimeProvider(Now));
        var export = await sut.BuildExportAsync(staff.StaffKey, TestTenantId);

        Assert.Equal("GdprExported", Assert.Single(export!.AuditTrail).Action);
    }

    [Fact]
    public async Task Export_includes_and_erasure_removes_records_held_by_other_feature_areas()
    {
        // Programme Ops' identity links carry the subject's source-tool user
        // id and email — they must appear in the Art. 15 export and go on
        // Art. 17 erasure, without the Staff domain depending on Programme Ops.
        var staffRepository = new FakeStaffRepository();
        var staff = new StaffProfile
        {
            StaffKey = Guid.NewGuid(), MemberId = 1, FullName = "Jamie Gardner", Email = "jamie@example.com",
            CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime
        };
        staffRepository.Staff.Add(staff);
        var identity = new FakeIdentityResolutionRepository();
        identity.Links.Add(new ProgrammePulse.Models.Programme.ExternalIdentityLink
        {
            LinkKey = Guid.NewGuid(), ExternalSource = "ClickUp", ExternalUserId = "42", Email = "jamie@example.com", StaffKey = staff.StaffKey, CreatedAtUtc = Now.UtcDateTime
        });
        identity.Links.Add(new ProgrammePulse.Models.Programme.ExternalIdentityLink
        {
            LinkKey = Guid.NewGuid(), ExternalSource = "ClickUp", ExternalUserId = "99", StaffKey = Guid.NewGuid(), CreatedAtUtc = Now.UtcDateTime
        });
        var participant = new ProgrammePulse.Services.ProgrammeOps.IdentityLinkDataParticipant(identity);

        var sut = new GdprService(staffRepository, new FakeAvailabilityRepository(), new FakeLeaveRequestRepository(), new FakeStaffRateRepository(), new FakeWorkHoursHistoryRepository(), new FakeStaffAuditLogRepository(), [participant], new FixedTimeProvider(Now));
        var export = await sut.BuildExportAsync(staff.StaffKey, TestTenantId);

        var linked = Assert.Single(export!.LinkedRecords);
        Assert.Equal("Identity links", linked.Section);
        Assert.Contains("user id 42", linked.Summary);

        await sut.EraseAsync(staff.StaffKey);

        Assert.DoesNotContain(identity.Links, l => l.StaffKey == staff.StaffKey);
        Assert.Single(identity.Links);
        Assert.False(staffRepository.Staff.Single(s => s.StaffKey == staff.StaffKey).IsActive);
    }

    [Fact]
    public async Task EraseAsync_scrubs_pii_but_leaves_availability_leave_and_rate_rows_linked()
    {
        var staffRepository = new FakeStaffRepository();
        var availability = new FakeAvailabilityRepository();
        var leaveRequests = new FakeLeaveRequestRepository();
        var rates = new FakeStaffRateRepository();

        var staff = MakeStaff();
        staffRepository.Staff.Add(staff);
        availability.Rows.Add(new Availability { StaffKey = staff.StaffKey, Date = new DateOnly(2026, 6, 1), StartTime = TimeOnly.MinValue, EndTime = TimeOnly.MaxValue, Status = AvailabilityStatus.Holiday, Source = AvailabilitySource.LeavePolicy });
        rates.Rates.Add(new StaffRate { StaffKey = staff.StaffKey, CostPerHour = 45m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staff.StaffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });

        var sut = new GdprService(staffRepository, availability, leaveRequests, rates, new FakeWorkHoursHistoryRepository(), new FakeStaffAuditLogRepository(), [], new FixedTimeProvider(Now));
        await sut.EraseAsync(staff.StaffKey);

        var updated = await staffRepository.GetByStaffKeyAsync(staff.StaffKey);
        Assert.Equal("Erased Staff Member", updated!.FullName);
        Assert.StartsWith("erased-", updated.Email);
        Assert.Null(updated.JobTitle);
        Assert.Null(updated.Department);
        Assert.False(updated.IsActive);

        // Financial/audit history is untouched by erasure.
        Assert.Single(await availability.GetForStaffAsync(staff.StaffKey, DateOnly.MinValue, DateOnly.MaxValue));
        Assert.Single(await rates.GetHistoryAsync(staff.StaffKey));
    }
}
