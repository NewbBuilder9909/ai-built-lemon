using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Staff;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.Staff;

public sealed class LeaveRequestRepository(IScopeProvider scopeProvider) : ILeaveRequestRepository
{
    public async Task<LeaveRequest?> GetByRequestKeyAsync(Guid requestKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<LeaveRequestDto>(
            Sql.Builder.Where("requestKey = @0 AND tenantId = @1", requestKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyList<LeaveRequest>> GetForStaffAsync(Guid staffKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<LeaveRequestDto>(
            Sql.Builder.Where("staffKey = @0 AND tenantId = @1", staffKey, tenantId).OrderBy("createdAtUtc DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<LeaveRequest>> GetPendingAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<LeaveRequestDto>(
            Sql.Builder.Where("status = @0 AND tenantId = @1", nameof(LeaveRequestStatus.Pending), tenantId).OrderBy("createdAtUtc"));
        return dtos.Select(Map).ToList();
    }

    public async Task<LeaveRequest> CreateAsync(LeaveRequest request, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = new LeaveRequestDto
        {
            RequestKey = request.RequestId,
            TenantId = tenantId,
            StaffKey = request.StaffKey,
            RequestedFrom = request.RequestedFrom.ToDateTime(TimeOnly.MinValue),
            RequestedTo = request.RequestedTo.ToDateTime(TimeOnly.MinValue),
            Type = request.Type.ToString(),
            Status = request.Status.ToString(),
            ApprovedByStaffKey = request.ApprovedByStaffKey,
            Notes = request.Notes,
            CreatedAtUtc = request.CreatedAtUtc,
            DecidedAtUtc = request.DecidedAtUtc
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return request with { TenantId = tenantId };
    }

    public async Task UpdateAsync(LeaveRequest request, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<LeaveRequestDto>(
            Sql.Builder.Where("requestKey = @0 AND tenantId = @1", request.RequestId, tenantId))
            ?? throw new CrossTenantReferenceException("LeaveRequest", request.RequestId);

        dto.Status = request.Status.ToString();
        dto.ApprovedByStaffKey = request.ApprovedByStaffKey;
        dto.Notes = request.Notes;
        dto.DecidedAtUtc = request.DecidedAtUtc;

        await scope.Database.UpdateAsync(dto);
        scope.Complete();
    }

    private static LeaveRequest Map(LeaveRequestDto dto) => new()
    {
        RequestId = dto.RequestKey,
        TenantId = dto.TenantId,
        StaffKey = dto.StaffKey,
        RequestedFrom = DateOnly.FromDateTime(dto.RequestedFrom),
        RequestedTo = DateOnly.FromDateTime(dto.RequestedTo),
        Type = Enum.Parse<LeaveType>(dto.Type),
        Status = Enum.Parse<LeaveRequestStatus>(dto.Status),
        ApprovedByStaffKey = dto.ApprovedByStaffKey,
        Notes = dto.Notes,
        CreatedAtUtc = dto.CreatedAtUtc,
        DecidedAtUtc = dto.DecidedAtUtc
    };
}
