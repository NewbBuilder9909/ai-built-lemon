using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Services.Staff;

/// <summary>Admin-only, same gating as StaffAdminController's other actions.</summary>
public interface IGdprService
{
    Task<GdprExport?> BuildExportAsync(Guid staffKey, Guid tenantId);

    Task EraseAsync(Guid staffKey);
}
