using System.Text.Json;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// Subject access and erasure for skills data.
///
/// The decision these pin down, stated in SkillsEvidenceDataParticipant:
/// an assertion is deleted outright rather than pseudonymised, because
/// detached from the person it means nothing — and the coverage aggregate
/// legitimately drops as a result. Keeping an anonymous ghost maintainer
/// would hide a real staffing gap, so the last two tests here assert that
/// the number *does* fall.
/// </summary>
public class SkillsEvidenceGdprTests
{
    private const string Csharp = "csharp";

    [Fact]
    public async Task The_export_includes_every_version_of_the_subjects_own_record()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var declared = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Lead, "led the migration",
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);
        await ctx.Service.ValidateAsync(
            declared.AssertionKey, ctx.Sarah.StaffKey, ProficiencyLevel.Working, "not yet, in my view",
            null, SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId);

        var rows = await ctx.Participant.ExportAsync(ctx.Alex.StaffKey);

        // Both versions — Article 15 covers what was held, not only what is
        // held now, and the superseded claim is the person's own words.
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("Skill assertions", r.Section));
        Assert.Contains(rows, r => r.Summary.Contains("led the migration", StringComparison.Ordinal));
        // Including the reviewer's rationale about them, which is their data too.
        Assert.Contains(rows, r => r.Summary.Contains("not yet, in my view", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_export_never_reaches_into_another_tenant()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantB);

        await ctx.ValidatedAsync(ctx.Alex, Csharp, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        var reviewerB = ctx.AddStaff("Gareth Ellis", 202, SkillsEvidenceTestContext.TenantB);
        await ctx.ValidatedAsync(ctx.Rhian, Csharp, ProficiencyLevel.Lead, reviewerB, SkillsEvidenceTestContext.TenantB);

        var alexRows = await ctx.Participant.ExportAsync(ctx.Alex.StaffKey);
        var rhianRows = await ctx.Participant.ExportAsync(ctx.Rhian.StaffKey);

        Assert.Equal(2, alexRows.Count);
        Assert.Equal(2, rhianRows.Count);
        // Each export is only ever one person's, in one tenant — the
        // participant resolves the subject's own tenant and filters on it.
        Assert.DoesNotContain(ctx.Repository.Assertions,
            a => a.StaffKey == ctx.Alex.StaffKey && a.TenantId != SkillsEvidenceTestContext.TenantA);
    }

    [Fact]
    public async Task A_subject_with_no_resolvable_tenant_exports_nothing_rather_than_everything()
    {
        var ctx = new SkillsEvidenceTestContext();

        // Fail closed: an unknown staff key must not fall through to an
        // unscoped read.
        var rows = await ctx.Participant.ExportAsync(Guid.NewGuid());

        Assert.Empty(rows);
    }

    [Fact]
    public async Task Erasure_removes_every_assertion_including_superseded_history()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var declared = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Lead, "sensitive note about my health",
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);
        await ctx.Service.ValidateAsync(
            declared.AssertionKey, ctx.Sarah.StaffKey, ProficiencyLevel.Working, "reviewer's opinion",
            null, SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId);

        Assert.Equal(2, ctx.Repository.Assertions.Count(a => a.StaffKey == ctx.Alex.StaffKey));

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        Assert.DoesNotContain(ctx.Repository.Assertions, a => a.StaffKey == ctx.Alex.StaffKey);
        Assert.Empty(await ctx.Participant.ExportAsync(ctx.Alex.StaffKey));
    }

    [Fact]
    public async Task Erasure_leaves_other_peoples_records_alone()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        await ctx.ValidatedAsync(ctx.Alex, Csharp, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);
        await ctx.ValidatedAsync(ctx.Nia, Csharp, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        Assert.Equal(2, ctx.Repository.Assertions.Count(a => a.StaffKey == ctx.Nia.StaffKey));
    }

    [Fact]
    public async Task Erasure_is_audited_with_a_count_and_no_content()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);
        await ctx.ValidatedAsync(ctx.Alex, Csharp, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        var entry = ctx.AuditLog.Entries.Single(e => e.Action == SkillsEvidenceAuditAction.AssertionsErasedForSubject);
        Assert.Equal(ctx.Alex.StaffKey.ToString(), entry.EntityId);
        Assert.Equal(SkillsEvidenceTestContext.TenantA, entry.TenantId);

        var detail = JsonDocument.Parse(entry.DetailJson!).RootElement;
        Assert.Equal(2, detail.GetProperty("assertionRowsDeleted").GetInt32());
        // The proof is the count. Nothing it just deleted is written back.
        Assert.DoesNotContain(Csharp, entry.DetailJson!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The consequence of deleting rather than pseudonymising, asserted
    /// rather than left as a claim in a comment: after erasure the
    /// organisation genuinely has less cover, and the page says so.
    /// </summary>
    [Fact]
    public async Task After_erasure_the_coverage_aggregate_honestly_drops()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);
        await ctx.ValidatedAsync(ctx.Alex, Csharp, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);
        await ctx.ValidatedAsync(ctx.Nia, Csharp, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        var before = await ctx.Coverage.BuildAsync(SkillsEvidenceTestContext.TenantA);
        Assert.Equal(2, before.Rows[0].ValidatedCover);
        Assert.False(before.Rows[0].IsSingleMaintainerRisk);

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        var after = await ctx.Coverage.BuildAsync(SkillsEvidenceTestContext.TenantA);
        Assert.Equal(1, after.Rows[0].ValidatedCover);
        // And the single-maintainer warning now fires, which is the point:
        // an anonymised ghost holder would have hidden a real exposure.
        Assert.True(after.Rows[0].IsSingleMaintainerRisk);
    }

    /// <summary>
    /// GdprService takes every registered IStaffDataParticipant, so the
    /// wiring is what makes skills data appear in a subject access request
    /// at all. This runs the real GdprService with the real participant.
    /// </summary>
    [Fact]
    public async Task Skills_reach_the_real_subject_access_export_through_the_participant_seam()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);
        await ctx.ValidatedAsync(ctx.Alex, Csharp, ProficiencyLevel.Practitioner, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        var gdpr = new GdprService(
            ctx.StaffRepository,
            new NoAvailability(),
            new NoLeave(),
            new NoRates(),
            new NoWorkHours(),
            new Staff.FakeStaffAuditLogRepository(),
            [ctx.Participant],
            ctx.Time);

        var export = await gdpr.BuildExportAsync(ctx.Alex.StaffKey, SkillsEvidenceTestContext.TenantA);

        Assert.NotNull(export);
        Assert.Equal(2, export.LinkedRecords.Count);
        Assert.All(export.LinkedRecords, r => Assert.Equal("Skill assertions", r.Section));
    }

    [Fact]
    public async Task Erasure_through_the_real_gdpr_service_clears_skills_before_the_profile_is_pseudonymised()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);
        await ctx.ValidatedAsync(ctx.Alex, Csharp, ProficiencyLevel.Practitioner, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        var gdpr = new GdprService(
            ctx.StaffRepository,
            new NoAvailability(),
            new NoLeave(),
            new NoRates(),
            new NoWorkHours(),
            new Staff.FakeStaffAuditLogRepository(),
            [ctx.Participant],
            ctx.Time);

        await gdpr.EraseAsync(ctx.Alex.StaffKey);

        Assert.DoesNotContain(ctx.Repository.Assertions, a => a.StaffKey == ctx.Alex.StaffKey);
        // The participant resolves the subject's tenant from the staff row,
        // so it has to run before that row is pseudonymised. GdprService
        // does participants first; the audit entry proves it still found
        // the tenant.
        Assert.Contains(ctx.AuditLog.Entries, e => e.Action == SkillsEvidenceAuditAction.AssertionsErasedForSubject);
    }

    // Minimal stand-ins for the Staff-domain repositories GdprService also
    // takes; this file is about the skills half of the export.
    private sealed class NoAvailability : IAvailabilityRepository
    {
        public Task<IReadOnlyList<Availability>> GetForStaffAsync(Guid staffKey, DateOnly from, DateOnly to) =>
            Task.FromResult<IReadOnlyList<Availability>>([]);

        public Task<Availability> CreateAsync(Availability availability) => throw new NotSupportedException();
    }

    private sealed class NoLeave : ILeaveRequestRepository
    {
        public Task<LeaveRequest?> GetByRequestKeyAsync(Guid requestKey, Guid tenantId) => Task.FromResult<LeaveRequest?>(null);
        public Task<IReadOnlyList<LeaveRequest>> GetForStaffAsync(Guid staffKey, Guid tenantId) => Task.FromResult<IReadOnlyList<LeaveRequest>>([]);
        public Task<IReadOnlyList<LeaveRequest>> GetPendingAsync(Guid tenantId) => Task.FromResult<IReadOnlyList<LeaveRequest>>([]);
        public Task<LeaveRequest> CreateAsync(LeaveRequest request, Guid tenantId) => throw new NotSupportedException();
        public Task UpdateAsync(LeaveRequest request, Guid tenantId) => throw new NotSupportedException();
    }

    private sealed class NoRates : IStaffRateRepository
    {
        public Task<StaffRate?> GetCurrentAsync(Guid staffKey) => Task.FromResult<StaffRate?>(null);
        public Task<IReadOnlyList<StaffRate>> GetHistoryAsync(Guid staffKey) => Task.FromResult<IReadOnlyList<StaffRate>>([]);
        public Task<StaffRate> SetCurrentRateAsync(Guid staffKey, decimal costPerHour, string rateCurrency, Guid changedByStaffKey) => throw new NotSupportedException();
    }

    private sealed class NoWorkHours : IWorkHoursHistoryRepository
    {
        public Task<WorkHoursHistory?> GetCurrentAsync(Guid staffKey) => Task.FromResult<WorkHoursHistory?>(null);
        public Task<IReadOnlyList<WorkHoursHistory>> GetHistoryAsync(Guid staffKey) => Task.FromResult<IReadOnlyList<WorkHoursHistory>>([]);
        public Task<WorkHoursHistory> SetCurrentHoursAsync(Guid staffKey, decimal hoursPerWeek, Guid changedByStaffKey) => throw new NotSupportedException();
    }
}
