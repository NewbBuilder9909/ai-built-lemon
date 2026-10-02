using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Persistence;

namespace ProgrammePulse.Migrations.StaffOps;

/// <summary>
/// Adds StaffOps_AuditLog.tenantId — closes the cross-tenant disclosure where
/// StaffAdminController.Audit read every tenant's StaffOps audit entries
/// (staff emails and roles, rate changes, MFA resets, GDPR actions).
///
/// The backfill attributes each existing row to the tenant that actually owns
/// it, never wholesale to the default tenant: since trial signup (#11) a
/// database can hold several tenants' rows, and stamping them all with the
/// default tenant would move the leak rather than close it. In order:
///
/// 1. "Tenant" rows carry the tenant key as their entity id.
/// 2. Staff/StaffRate/StaffMfa rows carry a staffKey; take that profile's
///    tenant. Erasure anonymises rather than deletes the profile, so erased
///    people's rows still resolve.
/// 3. Anything else takes the acting member's tenant.
/// 4. If exactly one tenant exists, remaining rows are unambiguously its own.
///    (Skipped when Tenancy_Tenant doesn't exist yet: on a fresh database the
///    StaffOps plan runs first, and its audit table is empty anyway.)
///
/// Whatever is still unresolved stays NULL, and GetRecentAsync's
/// "tenantId = @0" never matches NULL — so an unprovable row is hidden from
/// every tenant rather than guessed into one (fail closed).
///
/// Also adds the (tenantId, timestampUtc, id) index the audit page's
/// newest-first, per-tenant query now needs. Hand-written T-SQL, following
/// AddIdentityLinkUniqueness, because the annotation set has no composite form.
/// </summary>
public sealed class AddStaffAuditLogTenantColumn(IMigrationContext context) : AsyncMigrationBase(context)
{
    public const string IndexName = "IX_StaffOps_AuditLog_tenant_timestamp";

    protected override Task MigrateAsync()
    {
        const string audit = StaffAuditLogDto.TableName;

        if (!ColumnExists(audit, "tenantId"))
        {
            Alter.Table(audit)
                .AddColumn("tenantId").AsGuid().Nullable()
                .Do();
        }

        Backfill(Database);

        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{IndexName}' AND object_id = OBJECT_ID('[{audit}]')) " +
            $"CREATE NONCLUSTERED INDEX [{IndexName}] ON [{audit}] ([tenantId], [timestampUtc] DESC, [id] DESC)");

        return Task.CompletedTask;
    }

    /// <summary>
    /// The attribution rules above, idempotent (only ever touches NULL rows).
    /// Separate so SQL integration tests can prove attribution against real
    /// rows without driving the Umbraco migration runner.
    /// </summary>
    internal static void Backfill(IUmbracoDatabase database)
    {
        const string audit = StaffAuditLogDto.TableName;
        const string staff = StaffDto.TableName;
        const string tenants = TenantDto.TableName;

        // No join to Tenancy_Tenant: a "Tenant" row's entity id *is* the tenant
        // key, and the StaffOps plan runs before the Tenancy plan on a fresh
        // database, so that table may not exist yet.
        database.Execute(
            $"UPDATE [{audit}] SET tenantId = TRY_CONVERT(uniqueidentifier, entityId) " +
            "WHERE tenantId IS NULL AND entityType = 'Tenant'");

        database.Execute(
            $"UPDATE a SET a.tenantId = s.tenantId FROM [{audit}] a " +
            $"JOIN [{staff}] s ON s.staffKey = TRY_CONVERT(uniqueidentifier, a.entityId) " +
            "WHERE a.tenantId IS NULL AND a.entityType IN ('Staff', 'StaffRate', 'StaffMfa') AND s.tenantId IS NOT NULL");

        database.Execute(
            $"UPDATE a SET a.tenantId = s.tenantId FROM [{audit}] a " +
            $"JOIN [{staff}] s ON s.memberId = a.actorMemberId " +
            "WHERE a.tenantId IS NULL AND a.entityType <> 'Tenant' AND s.tenantId IS NOT NULL");

        // Checked from C#, not with an IF in the same batch: SQL Server binds
        // every table a batch names before evaluating the IF.
        if (database.ExecuteScalar<int>($"SELECT CASE WHEN OBJECT_ID('[{tenants}]') IS NULL THEN 0 ELSE 1 END") == 1)
        {
            database.Execute(
                $"IF (SELECT COUNT(*) FROM [{tenants}]) = 1 " +
                $"UPDATE [{audit}] SET tenantId = (SELECT TOP 1 tenantKey FROM [{tenants}]) WHERE tenantId IS NULL");
        }
    }
}
