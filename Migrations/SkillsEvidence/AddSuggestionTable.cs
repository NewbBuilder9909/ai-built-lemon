using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.SkillsEvidence;

/// <summary>
/// Slice 5: the suggestion queue.
///
/// The composite unique index is what stops the engine nagging.
/// Regenerating suggestions is idempotent on
/// (tenantId, kind, subjectStaffKey, subjectKey, relatedKey), so a
/// proposal somebody dismissed is not re-raised on the next run — a
/// suggestion queue that repeats itself is one people stop reading, and
/// an unread queue hides the useful entries as effectively as having
/// none.
///
/// SQL Server treats NULLs as equal within a unique index, which is the
/// behaviour wanted here: two component-level suggestions with no
/// subject staff key collapse to one, rather than the index ignoring
/// them.
/// </summary>
public sealed class AddSuggestionTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    public const string IdentityIndexName = "UX_SkillsEvidence_Suggestion_identity";

    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, SuggestionDto.TableName))
        {
            Create.Table<SuggestionDto>().Do();
        }

        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{IdentityIndexName}' AND object_id = OBJECT_ID('[{SuggestionDto.TableName}]')) " +
            $"CREATE UNIQUE NONCLUSTERED INDEX [{IdentityIndexName}] ON [{SuggestionDto.TableName}] " +
            "([tenantId], [kind], [subjectStaffKey], [subjectKey], [relatedKey])");

        return Task.CompletedTask;
    }
}
