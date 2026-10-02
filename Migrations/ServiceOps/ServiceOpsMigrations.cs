using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Migrations.ServiceOps;

/// <summary>
/// Its own plan name, not an extension of "SkillsEvidence" — the rule in
/// CLAUDE.md, and here it carries product meaning too. The design
/// document promises a customer can connect a support desk without
/// connecting a repository; Service Ops upgrading and failing
/// independently of the evidence area is what makes that structurally
/// true rather than merely intended.
/// </summary>
public sealed class ServiceOpsMigrationPlan : MigrationPlan
{
    public ServiceOpsMigrationPlan() : base("ServiceOps")
    {
        From(string.Empty)
            .To<AddServiceOpsTables>("2026-09-serviceops-01");
    }
}

/// <summary>
/// Creates the ServiceOps tables and the five composite unique indexes
/// the domain depends on. Idempotent via DoesTableExist and sys.indexes,
/// raw T-SQL for the composites because the annotation set has no
/// composite form — same precedent as AddSkillsEvidenceTables.
///
/// The load-bearing one is the case identity index: it is what makes an
/// <c>updated_since</c> replay safe. Freshdesk paginates by watermark
/// rather than by opaque cursor, so every run deliberately re-reads an
/// overlapping window; without idempotent upsert that would duplicate
/// cases and inflate every demand figure in the product.
/// </summary>
public sealed class AddServiceOpsTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        CreateTableIfMissing<DeskConnectionDto>(DeskConnectionDto.TableName);
        CreateTableIfMissing<SupportCaseFactDto>(SupportCaseFactDto.TableName);
        CreateTableIfMissing<SupportCodeLinkDto>(SupportCodeLinkDto.TableName);
        CreateTableIfMissing<SupportCaseParticipantDto>(SupportCaseParticipantDto.TableName);
        CreateTableIfMissing<DeskAgentLinkDto>(DeskAgentLinkDto.TableName);
        CreateTableIfMissing<DeskCoverageDto>(DeskCoverageDto.TableName);
        CreateTableIfMissing<RawDeskPayloadDto>(RawDeskPayloadDto.TableName);
        CreateTableIfMissing<ServiceOpsAuditLogDto>(ServiceOpsAuditLogDto.TableName);

        CreateUniqueIndexIfMissing(
            "UX_ServiceOps_DeskConnection_account",
            DeskConnectionDto.TableName,
            "([tenantId], [provider], [sourceAccountId])");

        CreateUniqueIndexIfMissing(
            "UX_ServiceOps_SupportCaseFact_identity",
            SupportCaseFactDto.TableName,
            "([tenantId], [connectionKey], [externalTicketId])");

        CreateUniqueIndexIfMissing(
            "UX_ServiceOps_SupportCodeLink_identity",
            SupportCodeLinkDto.TableName,
            "([tenantId], [connectionKey], [externalTicketId], [artifactType], [artifactExternalId])");

        // Role is part of the key: one person can be both resolver and
        // reviewer on a case, and those are two different claims.
        CreateUniqueIndexIfMissing(
            "UX_ServiceOps_CaseParticipant_identity",
            SupportCaseParticipantDto.TableName,
            "([tenantId], [connectionKey], [externalTicketId], [externalAgentId], [role])");

        CreateUniqueIndexIfMissing(
            "UX_ServiceOps_AgentLink_agent",
            DeskAgentLinkDto.TableName,
            "([tenantId], [connectionKey], [externalAgentId])");

        CreateUniqueIndexIfMissing(
            "UX_ServiceOps_Coverage_stream",
            DeskCoverageDto.TableName,
            "([tenantId], [connectionKey], [stream])");

        return Task.CompletedTask;
    }

    private void CreateTableIfMissing<TDto>(string tableName)
    {
        if (!SqlSyntax.DoesTableExist(Database, tableName))
        {
            Create.Table<TDto>().Do();
        }
    }

    private void CreateUniqueIndexIfMissing(string indexName, string table, string columns) =>
        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('[{table}]')) " +
            $"CREATE UNIQUE NONCLUSTERED INDEX [{indexName}] ON [{table}] {columns}");
}

/// <summary>
/// Runs the ServiceOps plan on every startup — tracked by plan name via
/// IKeyValueService, a safe no-op once the tables exist, and independent
/// of every other plan.
/// </summary>
public sealed class ServiceOpsMigrationStartupHandler(
    IMigrationPlanExecutor migrationPlanExecutor,
    IScopeProvider scopeProvider,
    IKeyValueService keyValueService) : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    public async Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        var upgrader = new Upgrader(new ServiceOpsMigrationPlan());
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
