using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class UnresolvedIdentityDto
{
    public const string TableName = "ProgrammeOps_UnresolvedIdentity";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("unresolvedIdentityKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid UnresolvedIdentityKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("externalSource")]
    [Length(64)]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_UnresolvedIdentity_externalSource")]
    public string ExternalSource { get; set; } = null!;

    [Column("externalUserId")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalUserId { get; set; }

    [Column("email")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Email { get; set; }

    [Column("displayName")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? DisplayName { get; set; }

    [Column("context")]
    [Length(64)]
    public string Context { get; set; } = null!;

    [Column("firstSeenUtc")]
    public DateTime FirstSeenUtc { get; set; }

    [Column("lastSeenUtc")]
    public DateTime LastSeenUtc { get; set; }

    [Column("occurrenceCount")]
    public int OccurrenceCount { get; set; }

    [Column("suggestedStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? SuggestedStaffKey { get; set; }

    [Column("resolvedStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ResolvedStaffKey { get; set; }

    [Column("resolvedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ResolvedAtUtc { get; set; }
}
