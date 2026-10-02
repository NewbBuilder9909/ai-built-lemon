using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.SkillsEvidence;

/// <summary>
/// Slice 4: manager-approved component ownership, approved backups, the
/// Platinum action layer, and the tenant's recorded data-processing
/// decision for person-level evidence.
///
/// Added to the existing SkillsEvidence plan rather than a new one —
/// same feature area, and the coverage view reads the skill assertions
/// from step 01 directly.
///
/// One filtered unique index carries a rule the C# cannot: at most one
/// *live* processing decision per tenant, with every superseded one kept.
/// Superseding rather than editing is what makes "what were we relying
/// on in March, and who said so" answerable a year later — the same
/// reasoning, and the same mechanism, as the assertion history in step 01.
/// </summary>
public sealed class AddContinuityTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    public const string LiveDecisionIndexName = "UX_SkillsEvidence_ProcessingDecision_live";

    protected override Task MigrateAsync()
    {
        CreateTableIfMissing<ComponentOwnershipDto>(ComponentOwnershipDto.TableName);
        CreateTableIfMissing<ComponentBackupDto>(ComponentBackupDto.TableName);
        CreateTableIfMissing<CoverageActionDto>(CoverageActionDto.TableName);
        CreateTableIfMissing<EvidenceProcessingDecisionDto>(EvidenceProcessingDecisionDto.TableName);

        CreateIndexIfMissing(
            "UX_SkillsEvidence_ComponentOwnership_component",
            ComponentOwnershipDto.TableName,
            "([tenantId], [componentKey])",
            filter: null);

        CreateIndexIfMissing(
            "UX_SkillsEvidence_ComponentBackup_person",
            ComponentBackupDto.TableName,
            "([tenantId], [componentKey], [staffKey])",
            filter: null);

        CreateIndexIfMissing(
            LiveDecisionIndexName,
            EvidenceProcessingDecisionDto.TableName,
            "([tenantId])",
            filter: "[withdrawnAtUtc] IS NULL");

        return Task.CompletedTask;
    }

    private void CreateTableIfMissing<TDto>(string tableName)
    {
        if (!SqlSyntax.DoesTableExist(Database, tableName))
        {
            Create.Table<TDto>().Do();
        }
    }

    private void CreateIndexIfMissing(string indexName, string table, string columns, string? filter)
    {
        var where = filter is null ? string.Empty : $" WHERE {filter}";
        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('[{table}]')) " +
            $"CREATE UNIQUE NONCLUSTERED INDEX [{indexName}] ON [{table}] {columns}{where}");
    }
}
