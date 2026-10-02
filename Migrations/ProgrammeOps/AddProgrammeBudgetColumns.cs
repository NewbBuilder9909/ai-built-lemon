using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Programme-level budget vs actual, for a programme with no formal
/// Contract Ops record — Contract Ops' burn-down already covers the ones
/// that have one. New step rather than editing earlier ones in place, per
/// the append-only convention.
/// </summary>
public sealed class AddProgrammeBudgetColumns(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(ProgrammeDto.TableName, "budgetAmount"))
        {
            Alter.Table(ProgrammeDto.TableName)
                .AddColumn("budgetAmount").AsDecimal(18, 4).Nullable()
                .Do();
        }

        if (!ColumnExists(ProgrammeDto.TableName, "budgetCurrency"))
        {
            Alter.Table(ProgrammeDto.TableName)
                .AddColumn("budgetCurrency").AsString(8).Nullable()
                .Do();
        }

        return Task.CompletedTask;
    }
}
