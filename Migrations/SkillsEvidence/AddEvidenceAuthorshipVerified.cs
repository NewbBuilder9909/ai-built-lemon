using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.SkillsEvidence;

/// <summary>
/// Adds authorshipVerified to SkillsEvidence_EngineeringEvidence: whether the
/// provider verified that the person on a commit row made the commit. Existing
/// rows stay null, which reads as "not verified" until the next sync rewrites
/// them. Idempotent.
/// </summary>
public sealed class AddEvidenceAuthorshipVerified(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(EngineeringEvidenceDto.TableName, "authorshipVerified"))
            Alter.Table(EngineeringEvidenceDto.TableName).AddColumn("authorshipVerified")
                .AsBoolean().Nullable().Do();
        return Task.CompletedTask;
    }
}
