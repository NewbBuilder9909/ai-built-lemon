using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Finding B3 of docs/architecture-review-2026-09-24.md: alert detection runs
/// after every sync and on demand, and each run checks for an open alert
/// before raising one. Two runs at once both saw none and both inserted.
/// This makes "one open alert per (tenant, type, entity)" a database rule.
///
/// Pre-flight: duplicates already open would make the index fail to build,
/// so all but the earliest of each set are acknowledged (not deleted, so the
/// history stays readable) with no acknowledging person, which is how a
/// system action is recorded on this table. Idempotent.
/// </summary>
public sealed class AddOpenAlertUniqueness(IMigrationContext context) : AsyncMigrationBase(context)
{
    public const string IndexName = "UX_ProgrammeOps_Alert_open";

    protected override Task MigrateAsync()
    {
        var table = AlertDto.TableName;
        Database.Execute(
            $"""
            WITH ranked AS (
                SELECT acknowledgedAtUtc,
                       ROW_NUMBER() OVER (PARTITION BY tenantId, [type], entityKey ORDER BY raisedAtUtc, id) AS rn
                FROM [{table}]
                WHERE acknowledgedAtUtc IS NULL)
            UPDATE ranked SET acknowledgedAtUtc = SYSUTCDATETIME() WHERE rn > 1
            """);

        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{IndexName}' AND object_id = OBJECT_ID('[{table}]')) " +
            $"CREATE UNIQUE NONCLUSTERED INDEX [{IndexName}] ON [{table}] ([tenantId], [type], [entityKey]) WHERE [acknowledgedAtUtc] IS NULL");
        return Task.CompletedTask;
    }
}
