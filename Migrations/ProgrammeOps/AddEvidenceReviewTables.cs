using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Recorded Evidence Check reviews: the step from a one-off check to a weekly
/// review loop (docs/archive/competitive-position-2026-09-26.md, "What to
/// build next"). The composite unique index is hand-written because the
/// annotation set has no composite form — precedent AddEstimateBaselineTable.
/// </summary>
public sealed class AddEvidenceReviewTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, EvidenceReviewDto.TableName))
        {
            Create.Table<EvidenceReviewDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, EvidenceReviewFindingDto.TableName))
        {
            Create.Table<EvidenceReviewFindingDto>().Do();
            Execute.Sql(
                "CREATE UNIQUE INDEX UX_ProgrammeOps_EvidenceReviewFinding_identity " +
                "ON ProgrammeOps_EvidenceReviewFinding (tenantId, reviewKey, findingKey)").Do();
        }

        return Task.CompletedTask;
    }
}
