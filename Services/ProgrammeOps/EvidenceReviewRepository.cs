using System.Text.Json;
using NPoco;
using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class EvidenceReviewRepository(IScopeProvider scopeProvider) : IEvidenceReviewRepository
{
    private const string Newest = "recordedAtUtc DESC, id DESC";

    /// <summary>Every finding column except the source records, for history and GDPR lookups.</summary>
    private const string FindingSummaryColumns =
        "id, tenantId, reviewKey, findingKey, category, severity, title, whyItMatters, numerator, denominator, unit, " +
        "disposition, ownerStaffKey, targetDate, note, decidedAtUtc, decidedByStaffKey, carriedForward";

    public async Task AddAsync(EvidenceReview review)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.InsertAsync(new EvidenceReviewDto
        {
            ReviewKey = review.ReviewKey,
            TenantId = review.TenantId,
            RecordedAtUtc = review.RecordedAtUtc,
            RecordedByStaffKey = review.RecordedByStaffKey,
            Readiness = review.Readiness.ToString(),
            ReadinessReason = Truncate(review.ReadinessReason, 1000),
            WorkItems = review.WorkItems,
            RecordedHours = review.RecordedHours,
            ProjectsJson = JsonSerializer.Serialize(review.Projects),
            SourceNotesJson = JsonSerializer.Serialize(review.SourceNotes),
            ScopeKey = review.Scope?.Key,
            ScopeProgrammeKey = review.Scope?.ProgrammeKey,
            ScopeCustomerKey = review.Scope?.CustomerKey,
            ScopeLabel = review.Scope is { } labelled ? Truncate(labelled.Label, 512) : null,
            PeriodFrom = review.Scope?.PeriodFrom.ToDateTime(TimeOnly.MinValue),
            PeriodTo = review.Scope?.PeriodTo.ToDateTime(TimeOnly.MinValue),
            ExtractedOn = review.Scope?.ExtractedOn?.ToDateTime(TimeOnly.MinValue),
            DecisionText = review.Scope?.Decision is { } decision ? Truncate(decision, 500) : null
        });

        foreach (var finding in review.Findings)
        {
            await scope.Database.InsertAsync(new EvidenceReviewFindingDto
            {
                TenantId = review.TenantId,
                ReviewKey = review.ReviewKey,
                FindingKey = finding.FindingKey,
                Category = finding.Category.ToString(),
                Severity = finding.Severity.ToString(),
                Title = Truncate(finding.Title, 256),
                WhyItMatters = Truncate(finding.WhyItMatters, 1000),
                Numerator = finding.Numerator,
                Denominator = finding.Denominator,
                Unit = Truncate(finding.Unit, 64),
                RecordsJson = JsonSerializer.Serialize(finding.Records),
                Disposition = finding.Disposition.ToString(),
                OwnerStaffKey = finding.OwnerStaffKey,
                TargetDate = finding.TargetDate?.ToDateTime(TimeOnly.MinValue),
                Note = finding.Note,
                DecidedAtUtc = finding.DecidedAtUtc,
                DecidedByStaffKey = finding.DecidedByStaffKey,
                CarriedForward = finding.CarriedForward
            });
        }

        scope.Complete();
    }

    public async Task<EvidenceReview?> GetAsync(Guid tenantId, Guid reviewKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var row = await scope.Database.FirstOrDefaultAsync<EvidenceReviewDto>(
            Sql.Builder.Where("tenantId = @0 AND reviewKey = @1", tenantId, reviewKey));
        return row is null ? null : await LoadAsync(scope.Database, row, withRecords: true);
    }

    public async Task<EvidenceReview?> GetLatestAsync(Guid tenantId, string scopeKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var row = await LatestRowAsync(scope.Database, tenantId, scopeKey);
        return row is null ? null : await LoadAsync(scope.Database, row, withRecords: true);
    }

    public async Task<EvidenceReview?> GetPreviousAsync(Guid tenantId, Guid reviewKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var current = await scope.Database.FirstOrDefaultAsync<EvidenceReviewDto>(
            Sql.Builder.Where("tenantId = @0 AND reviewKey = @1", tenantId, reviewKey));
        if (current is null)
            return null;

        // The previous review of the same scope: comparing a programme with the whole organisation isn't like with like.
        var rows = await scope.Database.FetchAsync<EvidenceReviewDto>(
            Sql.Builder.Select("TOP 1 *").From(EvidenceReviewDto.TableName)
                .Where($"tenantId = @0 AND (recordedAtUtc < @1 OR (recordedAtUtc = @1 AND id < @2)) AND {InScope(3)}",
                    tenantId, current.RecordedAtUtc, current.Id, current.ScopeKey ?? EvidenceScope.WholeOrganisationKey)
                .OrderBy(Newest));
        return rows.Count == 0 ? null : await LoadAsync(scope.Database, rows[0], withRecords: true);
    }

    public async Task<IReadOnlyList<EvidenceReview>> GetHistoryAsync(Guid tenantId, int take)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var rows = await scope.Database.FetchAsync<EvidenceReviewDto>(
            Sql.Builder.Select($"TOP {Math.Max(1, take)} *").From(EvidenceReviewDto.TableName)
                .Where("tenantId = @0", tenantId)
                .OrderBy(Newest));
        if (rows.Count == 0)
            return [];

        var findings = await scope.Database.FetchAsync<EvidenceReviewFindingDto>(
            Sql.Builder.Select(FindingSummaryColumns).From(EvidenceReviewFindingDto.TableName)
                .Where("tenantId = @0 AND reviewKey IN (@1)", tenantId, rows.Select(r => r.ReviewKey).ToList()));
        var byReview = findings.ToLookup(f => f.ReviewKey);
        return rows.Select(r => Map(r, byReview[r.ReviewKey])).ToList();
    }

    public async Task<DecisionWriteOutcome> DecideAsync(Guid tenantId, Guid reviewKey, string findingKey, FindingDisposition disposition,
        Guid? ownerStaffKey, DateOnly? targetDate, string? note, Guid? decidedByStaffKey, DateTime decidedAtUtc)
    {
        using var scope = scopeProvider.CreateScope();
        // Only the latest review of its own scope takes decisions; a later
        // review of another programme doesn't freeze this one.
        var target = await scope.Database.FirstOrDefaultAsync<EvidenceReviewDto>(
            Sql.Builder.Where("tenantId = @0 AND reviewKey = @1", tenantId, reviewKey));
        if (target is null)
        {
            scope.Complete();
            return DecisionWriteOutcome.NotFound;
        }
        var latest = await LatestRowAsync(scope.Database, tenantId, target.ScopeKey ?? EvidenceScope.WholeOrganisationKey);
        if (latest is null)
        {
            scope.Complete();
            return DecisionWriteOutcome.NotFound;
        }

        if (latest.ReviewKey != reviewKey)
        {
            var exists = await scope.Database.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {EvidenceReviewDto.TableName} WHERE tenantId = @0 AND reviewKey = @1", tenantId, reviewKey);
            scope.Complete();
            return exists > 0 ? DecisionWriteOutcome.NotLatest : DecisionWriteOutcome.NotFound;
        }

        var updated = await scope.Database.ExecuteAsync(new Sql(
            $"UPDATE {EvidenceReviewFindingDto.TableName} SET disposition = @3, ownerStaffKey = @4, targetDate = @5, note = @6, " +
            "decidedAtUtc = @7, decidedByStaffKey = @8, carriedForward = 0 " +
            "WHERE tenantId = @0 AND reviewKey = @1 AND findingKey = @2",
            tenantId, reviewKey, findingKey, disposition.ToString(), ownerStaffKey!, targetDate?.ToDateTime(TimeOnly.MinValue)!,
            (string.IsNullOrWhiteSpace(note) ? null : note.Trim())!, decidedAtUtc, decidedByStaffKey!));
        scope.Complete();
        return updated == 1 ? DecisionWriteOutcome.Saved : DecisionWriteOutcome.NotFound;
    }

    public async Task<IReadOnlyList<EvidenceReviewStaffReference>> GetStaffReferencesAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var recorded = await scope.Database.FetchAsync<EvidenceReviewDto>(
            Sql.Builder.Select("id, reviewKey, tenantId, recordedAtUtc, recordedByStaffKey, readiness").From(EvidenceReviewDto.TableName)
                .Where("recordedByStaffKey = @0", staffKey));
        var findings = await scope.Database.FetchAsync<EvidenceReviewFindingDto>(
            Sql.Builder.Select(FindingSummaryColumns).From(EvidenceReviewFindingDto.TableName)
                .Where("ownerStaffKey = @0 OR decidedByStaffKey = @0", staffKey));

        var reviewDates = new Dictionary<Guid, DateTime>();
        var findingReviewKeys = findings.Select(f => f.ReviewKey).Distinct().ToList();
        if (findingReviewKeys.Count > 0)
        {
            var reviews = await scope.Database.FetchAsync<EvidenceReviewDto>(
                Sql.Builder.Select("id, reviewKey, recordedAtUtc").From(EvidenceReviewDto.TableName)
                    .Where("reviewKey IN (@0)", findingReviewKeys));
            reviewDates = reviews.ToDictionary(r => r.ReviewKey, r => r.RecordedAtUtc);
        }

        var references = recorded
            .Select(r => new EvidenceReviewStaffReference($"Recorded an Evidence Check review (readiness {r.Readiness}).", r.RecordedAtUtc))
            .ToList();
        foreach (var finding in findings)
        {
            var when = reviewDates.GetValueOrDefault(finding.ReviewKey, finding.DecidedAtUtc ?? DateTime.MinValue);
            if (finding.OwnerStaffKey == staffKey)
                references.Add(new($"Named owner of \"{finding.Title}\" ({finding.Disposition}).", when));
            if (finding.DecidedByStaffKey == staffKey)
                references.Add(new($"Recorded the decision on \"{finding.Title}\" ({finding.Disposition}).", finding.DecidedAtUtc ?? when));
        }

        return references.OrderBy(r => r.RecordedAtUtc).ToList();
    }

    public async Task EraseStaffReferencesAsync(Guid staffKey)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync($"UPDATE {EvidenceReviewDto.TableName} SET recordedByStaffKey = NULL WHERE recordedByStaffKey = @0", staffKey);
        await scope.Database.ExecuteAsync($"UPDATE {EvidenceReviewFindingDto.TableName} SET ownerStaffKey = NULL WHERE ownerStaffKey = @0", staffKey);
        await scope.Database.ExecuteAsync($"UPDATE {EvidenceReviewFindingDto.TableName} SET decidedByStaffKey = NULL WHERE decidedByStaffKey = @0", staffKey);
        scope.Complete();
    }

    private static async Task<EvidenceReviewDto?> LatestRowAsync(IUmbracoDatabase database, Guid tenantId, string scopeKey)
    {
        var rows = await database.FetchAsync<EvidenceReviewDto>(
            Sql.Builder.Select("TOP 1 *").From(EvidenceReviewDto.TableName)
                .Where($"tenantId = @0 AND {InScope(1)}", tenantId, scopeKey).OrderBy(Newest));
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>Rows of one scope. Reviews from before scopes existed (null key) read the whole organisation.</summary>
    private static string InScope(int parameter) =>
        $"(scopeKey = @{parameter} OR (scopeKey IS NULL AND @{parameter} = '{EvidenceScope.WholeOrganisationKey}'))";

    private static async Task<EvidenceReview> LoadAsync(IUmbracoDatabase database, EvidenceReviewDto row, bool withRecords)
    {
        var findings = await database.FetchAsync<EvidenceReviewFindingDto>(
            Sql.Builder.Select(withRecords ? "*" : FindingSummaryColumns).From(EvidenceReviewFindingDto.TableName)
                .Where("tenantId = @0 AND reviewKey = @1", row.TenantId, row.ReviewKey));
        return Map(row, findings);
    }

    private static EvidenceReview Map(EvidenceReviewDto row, IEnumerable<EvidenceReviewFindingDto> findings) => new()
    {
        ReviewKey = row.ReviewKey,
        TenantId = row.TenantId,
        RecordedAtUtc = DateTime.SpecifyKind(row.RecordedAtUtc, DateTimeKind.Utc),
        RecordedByStaffKey = row.RecordedByStaffKey,
        Readiness = Enum.Parse<EvidenceReadiness>(row.Readiness),
        ReadinessReason = row.ReadinessReason,
        WorkItems = row.WorkItems,
        RecordedHours = row.RecordedHours,
        Projects = Deserialize<List<ProjectEvidenceSummary>>(row.ProjectsJson) ?? [],
        SourceNotes = Deserialize<List<string>>(row.SourceNotesJson) ?? [],
        Scope = row.PeriodFrom is { } from && row.PeriodTo is { } to
            ? new EvidenceScope(row.ScopeProgrammeKey, row.ScopeCustomerKey, row.ScopeLabel ?? "", DateOnly.FromDateTime(from), DateOnly.FromDateTime(to),
                row.ExtractedOn is { } extracted ? DateOnly.FromDateTime(extracted) : null, row.DecisionText)
            : null,
        Findings = findings
            .Select(f => new EvidenceReviewFinding
            {
                FindingKey = f.FindingKey,
                Category = Enum.Parse<EvidenceFindingCategory>(f.Category),
                Severity = Enum.Parse<EvidenceFindingSeverity>(f.Severity),
                Title = f.Title,
                WhyItMatters = f.WhyItMatters,
                Numerator = f.Numerator,
                Denominator = f.Denominator,
                Unit = f.Unit,
                Records = Deserialize<List<EvidenceRecord>>(f.RecordsJson) ?? [],
                Disposition = Enum.Parse<FindingDisposition>(f.Disposition),
                OwnerStaffKey = f.OwnerStaffKey,
                TargetDate = f.TargetDate is { } target ? DateOnly.FromDateTime(target) : null,
                Note = f.Note,
                DecidedAtUtc = f.DecidedAtUtc is { } decided ? DateTime.SpecifyKind(decided, DateTimeKind.Utc) : null,
                DecidedByStaffKey = f.DecidedByStaffKey,
                CarriedForward = f.CarriedForward
            })
            // Same order the check itself uses, so a recorded review reads like the live page.
            .OrderBy(f => f.Category)
            .ThenBy(f => f.Severity)
            .ThenByDescending(f => f.Denominator == 0 ? 0 : f.Numerator / f.Denominator)
            .ToList()
    };

    private static T? Deserialize<T>(string? json) where T : class =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<T>(json);

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
