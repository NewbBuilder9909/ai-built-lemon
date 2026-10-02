using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.Staff;
using ProgrammePulse.Services.Audit;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Admin-only: staff list, staff detail with cost fields, staff onboarding,
/// rate changes, and the audit trail. Every action declares
/// [RequireCapability(ManageStaff)] or ViewAudit, which refuses before the
/// body runs, so the aggregation here never fetches StaffRate rows unless
/// the check has passed. This is the highest-risk surface in Phase 1; see
/// StaffAdminDetailViewModel for why cost data is only ever assembled here.
///
/// Tenant boundary: every action takes a [CurrentTenant] tenantId, so an
/// unresolved tenant is refused (403) after the capability check and before
/// the body runs, exactly as StaffBrandingController does. IStaffAdminService
/// proves any target StaffKey belongs to the caller's tenant (NotFound
/// otherwise) — so an Admin in one organisation can neither list nor act on
/// another organisation's people, even with a guessed key. The audit trail
/// is tenant-scoped too (IAuditTrailQueryService). See docs/tenancy.md.
/// </summary>
[Route("staffops/admin")]
public sealed class StaffAdminController(
    ICurrentStaff currentStaff,
    IStaffAdminService staffAdmin,
    IAuditTrailQueryService auditTrail) : Controller
{
    private const int AuditPageSize = 100;

    [HttpGet("")]
    [RequireCapability(Capability.ManageStaff)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? message = null)
    {
        var rows = await staffAdmin.BuildRosterAsync(tenantId);

        ViewData["Title"] = "People";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Admin/Index.cshtml", rows);
    }

    [HttpGet("create")]
    [RequireCapability(Capability.ManageStaff)]
    public IActionResult Create([CurrentTenant] Guid tenantId, string? message = null)
    {
        // tenantId is unused here; the parameter is the guard (no resolved
        // tenant: 403 before the form renders). The POST passes it on.
        ViewData["Title"] = "Add a person";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Admin/Create.cshtml");
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageStaff)]
    public async Task<IActionResult> Create(
        [CurrentTenant] Guid tenantId, string fullName, string email, string password, string role,
        string? jobTitle, string? department, string? team, decimal defaultWorkHoursPerWeek = 37.5m)
    {
        var result = await staffAdmin.OnboardAsync(tenantId,
            new StaffOnboardingRequest(fullName, email, password, role, jobTitle, department, team, defaultWorkHoursPerWeek),
            await currentStaff.GetMemberIdAsync());

        return result.Succeeded
            ? Redirect($"/staffops/admin/{result.StaffKey}")
            : Redirect("/staffops/admin/create?message=" + Uri.EscapeDataString(string.Join(" ", result.Errors)));
    }

    [HttpGet("{staffKey:guid}")]
    [RequireCapability(Capability.ManageStaff)]
    public async Task<IActionResult> Detail([CurrentTenant] Guid tenantId, Guid staffKey, string? message = null)
    {
        var model = await staffAdmin.BuildDetailAsync(tenantId, staffKey, await currentStaff.GetMemberIdAsync());
        if (model is null)
        {
            return NotFound();
        }

        ViewData["Title"] = model.FullName;
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Admin/Detail.cshtml", model);
    }

    [HttpPost("{staffKey:guid}/work-hours")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageStaff)]
    public async Task<IActionResult> SetWorkHours([CurrentTenant] Guid tenantId, Guid staffKey, decimal defaultWorkHoursPerWeek) =>
        ToResponse(staffKey, await staffAdmin.SetWorkHoursAsync(tenantId, staffKey, defaultWorkHoursPerWeek,
            await currentStaff.GetProfileAsync(), await currentStaff.GetMemberIdAsync()));

    [HttpPost("{staffKey:guid}/rate")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageStaff)]
    public async Task<IActionResult> SetRate([CurrentTenant] Guid tenantId, Guid staffKey, decimal costPerHour, string rateCurrency) =>
        ToResponse(staffKey, await staffAdmin.SetRateAsync(tenantId, staffKey, costPerHour, rateCurrency,
            await currentStaff.GetProfileAsync(), await currentStaff.GetMemberIdAsync()));

    /// <summary>
    /// Admin-to-admin MFA reset: deletes the target member's TOTP enrollment
    /// and recovery codes entirely, so their next sign-in is treated as a
    /// fresh enrollment (a new QR code and a new set of recovery codes) —
    /// the break-glass path for an Admin who lost their authenticator app
    /// and has already used up (or never saved) their recovery codes. See
    /// IMfaRepository.ResetAsync and docs on MfaRecoveryCodeGenerator.
    /// </summary>
    [HttpPost("{staffKey:guid}/mfa/reset")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageStaff)]
    public async Task<IActionResult> ResetMfa([CurrentTenant] Guid tenantId, Guid staffKey)
    {
        var result = await staffAdmin.ResetMfaAsync(tenantId, staffKey, await currentStaff.GetMemberIdAsync());
        return result.Status == StaffAdminStatus.NotFound
            ? NotFound()
            : Redirect($"/staffops/admin/{staffKey}?message=" + Uri.EscapeDataString("MFA enrollment reset. They'll be asked to set up a new authenticator (and get new recovery codes) on their next sign-in."));
    }

    /// <summary>
    /// GDPR data-subject export: every field this application holds against
    /// the given StaffKey (profile, availability, leave requests, rate
    /// history, audit trail), as a downloadable JSON file — see docs/gdpr.md.
    /// </summary>
    [HttpGet("{staffKey:guid}/export")]
    [RequireCapability(Capability.ManageStaff)]
    public async Task<IActionResult> Export([CurrentTenant] Guid tenantId, Guid staffKey)
    {
        var export = await staffAdmin.ExportAsync(tenantId, staffKey, await currentStaff.GetMemberIdAsync());
        if (export is null)
        {
            return NotFound();
        }

        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"staff-data-export-{staffKey:N}.json");
    }

    /// <summary>
    /// GDPR erasure, scoped to the PII IStaffRepository owns — see
    /// IStaffRepository.AnonymizeAsync for exactly what this does and does
    /// not touch.
    /// </summary>
    [HttpPost("{staffKey:guid}/erase")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageStaff)]
    public async Task<IActionResult> Erase([CurrentTenant] Guid tenantId, Guid staffKey)
    {
        var result = await staffAdmin.EraseAsync(tenantId, staffKey, await currentStaff.GetMemberIdAsync());
        return result.Status == StaffAdminStatus.NotFound ? NotFound() : Redirect("/staffops/admin");
    }

    /// <summary>Replaces the person's roles (at least one, from the tenant roles).</summary>
    [HttpPost("{staffKey:guid}/roles")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageStaff)]
    public async Task<IActionResult> SetRoles([CurrentTenant] Guid tenantId, Guid staffKey, string[]? roles) =>
        ToResponse(staffKey, await staffAdmin.SetRolesAsync(tenantId, staffKey, roles ?? [], await currentStaff.GetMemberIdAsync()),
            "Roles saved. They apply from the person's next page.");

    /// <summary>Suspends (active=false) or restores sign-in and every page, effective on the person's next request.</summary>
    [HttpPost("{staffKey:guid}/access")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageStaff)]
    public async Task<IActionResult> SetAccess([CurrentTenant] Guid tenantId, Guid staffKey, bool active) =>
        ToResponse(staffKey, await staffAdmin.SetAccessAsync(tenantId, staffKey, active, await currentStaff.GetMemberIdAsync()),
            active ? "Access restored." : "Access suspended. They are signed out on their next page.");

    [HttpPost("{staffKey:guid}/unlock")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageStaff)]
    public async Task<IActionResult> Unlock([CurrentTenant] Guid tenantId, Guid staffKey) =>
        ToResponse(staffKey, await staffAdmin.UnlockAsync(tenantId, staffKey, await currentStaff.GetMemberIdAsync()),
            "Unlocked. They can sign in again.");

    /// <summary>
    /// Issues a one-time password reset link and shows it to the Admin, once,
    /// to pass on. Rendered from the POST rather than redirected, so the token
    /// never sits in a URL the Admin's browser keeps. Nobody sees a password.
    /// </summary>
    [HttpPost("{staffKey:guid}/password-reset")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageStaff)]
    public async Task<IActionResult> PasswordReset([CurrentTenant] Guid tenantId, Guid staffKey)
    {
        var issue = await staffAdmin.IssuePasswordResetAsync(tenantId, staffKey, await currentStaff.GetMemberIdAsync());
        if (issue is null)
        {
            return NotFound();
        }

        var link = $"{Request.Scheme}://{Request.Host}{StaffAccountController.ResetPath}?m={issue.MemberId}&t={Uri.EscapeDataString(issue.Token)}";
        ViewData["Title"] = "Password reset link";
        return View("~/Views/StaffOps/Admin/ResetLink.cshtml", new PasswordResetLinkViewModel(staffKey, issue.FullName, link));
    }

    /// <summary>Deletes the person: erases their personal data and deletes their login. History stays, unnamed.</summary>
    [HttpPost("{staffKey:guid}/delete")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageStaff)]
    public async Task<IActionResult> Delete([CurrentTenant] Guid tenantId, Guid staffKey)
    {
        var result = await staffAdmin.DeleteAsync(tenantId, staffKey, await currentStaff.GetMemberIdAsync());
        return result.Status switch
        {
            StaffAdminStatus.NotFound => NotFound(),
            StaffAdminStatus.Invalid => Redirect($"/staffops/admin/{staffKey}?message=" + Uri.EscapeDataString(result.Message!)),
            _ => Redirect("/staffops/admin?message=" + Uri.EscapeDataString("Deleted. Their login is gone and their name is removed from history."))
        };
    }

    /// <summary>
    /// Read-only view over the tenant's audit trails (staff/rate/MFA/GDPR
    /// and platform actions on this tenant; sync runs and reporting actions;
    /// skills and evidence; service health), merged newest-first. Actor ids
    /// are resolved to names only for members of the caller's own tenant.
    /// See docs/data-governance.md.
    /// </summary>
    [HttpGet("audit")]
    [RequireCapability(Capability.ViewAudit)]
    public async Task<IActionResult> Audit([CurrentTenant] Guid tenantId, int page = 1)
    {
        var rows = await auditTrail.GetPageAsync(tenantId, new PageRequest(page, AuditPageSize));

        ViewData["Title"] = "Audit trail";
        return View("~/Views/StaffOps/Admin/Audit.cshtml", rows);
    }

    private IActionResult ToResponse(Guid staffKey, StaffAdminResult result, string? doneMessage = null) => result.Status switch
    {
        StaffAdminStatus.NotFound => NotFound(),
        StaffAdminStatus.NoActingProfile => NotFound("No staff profile is linked to this account."),
        StaffAdminStatus.Invalid => Redirect($"/staffops/admin/{staffKey}?message=" + Uri.EscapeDataString(result.Message!)),
        _ when doneMessage is not null => Redirect($"/staffops/admin/{staffKey}?message=" + Uri.EscapeDataString(doneMessage)),
        _ => Redirect($"/staffops/admin/{staffKey}")
    };
}
