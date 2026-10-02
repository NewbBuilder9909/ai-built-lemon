using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// One row per (tenant, source), acquired with a single conditional UPDATE
/// (owner is null or the lease has expired) — the database-backed lease
/// that lets a second application instance refuse to start a run the
/// first instance already has in flight. See
/// Services/ProgrammeOps/SyncRunRepository.TryAcquireAsync. The live unique
/// constraint is UX_ProgrammeOps_SyncLease_tenant_source on (tenantId,
/// source), created by Migrations/ProgrammeOps/RekeySyncLeaseByTenant —
/// this class predates tenancy and originally had a single-column unique
/// index on Source alone; that attribute is removed rather than left here
/// misleadingly, since NPoco's [Index] only affects brand-new tables.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class SyncLeaseDto
{
    public const string TableName = "ProgrammeOps_SyncLease";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("source")]
    [Length(64)]
    public string Source { get; set; } = null!;

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("ownerInstanceId")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? OwnerInstanceId { get; set; }

    [Column("leaseExpiresAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? LeaseExpiresAtUtc { get; set; }

    [Column("runKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? RunKey { get; set; }
}
