using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class IssueDto
{
    public const string TableName = "ProgrammeOps_Issue";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("issueKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid IssueKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("projectKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_Issue_projectKey")]
    public Guid ProjectKey { get; set; }

    [Column("title")]
    [Length(256)]
    public string Title { get; set; } = null!;

    [Column("description")]
    [Length(1024)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Description { get; set; }

    [Column("severity")]
    [Length(32)]
    public string Severity { get; set; } = null!;

    [Column("status")]
    [Length(32)]
    public string Status { get; set; } = null!;

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}
