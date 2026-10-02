using System.Data;
using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Staff;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Staff;

// Keep participant erasure and profile deactivation atomic, including when a later participant fails.
public sealed class TransactionalGdprService(GdprService inner, IStaffRepository staff, IScopeProvider scopes) : IGdprService
{
    public async Task<GdprExport?> BuildExportAsync(Guid staffKey, Guid tenantId)
    {
        var subject = await staff.GetByStaffKeyAsync(staffKey);
        if (subject?.TenantId != tenantId) return null;
        return await inner.BuildExportAsync(staffKey, tenantId);
    }

    public async Task EraseAsync(Guid staffKey)
    {
        var subject = await staff.GetByStaffKeyAsync(staffKey)
            ?? throw new InvalidOperationException("Staff profile not found.");
        using var scope = scopes.CreateScope(isolationLevel: IsolationLevel.Serializable);
        if (subject.TenantId is Guid tenantId)
        {
            var tenant = await scope.Database.FirstOrDefaultAsync<TenantDto>(
                "SELECT * FROM Tenancy_Tenant WITH (UPDLOCK, HOLDLOCK) WHERE tenantKey=@0", tenantId);
            if (tenant is null) throw new UnauthorizedAccessException();
        }
        var current = await staff.GetByStaffKeyAsync(staffKey);
        if (current?.TenantId != subject.TenantId) throw new UnauthorizedAccessException();
        await inner.EraseAsync(staffKey);
        scope.Complete();
    }
}
