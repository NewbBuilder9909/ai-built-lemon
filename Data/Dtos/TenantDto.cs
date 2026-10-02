using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class TenantDto
{
    public const string TableName = "Tenancy_Tenant";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("tenantKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid TenantKey { get; set; }

    [Column("name")]
    [Length(200)]
    public string Name { get; set; } = null!;

    [Column("shortCode")]
    [Length(32)]
    [Index(IndexTypes.UniqueNonClustered)]
    public string ShortCode { get; set; } = null!;

    [Column("isActive")]
    public bool IsActive { get; set; }

    // The four lifecycle columns below were added by
    // Migrations/Tenancy/AddTenantLifecycleColumns as nullable-then-backfilled
    // (the codebase's convention for widening a live table); TenantRepository
    // maps a null back to the pre-lifecycle meaning (Active/Enterprise).

    [Column("status")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? Status { get; set; }

    // "planName", not "plan": PLAN is a reserved word in T-SQL, and while
    // NPoco escapes identifiers in the SQL it generates, hand-written SQL
    // (migrations, operator runbooks) shouldn't have to remember to.
    [Column("planName")]
    [Length(32)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Plan { get; set; }

    [Column("trialEndsAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? TrialEndsAtUtc { get; set; }

    [Column("updatedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? UpdatedAtUtc { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }
}
