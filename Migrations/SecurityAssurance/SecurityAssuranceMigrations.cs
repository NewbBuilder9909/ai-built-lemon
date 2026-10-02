using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Migrations.SecurityAssurance;

/// <summary>
/// Its own plan: security findings are a feature area of their own (step 3
/// of docs/delivery-evidence-and-contract-assurance.md), upgrading and
/// failing independently of Contract Ops and the evidence area.
/// </summary>
public sealed class SecurityAssuranceMigrationPlan : MigrationPlan
{
    public SecurityAssuranceMigrationPlan() : base("SecurityAssurance")
    {
        From(string.Empty)
            .To<AddSecurityAssuranceTables>("2026-09-securityassurance-01");
    }
}

/// <summary>
/// New tables, so tenantId is non-nullable throughout. The identity indexes
/// are what make a replayed sync idempotent: every run re-reads the tool's
/// full export, and without them a replay would duplicate findings and
/// overstate every count on the assurance page. Idempotent.
/// </summary>
public sealed class AddSecurityAssuranceTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        CreateTableIfMissing<SecurityConnectionDto>(SecurityConnectionDto.TableName);
        CreateTableIfMissing<SecurityFindingDto>(SecurityFindingDto.TableName);
        CreateTableIfMissing<RepositoryGateObservationDto>(RepositoryGateObservationDto.TableName);
        CreateTableIfMissing<SecurityCheckRunDto>(SecurityCheckRunDto.TableName);
        CreateTableIfMissing<SecurityRawPayloadDto>(SecurityRawPayloadDto.TableName);
        CreateTableIfMissing<SecurityAuditLogDto>(SecurityAuditLogDto.TableName);

        CreateIndexIfMissing("UX_SecurityAssurance_Connection_tool", SecurityConnectionDto.TableName, "([tenantId], [tool])", unique: true);
        CreateIndexIfMissing("UX_SecurityAssurance_Finding_identity", SecurityFindingDto.TableName, "([tenantId], [tool], [externalId])", unique: true);
        CreateIndexIfMissing("UX_SecurityAssurance_RepositoryObservation_identity", RepositoryGateObservationDto.TableName, "([tenantId], [tool], [toolRepositoryId])", unique: true);
        CreateIndexIfMissing("UX_SecurityAssurance_CheckRun_identity", SecurityCheckRunDto.TableName, "([tenantId], [tool], [externalId])", unique: true);
        CreateIndexIfMissing("IX_SecurityAssurance_RawPayload_fetched", SecurityRawPayloadDto.TableName, "([fetchedAtUtc])", unique: false);
        CreateIndexIfMissing("IX_SecurityAssurance_AuditLog_tenant_timestamp", SecurityAuditLogDto.TableName, "([tenantId], [timestampUtc])", unique: false);
        return Task.CompletedTask;
    }

    private void CreateTableIfMissing<TDto>(string tableName)
    {
        if (!SqlSyntax.DoesTableExist(Database, tableName))
        {
            Create.Table<TDto>().Do();
        }
    }

    private void CreateIndexIfMissing(string indexName, string table, string columns, bool unique) =>
        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('[{table}]')) " +
            $"CREATE {(unique ? "UNIQUE " : string.Empty)}NONCLUSTERED INDEX [{indexName}] ON [{table}] {columns}");
}

/// <summary>Runs the SecurityAssurance plan on every startup; a no-op once current.</summary>
public sealed class SecurityAssuranceMigrationStartupHandler(
    IMigrationPlanExecutor migrationPlanExecutor,
    IScopeProvider scopeProvider,
    IKeyValueService keyValueService) : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    public async Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        var upgrader = new Upgrader(new SecurityAssuranceMigrationPlan());
        await upgrader.ExecuteAsync(migrationPlanExecutor, scopeProvider, keyValueService);
    }

    public async Task HandleAsync(IEnumerable<UmbracoApplicationStartingNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            await HandleAsync(notification, cancellationToken);
        }
    }
}
