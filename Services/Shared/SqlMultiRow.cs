using System.Text;
using System.Text.RegularExpressions;
using Umbraco.Cms.Infrastructure.Persistence;

namespace ProgrammePulse.Services.Shared;

/// <summary>
/// Set-based writes for sync paths that used to cost several round trips per
/// row (finding A3 of docs/architecture-review-2026-09-24.md). Each call
/// writes many rows per statement, chunked to SQL Server's limits: 2,100
/// parameters per command and 1,000 rows per VALUES list.
///
/// Table and column names are interpolated, so they must come from DTO
/// constants in code, never from input. Every value is a parameter.
/// </summary>
public static class SqlMultiRow
{
    private const int MaxParameters = 2000;
    private const int MaxRowsPerValues = 1000;

    /// <summary>INSERT INTO table (columns) VALUES (...), (...).</summary>
    public static async Task InsertAsync(IUmbracoDatabase database, string table, IReadOnlyList<string> columns, IEnumerable<object?[]> rows)
    {
        if (!Regex.IsMatch(table, "^[a-zA-Z0-9_]+$"))
            throw new ArgumentException("Invalid input");
        foreach (var col in columns)
        {
            if (!Regex.IsMatch(col, "^[a-zA-Z0-9_]+$"))
                throw new ArgumentException("Invalid input");
        }
        foreach (var chunk in rows.Chunk(RowsPerStatement(columns.Count)))
        {
            var sql = new StringBuilder($"INSERT INTO {SqlIdentifier.Quote(table)} ({ColumnList(columns)}) VALUES ");
            var args = new List<object>(chunk.Length * columns.Count);
            AppendValues(sql, args, chunk, columns.Count);
            await database.ExecuteAsync(sql.ToString(), args.ToArray());
        }
    }

    /// <summary>
    /// UPDATE the rows whose <paramref name="keyColumn"/> matches, setting
    /// <paramref name="setColumns"/>, and only where tenantId matches: a key
    /// from another tenant updates nothing. Each row is the key followed by
    /// the set columns' values, in order.
    /// </summary>
    public static async Task UpdateAsync(
        IUmbracoDatabase database, string table, string keyColumn, IReadOnlyList<string> setColumns, IEnumerable<object?[]> rows, Guid tenantId)
    {
        var width = setColumns.Count + 1;
        foreach (var chunk in rows.Chunk(RowsPerStatement(width + 1)))
        {
            var sql = new StringBuilder("UPDATE t SET ");
            sql.AppendJoin(", ", setColumns.Select(c => $"t.{SqlIdentifier.Quote(c)} = v.{SqlIdentifier.Quote(c)}"));
            sql.Append($" FROM {SqlIdentifier.Quote(table)} t JOIN (VALUES ");
            var args = new List<object>(chunk.Length * width + 1);
            AppendValues(sql, args, chunk, width);
            sql.Append($") AS v ({SqlIdentifier.Quote(keyColumn)}, {ColumnList(setColumns)}) ON t.{SqlIdentifier.Quote(keyColumn)} = v.{SqlIdentifier.Quote(keyColumn)} WHERE t.[tenantId] = @{args.Count}");
            args.Add(tenantId);
            await database.ExecuteAsync(sql.ToString(), args.ToArray());
        }
    }

    private static int RowsPerStatement(int width) => Math.Max(1, Math.Min(MaxRowsPerValues, MaxParameters / width));

    private static string ColumnList(IEnumerable<string> columns) => string.Join(", ", columns.Select(SqlIdentifier.Quote));

    private static void AppendValues(StringBuilder sql, List<object> args, IReadOnlyList<object?[]> rows, int width)
    {
        for (var r = 0; r < rows.Count; r++)
        {
            if (rows[r].Length != width)
            {
                throw new ArgumentException($"Row {r} has {rows[r].Length} values; expected {width}.");
            }

            sql.Append(r == 0 ? "(" : ", (");
            for (var c = 0; c < width; c++)
            {
                sql.Append(c == 0 ? string.Empty : ", ").Append('@').Append(args.Count);
                // NPoco sends a null element as DBNull; its signature just says object.
                args.Add(rows[r][c]!);
            }

            sql.Append(')');
        }
    }
}
