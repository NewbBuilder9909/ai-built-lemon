using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// One declared repository → project link. The composite indexes (one live
/// link per repository and project; links by project) are hand-written in
/// AddCodeRepositoryLinkTable because the annotation set has no composite or
/// filtered form.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class CodeRepositoryLinkDto
{
    public const string TableName = "ProgrammeOps_CodeRepositoryLink";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("linkKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid LinkKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("provider")]
    [Length(32)]
    public string Provider { get; set; } = null!;

    [Column("sourceAccountId")]
    [Length(128)]
    public string SourceAccountId { get; set; } = null!;

    [Column("repositoryKey")]
    [Length(256)]
    public string RepositoryKey { get; set; } = null!;

    [Column("projectKey")]
    public Guid ProjectKey { get; set; }

    [Column("note")]
    [Length(512)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Note { get; set; }

    [Column("linkedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? LinkedByStaffKey { get; set; }

    [Column("linkedAtUtc")]
    public DateTime LinkedAtUtc { get; set; }

    [Column("removedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? RemovedAtUtc { get; set; }

    [Column("removedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? RemovedByStaffKey { get; set; }
}
