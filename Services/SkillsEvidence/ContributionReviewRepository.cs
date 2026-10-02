using Microsoft.Data.SqlClient;
using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Shared;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.SkillsEvidence;

public sealed class ContributionReviewRepository(IScopeProvider scopes) : IContributionReviewRepository
{
    public async Task<IReadOnlyList<SkillContributionReview>> GetPageAsync(Guid staffKey, Guid tenantId, int page)
    {
        if (page is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(page));
        using var scope = scopes.CreateScope(autoComplete: true);
        var query = new Sql($"""
            SELECT r.* FROM {SkillContributionReviewDto.TableName} r
            WHERE r.tenantId = @0 AND r.staffKey = @1
              AND NOT EXISTS (SELECT 1 FROM {SkillContributionReviewDto.TableName} newer
                  WHERE newer.tenantId = @0 AND newer.linkKey = r.linkKey AND newer.revision > r.revision)
            ORDER BY r.recordedAtUtc DESC, r.linkKey
            """, tenantId, staffKey).ForPage(new PageRequest(page, IContributionReviewRepository.PageSize));
        var rows = await scope.Database.FetchAsync<SkillContributionReviewDto>(query);
        return rows.Select(Map).ToArray();
    }

    public async Task<SkillContributionReview?> GetCurrentAsync(Guid linkKey, Guid tenantId)
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        var row = await scope.Database.FirstOrDefaultAsync<SkillContributionReviewDto>(
            $"SELECT TOP 1 * FROM {SkillContributionReviewDto.TableName} WHERE tenantId = @0 AND linkKey = @1 ORDER BY revision DESC", tenantId, linkKey);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<SkillContributionReview>> GetHistoryAsync(Guid linkKey, Guid tenantId)
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        var rows = await scope.Database.FetchAsync<SkillContributionReviewDto>(
            $"SELECT TOP 51 * FROM {SkillContributionReviewDto.TableName} WHERE tenantId = @0 AND linkKey = @1 ORDER BY revision DESC", tenantId, linkKey);
        return rows.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<SkillContributionSource>> GetSourcesAsync(Guid staffKey, Guid tenantId, IReadOnlyCollection<Guid>? keys = null)
    {
        if (keys is { Count: 0 }) return [];
        if (keys is { Count: > 100 }) throw new ArgumentOutOfRangeException(nameof(keys));
        return await ReadSourcesAsync(staffKey, tenantId, keys, PageRequest.First(100));
    }

    public async Task<ResultPage<SkillContributionSource>> GetSourcePageAsync(Guid staffKey, Guid tenantId, int page)
    {
        if (page is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(page));
        var request = new PageRequest(page, 100);
        return ResultPage<SkillContributionSource>.From(await ReadSourcesAsync(staffKey, tenantId, null, request, true), request);
    }

    private async Task<IReadOnlyList<SkillContributionSource>> ReadSourcesAsync(Guid staffKey, Guid tenantId,
        IReadOnlyCollection<Guid>? keys, PageRequest page, bool includeNext = false)
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        var sql = new Sql($"""
            SELECT e.* FROM {EngineeringEvidenceDto.TableName} e
            JOIN {EvidenceConnectionDto.TableName} c WITH (HOLDLOCK) ON c.connectionKey = e.connectionKey AND c.tenantId = @0
            WHERE e.tenantId = @0 AND e.staffKey = @1 AND e.actorIsBot = 0 AND e.attributionStatus = 'Mapped'
              AND c.status = 'Active'
              AND EXISTS (SELECT 1 FROM OPENJSON(c.selectedRepositoriesJson) selected WHERE selected.[value] = e.repositoryKey)
            """, tenantId, staffKey);
        if (keys is not null) sql.Append("AND e.tenantId = @0 AND e.evidenceKey IN (@1)", tenantId, keys.ToArray());
        sql.Append("ORDER BY e.occurredAtUtc DESC, e.evidenceKey");
        if (includeNext) sql.ForPage(page);
        else sql.Append("OFFSET @0 ROWS FETCH NEXT @1 ROWS ONLY", page.Skip, page.Size);
        var rows = await scope.Database.FetchAsync<EngineeringEvidenceDto>(sql);
        return rows.Select(e => new SkillContributionSource(e.EvidenceKey, e.RepositoryKey, e.ExternalId,
            e.Role, e.Title, SafeUrl(e.SourceUrl), e.OccurredAtUtc,
            e.SourceType == nameof(EvidenceSourceType.Commit) && e.AuthorshipVerified != true)).ToArray();
    }

    public async Task AppendAsync(SkillContributionReview revision, int expectedRevision)
    {
        if (revision.Revision != expectedRevision + 1 || expectedRevision < 0)
            throw new ArgumentException("Invalid revision sequence.");
        using var scope = scopes.CreateScope();
        var db = scope.Database;
        // Serializes writers for this link, including the absent first row.
        var previous = await db.FirstOrDefaultAsync<SkillContributionReviewDto>(
            $"SELECT TOP 1 * FROM {SkillContributionReviewDto.TableName} WITH (UPDLOCK, HOLDLOCK) WHERE tenantId = @0 AND linkKey = @1 ORDER BY revision DESC",
            revision.TenantId, revision.LinkKey);
        if ((previous?.Revision ?? 0) != expectedRevision) throw new ContributionReviewConflictException();
        if (previous is not null && (previous.StaffKey != revision.StaffKey || previous.AssertionKey != revision.AssertionKey || previous.EvidenceKey != revision.EvidenceKey))
            throw new CrossTenantReferenceException("Contribution", revision.LinkKey);

        var assertion = await db.FirstOrDefaultAsync<StaffSkillAssertionDto>(
            $"SELECT * FROM {StaffSkillAssertionDto.TableName} WITH (HOLDLOCK) WHERE tenantId = @0 AND staffKey = @1 AND assertionKey = @2",
            revision.TenantId, revision.StaffKey, revision.AssertionKey);
        if (assertion is null) throw new CrossTenantReferenceException("Assertion", revision.AssertionKey);
        if (revision.Status != ContributionReviewStatus.Withdrawn)
        {
            if (assertion.SupersededAtUtc is not null || assertion.Status == nameof(AssertionStatus.Withdrawn))
                throw new SkillAssertionValidationException("The skill assertion changed. Link a new example to its current revision.");
            var source = await db.FirstOrDefaultAsync<EngineeringEvidenceDto>(
                $"SELECT * FROM {EngineeringEvidenceDto.TableName} WITH (HOLDLOCK) WHERE tenantId = @0 AND staffKey = @1 AND evidenceKey = @2 AND actorIsBot = 0 AND attributionStatus = 'Mapped'",
                revision.TenantId, revision.StaffKey, revision.EvidenceKey);
            if (source is null) throw new CrossTenantReferenceException("Evidence", revision.EvidenceKey);
            if ((await GetSourcesAsync(revision.StaffKey, revision.TenantId, [revision.EvidenceKey])).Count == 0)
                throw new SkillAssertionValidationException("The evidence source is no longer available for review.");
        }

        var people = await db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {StaffDto.TableName} WITH (HOLDLOCK) WHERE tenantId = @0 AND staffKey IN (@1) AND isActive = 1",
            revision.TenantId, new[] { revision.StaffKey, revision.RecordedByStaffKey }.Distinct().ToArray());
        if (people != (revision.StaffKey == revision.RecordedByStaffKey ? 1 : 2))
            throw new CrossTenantReferenceException("Staff", revision.RecordedByStaffKey);
        try
        {
            await db.InsertAsync(ToDto(revision));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw new SkillAssertionValidationException("This contribution is already linked to that assertion. Open the existing example to correct it.");
        }
        scope.Complete();
    }

    public async Task<IReadOnlyList<SkillContributionReview>> ExportAsync(Guid staffKey, Guid tenantId)
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        var rows = await scope.Database.FetchAsync<SkillContributionReviewDto>(
            Sql.Builder.Where("tenantId = @0 AND (staffKey = @1 OR recordedByStaffKey = @1)", tenantId, staffKey).OrderBy("recordedAtUtc, id"));
        return rows.Select(Map).ToArray();
    }

    public async Task EraseAsync(Guid staffKey, Guid tenantId)
    {
        using var scope = scopes.CreateScope();
        await scope.Database.ExecuteAsync($"DELETE FROM {SkillContributionReviewDto.TableName} WHERE tenantId = @0 AND staffKey = @1", tenantId, staffKey);
        // Reviewer erasure preserves the decision but removes their identity and free-text rationale.
        await scope.Database.ExecuteAsync($"UPDATE {SkillContributionReviewDto.TableName} SET recordedByStaffKey = @2, decisionNote = NULL WHERE tenantId = @0 AND recordedByStaffKey = @1",
            tenantId, staffKey, Guid.Empty);
        scope.Complete();
    }

    private static string? SafeUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) ? value : null;

    private static SkillContributionReview Map(SkillContributionReviewDto d) => new()
    {
        LinkKey = d.LinkKey, Revision = d.Revision, TenantId = d.TenantId, StaffKey = d.StaffKey,
        AssertionKey = d.AssertionKey, EvidenceKey = d.EvidenceKey, Status = Enum.Parse<ContributionReviewStatus>(d.Status),
        DemonstrationNote = d.DemonstrationNote, AiTool = d.AiTool, AiWorkflow = Enum.Parse<AiAssistanceWorkflow>(d.AiWorkflow),
        DecisionNote = d.DecisionNote, RecordedByStaffKey = d.RecordedByStaffKey, RecordedAtUtc = d.RecordedAtUtc
    };

    private static SkillContributionReviewDto ToDto(SkillContributionReview r) => new()
    {
        LinkKey = r.LinkKey, Revision = r.Revision, TenantId = r.TenantId, StaffKey = r.StaffKey,
        AssertionKey = r.AssertionKey, EvidenceKey = r.EvidenceKey, Status = r.Status.ToString(),
        DemonstrationNote = r.DemonstrationNote, AiTool = r.AiTool, AiWorkflow = r.AiWorkflow.ToString(),
        DecisionNote = r.DecisionNote, RecordedByStaffKey = r.RecordedByStaffKey, RecordedAtUtc = r.RecordedAtUtc
    };
}
