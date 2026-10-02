using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// What each recorded review covered: programme and customer, reporting
/// period, declared extract date and the decision it supports. Nullable and
/// not backfilled: an existing review read the whole organisation over all
/// time, and saying so is more honest than inventing a period for it.
/// </summary>
public sealed class AddEvidenceReviewScope(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        var table = EvidenceReviewDto.TableName;
        if (!ColumnExists(table, "scopeKey"))
            Alter.Table(table).AddColumn("scopeKey").AsString(80).Nullable().Do();
        if (!ColumnExists(table, "scopeProgrammeKey"))
            Alter.Table(table).AddColumn("scopeProgrammeKey").AsGuid().Nullable().Do();
        if (!ColumnExists(table, "scopeCustomerKey"))
            Alter.Table(table).AddColumn("scopeCustomerKey").AsGuid().Nullable().Do();
        if (!ColumnExists(table, "scopeLabel"))
            Alter.Table(table).AddColumn("scopeLabel").AsString(512).Nullable().Do();
        if (!ColumnExists(table, "periodFrom"))
            Alter.Table(table).AddColumn("periodFrom").AsDateTime().Nullable().Do();
        if (!ColumnExists(table, "periodTo"))
            Alter.Table(table).AddColumn("periodTo").AsDateTime().Nullable().Do();
        if (!ColumnExists(table, "extractedOn"))
            Alter.Table(table).AddColumn("extractedOn").AsDateTime().Nullable().Do();
        if (!ColumnExists(table, "decisionText"))
            Alter.Table(table).AddColumn("decisionText").AsString(500).Nullable().Do();
        return Task.CompletedTask;
    }
}
