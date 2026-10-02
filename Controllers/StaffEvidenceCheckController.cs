using System.Text;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Reporting;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Evidence Check: can this week's delivery report be trusted, and what must
/// be fixed or owned before it's presented? The automated exception register
/// for a paid diagnostic, and the recurring check a subscribed customer runs
/// before each review. Tenant-scoped and open to delivery reporting roles,
/// so it carries no cost data (see IEvidenceCheckService).
///
/// Recorded reviews turn the check into a weekly loop: recording freezes the
/// check (CaptureTrend, the same grant as capturing a reporting trend
/// point), each finding then takes an owner and a decision
/// (ManageExecutiveDecisions), and the next check shows whether those
/// decisions held. Reading reviews needs only ViewDeliveryReporting.
/// </summary>
[Route("staffops/programme/evidence-check")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequireFeature(ProductFeature.ReportingHub)]
[RequireCapability(Capability.ViewDeliveryReporting)]
public sealed class StaffEvidenceCheckController(
    IStaffAuthorizationService staffAuthorizationService,
    IEvidenceCheckService evidenceCheck,
    IEvidenceReviewService evidenceReviews,
    ICurrentStaff currentStaff,
    ITenantContext tenantContext) : Controller
{
    private const string ReviewView = "~/Views/StaffOps/Programme/EvidenceReview.cshtml";

    /// <summary>
    /// The live check for one scope: programme and/or customer, and a
    /// reporting period (the last <see cref="EvidenceScope.DefaultPeriodDays"/>
    /// days unless given). A scope that doesn't resolve shows the default
    /// scope with the reason, rather than an empty page.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, Guid? programmeKey = null, Guid? customerKey = null,
        DateOnly? from = null, DateOnly? to = null, DateOnly? extractedOn = null, string? notice = null)
    {
        var resolution = await evidenceCheck.ResolveScopeAsync(tenantId, new EvidenceScopeRequest(programmeKey, customerKey, from, to, extractedOn));
        return await LivePageAsync(tenantId, resolution, notice);
    }

    /// <summary>The full exception register for one scope, one row per source record, for the evidence pack.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([CurrentTenant] Guid tenantId, Guid? programmeKey = null, Guid? customerKey = null,
        DateOnly? from = null, DateOnly? to = null, DateOnly? extractedOn = null, CancellationToken cancellationToken = default)
    {
        var resolution = await evidenceCheck.ResolveScopeAsync(tenantId, new EvidenceScopeRequest(programmeKey, customerKey, from, to, extractedOn), cancellationToken);
        if (resolution.Scope is not { } scope)
            return BadRequest(resolution.Error);
        var report = await evidenceCheck.BuildAsync(tenantId, scope, cancellationToken);
        var csv = new StringBuilder();
        csv.Append(CsvWriter.WriteRow("Evidence Check", $"as of {report.AsOfUtc:yyyy-MM-dd HH:mm} UTC", $"Readiness: {Label(report.Readiness)}", report.ReadinessReason));
        csv.Append(CsvWriter.WriteRow("Scope", ScopeSummary(scope)));
        csv.Append(CsvWriter.WriteRow());
        csv.Append(CsvWriter.WriteRow("Category", "Severity", "Finding", "Numerator", "Denominator", "Unit", "Project", "Source", "Source record id", "Record", "Detail", "Owner", "Decision"));
        foreach (var finding in report.Findings)
        {
            var records = finding.Records.Count > 0 ? finding.Records : [new EvidenceRecord("", null, null, "", "")];
            foreach (var record in records)
            {
                csv.Append(CsvWriter.WriteRow(
                    CategoryLabel(finding.Category), finding.Severity.ToString(), finding.Title,
                    finding.Numerator.ToString("0.##"), finding.Denominator.ToString("0.##"), finding.Unit,
                    record.Project, record.Source, record.ExternalId, record.Title, record.Detail,
                    "", ""));
            }
        }

        return File(CsvWriter.ToBytes(csv.ToString()), "text/csv", $"evidence-check-{report.AsOfUtc:yyyy-MM-dd}.csv");
    }

    /// <summary>Freeze the check for one scope as a review, carrying forward the last same-scope review's decisions.</summary>
    [HttpPost("record")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.CaptureTrend)]
    public async Task<IActionResult> Record([CurrentTenant] Guid tenantId, Guid? programmeKey, Guid? customerKey,
        DateOnly? from, DateOnly? to, DateOnly? extractedOn, string? decision)
    {
        var resolution = await evidenceCheck.ResolveScopeAsync(tenantId, new EvidenceScopeRequest(programmeKey, customerKey, from, to, extractedOn, decision));
        if (resolution.Scope is not { } scope)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return await LivePageAsync(tenantId, resolution, null);
        }

        var review = await evidenceReviews.RecordAsync(tenantId, scope, await ActorAsync());
        return review is null
            ? Redirect($"/staffops/programme/evidence-check?{Query(scope)}&notice=nothing-to-record")
            : Redirect($"/staffops/programme/evidence-check/reviews/{review.ReviewKey}");
    }

    /// <summary>The query string that reopens a scope, for links and the CSV export.</summary>
    public static string Query(EvidenceScope scope) =>
        string.Join('&', new[]
        {
            scope.ProgrammeKey is { } programme ? $"programmeKey={programme}" : null,
            scope.CustomerKey is { } customer ? $"customerKey={customer}" : null,
            $"from={scope.PeriodFrom:yyyy-MM-dd}",
            $"to={scope.PeriodTo:yyyy-MM-dd}",
            scope.ExtractedOn is { } extracted ? $"extractedOn={extracted:yyyy-MM-dd}" : null
        }.Where(part => part is not null));

    private async Task<IActionResult> LivePageAsync(Guid tenantId, EvidenceScopeResolution resolution, string? notice)
    {
        var scope = resolution.Scope;
        if (scope is null)
        {
            // Show the default scope, and say why the requested one was refused.
            var fallback = await evidenceCheck.ResolveScopeAsync(tenantId, new EvidenceScopeRequest());
            scope = fallback.Scope!;
        }

        var page = await evidenceReviews.BuildLiveAsync(tenantId, scope);
        ViewData["Title"] = "Review";
        return View("~/Views/StaffOps/Programme/EvidenceCheck.cshtml", page with
        {
            CanRecord = await staffAuthorizationService.HasAsync(Capability.CaptureTrend),
            NothingToRecord = notice == "nothing-to-record",
            ScopePicker = resolution.Picker,
            ScopeError = resolution.Error
        });
    }

    [HttpGet("reviews")]
    public async Task<IActionResult> Reviews([CurrentTenant] Guid tenantId)
    {
        ViewData["Title"] = "History";
        return View("~/Views/StaffOps/Programme/EvidenceReviews.cshtml", await evidenceReviews.GetHistoryAsync(tenantId));
    }

    [HttpGet("reviews/{reviewKey:guid}")]
    public async Task<IActionResult> Review([CurrentTenant] Guid tenantId, Guid reviewKey, string? saved = null)
    {
        var page = await evidenceReviews.GetReviewAsync(tenantId, reviewKey);
        if (page is null)
            return NotFound();

        return View(ReviewView, (await PrepareReviewViewAsync(page)) with
        {
            SavedFindingKey = page.Review.Findings.Any(f => f.FindingKey == saved) ? saved : null
        });
    }

    [HttpPost("reviews/{reviewKey:guid}/decide")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageExecutiveDecisions)]
    public async Task<IActionResult> Decide([CurrentTenant] Guid tenantId, Guid reviewKey, string findingKey, FindingDisposition disposition,
        Guid? ownerStaffKey, DateOnly? targetDate, string? note)
    {
        var result = await evidenceReviews.DecideAsync(tenantId, reviewKey, findingKey ?? "", disposition,
            ownerStaffKey, targetDate, note, await ActorAsync());
        if (result.Status == EvidenceDecisionStatus.Saved)
            return Redirect($"/staffops/programme/evidence-check/reviews/{reviewKey}?saved={Uri.EscapeDataString(findingKey!)}#finding-{Uri.EscapeDataString(findingKey!)}");
        if (result.Status == EvidenceDecisionStatus.NotFound)
            return NotFound();

        var page = await evidenceReviews.GetReviewAsync(tenantId, reviewKey);
        if (page is null)
            return NotFound();

        Response.StatusCode = result.Status == EvidenceDecisionStatus.NotLatest ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        return View(ReviewView, (await PrepareReviewViewAsync(page)) with
        {
            DecisionError = result.Error,
            DecisionFindingKey = findingKey
        });
    }

    /// <summary>The board-ready pack for one recorded review: printable, self-contained, reproducible.</summary>
    [HttpGet("reviews/{reviewKey:guid}/pack")]
    public async Task<IActionResult> Pack([CurrentTenant] Guid tenantId, Guid reviewKey)
    {
        var page = await evidenceReviews.GetReviewAsync(tenantId, reviewKey, tenantContext.CurrentTenant?.Name);
        if (page is null)
            return NotFound();

        return View("~/Views/StaffOps/Programme/EvidencePack.cshtml", page);
    }

    /// <summary>A recorded review's exception register, with the decisions filled in.</summary>
    [HttpGet("reviews/{reviewKey:guid}/export")]
    public async Task<IActionResult> ExportReview([CurrentTenant] Guid tenantId, Guid reviewKey)
    {
        var page = await evidenceReviews.GetReviewAsync(tenantId, reviewKey);
        if (page is null)
            return NotFound();

        var review = page.Review;
        var csv = new StringBuilder();
        csv.Append(CsvWriter.WriteRow("Evidence Check review", $"recorded {review.RecordedAtUtc:yyyy-MM-dd HH:mm} UTC", $"Readiness: {Label(review.Readiness)}", review.ReadinessReason));
        csv.Append(CsvWriter.WriteRow("Scope", ScopeSummary(review.Scope)));
        csv.Append(CsvWriter.WriteRow());
        csv.Append(CsvWriter.WriteRow("Category", "Severity", "Finding", "Numerator", "Denominator", "Unit", "Project", "Source", "Source record id", "Record", "Detail",
            "Owner", "Decision", "Target date", "Note", "Decision carried forward"));
        foreach (var finding in review.Findings)
        {
            var owner = finding.OwnerStaffKey is { } key ? page.StaffNames.GetValueOrDefault(key, "(former staff member)") : "";
            var records = finding.Records.Count > 0 ? finding.Records : [new EvidenceRecord("", null, null, "", "")];
            foreach (var record in records)
            {
                csv.Append(CsvWriter.WriteRow(
                    CategoryLabel(finding.Category), finding.Severity.ToString(), finding.Title,
                    finding.Numerator.ToString("0.##"), finding.Denominator.ToString("0.##"), finding.Unit,
                    record.Project, record.Source, record.ExternalId, record.Title, record.Detail,
                    owner, EvidenceReviewCalculator.Label(finding.Disposition), finding.TargetDate?.ToString("yyyy-MM-dd"),
                    finding.Note, finding.CarriedForward ? "yes" : "no"));
            }
        }

        return File(CsvWriter.ToBytes(csv.ToString()), "text/csv", $"evidence-review-{review.RecordedAtUtc:yyyy-MM-dd}.csv");
    }

    public static string Label(EvidenceReadiness readiness) => readiness switch
    {
        EvidenceReadiness.DecisionReady => "Decision-ready",
        EvidenceReadiness.UseWithCaveats => "Use with caveats",
        EvidenceReadiness.NotDecisionReady => "Not decision-ready",
        _ => "No data yet"
    };

    /// <summary>The CSS modifier for a readiness band (charts.css); empty when there is no data yet.</summary>
    public static string BandModifier(EvidenceReadiness readiness) => readiness switch
    {
        EvidenceReadiness.DecisionReady => "ready",
        EvidenceReadiness.UseWithCaveats => "caveats",
        EvidenceReadiness.NotDecisionReady => "not-ready",
        _ => ""
    };

    /// <summary>A share as a CSS percentage for a bar's width, clamped to 0–100.</summary>
    public static string SharePercent(decimal numerator, decimal denominator) =>
        (denominator <= 0 ? 0m : Math.Round(Math.Clamp(numerator * 100m / denominator, 0m, 100m), 1))
            .ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>One line saying what a check or review covered, for pages, packs and exports.</summary>
    public static string ScopeSummary(EvidenceScope? scope) => scope is null
        ? "Whole organisation, all recorded time (recorded before reviews had a declared scope)."
        : $"{scope.Label}; period {scope.PeriodFrom:d MMM yyyy} to {scope.PeriodTo:d MMM yyyy}; "
          + (scope.ExtractedOn is { } extracted ? $"data extracted {extracted:d MMM yyyy}." : "extract date not declared.");

    public static string CategoryLabel(EvidenceFindingCategory category) =>
        category == EvidenceFindingCategory.EvidenceGap ? "Evidence gap" : "Delivery exception";

    private async Task<EvidenceReviewPage> PrepareReviewViewAsync(EvidenceReviewPage page)
    {
        ViewData["Title"] = $"Review of {page.Review.RecordedAtUtc:d MMM yyyy}";
        return page with { CanDecide = page.IsLatest && await staffAuthorizationService.HasAsync(Capability.ManageExecutiveDecisions) };
    }

    private async Task<EvidenceReviewActor> ActorAsync() =>
        new((await currentStaff.GetProfileAsync())?.StaffKey, await currentStaff.GetMemberIdAsync());
}
