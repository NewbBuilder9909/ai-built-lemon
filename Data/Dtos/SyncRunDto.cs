using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class SyncRunDto
{
    public const string TableName = "ProgrammeOps_SyncRun";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("runKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid RunKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("source")]
    [Length(64)]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_SyncRun_source")]
    public string Source { get; set; } = null!;

    [Column("status")]
    [Length(16)]
    public string Status { get; set; } = null!;

    [Column("startedAtUtc")]
    public DateTime StartedAtUtc { get; set; }

    [Column("heartbeatAtUtc")]
    public DateTime HeartbeatAtUtc { get; set; }

    [Column("finishedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? FinishedAtUtc { get; set; }

    [Column("triggeredByMemberId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? TriggeredByMemberId { get; set; }

    [Column("instanceId")]
    [Length(128)]
    public string InstanceId { get; set; } = null!;

    [Column("stage")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Stage { get; set; }

    [Column("summary")]
    [Length(512)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Summary { get; set; }

    [Column("summaryJson")]
    [Length(2000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? SummaryJson { get; set; }

    [Column("error")]
    [Length(2000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Error { get; set; }
}
