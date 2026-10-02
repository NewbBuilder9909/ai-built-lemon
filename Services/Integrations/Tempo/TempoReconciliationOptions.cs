using System.ComponentModel.DataAnnotations;

namespace ProgrammePulse.Services.Integrations.Tempo;

public sealed class TempoReconciliationOptions
{
    public const string SectionName = "TempoReconciliation";
    public Guid[] EnabledTenantIds { get; set; } = [];
    [Range(1, 180)] public int DeletionLookbackDays { get; set; } = 90;
    public bool IsEnabled(Guid tenantId) => EnabledTenantIds.Contains(tenantId);
}
