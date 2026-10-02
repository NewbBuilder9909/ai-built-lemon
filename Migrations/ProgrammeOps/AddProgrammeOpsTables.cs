using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Creates the ProgrammeOps tables (Bronze raw capture + Silver canonical
/// entities). Idempotent via DoesTableExist checks, same as
/// Migrations/StaffOps/AddStaffOpsTables.
/// </summary>
public sealed class AddProgrammeOpsTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, RawClickUpPayloadDto.TableName))
        {
            Create.Table<RawClickUpPayloadDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, ProgrammeDto.TableName))
        {
            Create.Table<ProgrammeDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, ProjectDto.TableName))
        {
            Create.Table<ProjectDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, WorkstreamDto.TableName))
        {
            Create.Table<WorkstreamDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, WorkItemDto.TableName))
        {
            Create.Table<WorkItemDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, DependencyDto.TableName))
        {
            Create.Table<DependencyDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, RiskDto.TableName))
        {
            Create.Table<RiskDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, IssueDto.TableName))
        {
            Create.Table<IssueDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, AuditLogDto.TableName))
        {
            Create.Table<AuditLogDto>().Do();
        }

        return Task.CompletedTask;
    }
}
