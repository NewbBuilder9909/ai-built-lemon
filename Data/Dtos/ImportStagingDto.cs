using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// An uploaded file waiting for the person who uploaded it to confirm a
/// replacement that removes rows. Customer data, so it is short-lived: the
/// row goes on confirm or cancel, stale rows are purged, and the delivery
/// data purge covers the table.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class ImportStagingDto
{
    public const string TableName = "ProgrammeOps_ImportStaging";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("stagingKey")]
    [Index(IndexTypes.UniqueNonClustered, Name = "UX_ProgrammeOps_ImportStaging_stagingKey")]
    public Guid StagingKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("kind")]
    [Length(32)]
    public string Kind { get; set; } = null!;

    [Column("content")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string Content { get; set; } = null!;

    /// <summary>What the preview showed, so confirm can refuse if the data changed since.</summary>
    [Column("planFingerprint")]
    [Length(64)]
    public string PlanFingerprint { get; set; } = null!;

    [Column("createdByMemberId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? CreatedByMemberId { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }
}
