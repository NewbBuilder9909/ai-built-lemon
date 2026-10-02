using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>Existing source billability keeps its prior meaning; newer unverified sources can say unknown.</summary>
public sealed class AddTimeEntryBillabilityKnown(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(TimeEntryDto.TableName, "billabilityKnown"))
            Alter.Table(TimeEntryDto.TableName).AddColumn("billabilityKnown")
                .AsBoolean().NotNullable().WithDefaultValue(true).Do();
        return Task.CompletedTask;
    }
}
