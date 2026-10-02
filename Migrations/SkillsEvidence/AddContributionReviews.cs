using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.SkillsEvidence;

public sealed class AddContributionReviews(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, SkillContributionReviewDto.TableName))
            Create.Table<SkillContributionReviewDto>().Do();

        Index("UX_ContributionReview_revision", "UNIQUE", "tenantId, linkKey, revision", "");
        Index("UX_ContributionReview_example", "UNIQUE", "tenantId, staffKey, assertionKey, evidenceKey", "WHERE revision = 1");
        Index("IX_ContributionReview_subject", "", "tenantId, staffKey, recordedAtUtc", "");
        return Task.CompletedTask;
    }

    private void Index(string name, string uniqueness, string columns, string filter) => Database.Execute(
        $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('{SkillContributionReviewDto.TableName}')) " +
        $"CREATE {uniqueness} NONCLUSTERED INDEX [{name}] ON [{SkillContributionReviewDto.TableName}] ({columns}) {filter}");
}
