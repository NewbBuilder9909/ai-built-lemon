using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ContractOps;

/// <summary>
/// Step 2 of docs/delivery-evidence-and-contract-assurance.md: the contract
/// obligations register and repository control attestations. New tables, so
/// tenantId is non-nullable from the start.
///
/// UX_ContractOps_RepositoryControlAttestation_current is load-bearing: one
/// current attestation per (tenant, repository, control), while every
/// superseded one is kept. Idempotent.
/// </summary>
public sealed class AddObligationTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    public const string CurrentAttestationIndexName = "UX_ContractOps_RepositoryControlAttestation_current";

    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, ContractObligationDto.TableName))
        {
            Create.Table<ContractObligationDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, RepositoryControlAttestationDto.TableName))
        {
            Create.Table<RepositoryControlAttestationDto>().Do();
        }

        CreateIndex("IX_ContractOps_Obligation_tenant_contract", ContractObligationDto.TableName,
            "CREATE NONCLUSTERED INDEX [{0}] ON [{1}] ([tenantId], [contractKey])");
        CreateIndex(CurrentAttestationIndexName, RepositoryControlAttestationDto.TableName,
            "CREATE UNIQUE NONCLUSTERED INDEX [{0}] ON [{1}] ([tenantId], [provider], [sourceAccountId], [repositoryKey], [control]) WHERE [supersededAtUtc] IS NULL");
        return Task.CompletedTask;
    }

    private void CreateIndex(string name, string table, string createTemplate) =>
        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('[{table}]')) " +
            string.Format(System.Globalization.CultureInfo.InvariantCulture, createTemplate, name, table));
}
