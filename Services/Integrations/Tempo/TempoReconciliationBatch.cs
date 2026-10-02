using System.Globalization;
using System.Text.Json;

namespace ProgrammePulse.Services.Integrations.Tempo;

public sealed record TempoWorklogFact(long Id, long? IssueId, string? AccountId, long Seconds, DateOnly WorkDate);
public sealed record TempoWorklog(TempoWorklogFact Fact, JsonElement Payload);
public sealed record TempoDeletion(long Id, DateTimeOffset DeletedAtUtc, JsonElement Payload);
public sealed record TempoReconciliationBatch(IReadOnlyList<TempoWorklog> Worklogs,
    IReadOnlyList<TempoDeletion> Deletions, int DuplicateWorklogs, int DuplicateDeletions, int SuppressedWorklogs)
{
    public static TempoReconciliationBatch Build(IReadOnlyList<JsonElement> worklogs,
        IReadOnlyList<JsonElement> deletions, DateOnly? auditFrom, DateTimeOffset observedAtUtc)
    {
        if (deletions.Count > 0 && auditFrom is null) throw new JsonException("Deletion evidence requires an audit window.");
        var uniqueWorklogs = new Dictionary<long, TempoWorklog>();
        var uniqueDeletions = new Dictionary<long, TempoDeletion>();
        foreach (var payload in worklogs)
        {
            var id = Id(payload, "tempoWorklogId");
            var secondsText = Value(payload, "timeSpentSeconds");
            if (!long.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                || seconds > int.MaxValue || !DateOnly.TryParseExact(Value(payload, "startDate"), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new JsonException("Tempo returned an invalid duration or work date.");
            long? issue = payload.TryGetProperty("issue", out var issueValue) && issueValue.ValueKind == JsonValueKind.Object
                && issueValue.TryGetProperty("id", out var issueId) && issueId.ValueKind != JsonValueKind.Null
                ? Id(issueValue, "id") : null;
            var author = payload.TryGetProperty("author", out var authorValue) && authorValue.ValueKind == JsonValueKind.Object
                ? Value(authorValue, "accountId") : null;
            var fact = new TempoWorklogFact(id, issue, string.IsNullOrWhiteSpace(author) ? null : author, seconds, date);
            if (uniqueWorklogs.TryGetValue(id, out var previous) && previous.Fact != fact)
                throw new JsonException("Tempo returned conflicting versions of the same worklog; retry the extraction.");
            uniqueWorklogs.TryAdd(id, new(fact, payload));
        }
        foreach (var payload in deletions)
        {
            var id = Id(payload, "tempoWorklogId");
            // The audit API examples omit a zone; interpret those timestamps as UTC, never server-local time.
            if (!DateTimeOffset.TryParse(Value(payload, "deletedAt"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var deletedAt)
                || deletedAt > observedAtUtc || deletedAt.UtcDateTime < auditFrom!.Value.ToDateTime(TimeOnly.MinValue))
                throw new JsonException("Tempo returned a deletion outside the requested audit window.");
            if (!uniqueDeletions.TryGetValue(id, out var previous) || deletedAt > previous.DeletedAtUtc)
                uniqueDeletions[id] = new(id, deletedAt, payload);
        }
        var surviving = uniqueWorklogs.Values.Where(w => !uniqueDeletions.ContainsKey(w.Fact.Id)).OrderBy(w => w.Fact.Id).ToArray();
        return new(surviving, uniqueDeletions.Values.OrderBy(d => d.Id).ToArray(),
            worklogs.Count - uniqueWorklogs.Count, deletions.Count - uniqueDeletions.Count, uniqueWorklogs.Count - surviving.Length);
    }

    private static long Id(JsonElement payload, string key) =>
        long.TryParse(Value(payload, key), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value : throw new JsonException("Tempo returned a record without a positive integer ID.");

    private static string? Value(JsonElement payload, string key)
    {
        if (payload.ValueKind != JsonValueKind.Object) throw new JsonException("Tempo returned a non-object record.");
        return !payload.TryGetProperty(key, out var value) ? null : value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null
        };
    }
}
