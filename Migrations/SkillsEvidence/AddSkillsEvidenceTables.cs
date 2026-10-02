using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.SkillsEvidence;

/// <summary>
/// Creates the three SkillsEvidence tables and the two uniqueness rules the
/// domain depends on. Idempotent via DoesTableExist / sys.indexes checks,
/// same as every other feature area's first migration.
///
/// Both indexes are raw T-SQL rather than annotations: the annotation set
/// has no composite or filtered form, and the filtered one is the whole
/// mechanism behind "one current assertion, all history kept". Precedent
/// for both is Migrations/ProgrammeOps/AddEstimateBaselineTable (composite
/// unique) and AddIdentityLinkUniqueness (filtered unique, guarded by
/// sys.indexes). SQL Server is the only provider this site runs on,
/// LocalDB included.
/// </summary>
public sealed class AddSkillsEvidenceTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    public const string SkillKeyIndexName = "UX_SkillsEvidence_SkillDefinition_tenant_skillKey";
    public const string CurrentAssertionIndexName = "UX_SkillsEvidence_StaffSkillAssertion_current";

    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, SkillDefinitionDto.TableName))
        {
            Create.Table<SkillDefinitionDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, StaffSkillAssertionDto.TableName))
        {
            Create.Table<StaffSkillAssertionDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, SkillsEvidenceAuditLogDto.TableName))
        {
            Create.Table<SkillsEvidenceAuditLogDto>().Do();
        }

        // One definition per skill key per tenant. Two tenants may both
        // track "billing"; one tenant may not track it twice.
        CreateIndexIfMissing(
            SkillKeyIndexName,
            SkillDefinitionDto.TableName,
            "([tenantId], [skillKey])",
            filter: null);

        // At most one *current* assertion per person per skill, with every
        // superseded row left in place. Without the filter this would allow
        // a single history row only; with it, history is unbounded and the
        // live row is unique — which is exactly the invariant
        // SkillsEvidenceRepository relies on when it supersedes-then-inserts
        // inside one transaction.
        CreateIndexIfMissing(
            CurrentAssertionIndexName,
            StaffSkillAssertionDto.TableName,
            "([tenantId], [staffKey], [skillKey])",
            filter: "[supersededAtUtc] IS NULL");

        return Task.CompletedTask;
    }

    private void CreateIndexIfMissing(string indexName, string table, string columns, string? filter)
    {
        var where = filter is null ? string.Empty : $" WHERE {filter}";
        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('[{table}]')) " +
            $"CREATE UNIQUE NONCLUSTERED INDEX [{indexName}] ON [{table}] {columns}{where}");
    }
}
