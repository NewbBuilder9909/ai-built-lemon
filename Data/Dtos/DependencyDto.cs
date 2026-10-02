using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class DependencyDto
{
    public const string TableName = "ProgrammeOps_Dependency";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("dependencyKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid DependencyKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("workItemKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_Dependency_workItemKey")]
    public Guid WorkItemKey { get; set; }

    [Column("dependsOnWorkItemKey")]
    public Guid DependsOnWorkItemKey { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }
}
