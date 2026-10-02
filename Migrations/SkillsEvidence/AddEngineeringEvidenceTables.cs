using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.SkillsEvidence;

/// <summary>
/// Slice 2: the engineering-evidence tables, added to the existing
/// SkillsEvidence plan rather than a new one — same feature area, and the
/// evidence contract references the same tenants and staff keys the skills
/// matrix does.
///
/// Four composite unique indexes carry rules the C# cannot enforce alone:
///
///   Connection    (tenantId, provider, sourceAccountId) — one connection
///                 per source account, but several accounts per tenant,
///                 which is what "multiple accounts of the same provider"
///                 requires.
///   Evidence      (tenantId, connectionKey, sourceType, externalId, role)
///                 — the idempotency key. Replaying a page updates rows
///                 rather than duplicating them, which is what makes a
///                 crashed run safe to re-run.
///   ActorLink     (tenantId, connectionKey, externalActorId) — one
///                 account maps to at most one person.
///   UnmappedActor (tenantId, connectionKey, externalActorId) — repeated
///                 sightings update one queue row.
///   Coverage      (tenantId, connectionKey, repositoryKey, stream) — one
///                 cursor per stream.
///
/// Raw T-SQL for the same reason as AddSkillsEvidenceTables: the
/// annotation set has no composite form. Idempotent via DoesTableExist and
/// sys.indexes.
/// </summary>
public sealed class AddEngineeringEvidenceTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        CreateTableIfMissing<EvidenceConnectionDto>(EvidenceConnectionDto.TableName);
        CreateTableIfMissing<EngineeringEvidenceDto>(EngineeringEvidenceDto.TableName);
        CreateTableIfMissing<EvidenceActorLinkDto>(EvidenceActorLinkDto.TableName);
        CreateTableIfMissing<UnmappedEvidenceActorDto>(UnmappedEvidenceActorDto.TableName);
        CreateTableIfMissing<EvidenceCoverageDto>(EvidenceCoverageDto.TableName);
        CreateTableIfMissing<RawEvidencePayloadDto>(RawEvidencePayloadDto.TableName);

        CreateUniqueIndexIfMissing(
            "UX_SkillsEvidence_Connection_account",
            EvidenceConnectionDto.TableName,
            "([tenantId], [provider], [sourceAccountId])");

        CreateUniqueIndexIfMissing(
            "UX_SkillsEvidence_EngineeringEvidence_identity",
            EngineeringEvidenceDto.TableName,
            "([tenantId], [connectionKey], [sourceType], [externalId], [role])");

        CreateUniqueIndexIfMissing(
            "UX_SkillsEvidence_ActorLink_actor",
            EvidenceActorLinkDto.TableName,
            "([tenantId], [connectionKey], [externalActorId])");

        CreateUniqueIndexIfMissing(
            "UX_SkillsEvidence_UnmappedActor_actor",
            UnmappedEvidenceActorDto.TableName,
            "([tenantId], [connectionKey], [externalActorId])");

        CreateUniqueIndexIfMissing(
            "UX_SkillsEvidence_Coverage_stream",
            EvidenceCoverageDto.TableName,
            "([tenantId], [connectionKey], [repositoryKey], [stream])");

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
