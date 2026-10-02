using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.ContractOps;
using ProgrammePulse.Services.ContractOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Contract obligations and repository control assurance (step 2 of
/// docs/delivery-evidence-and-contract-assurance.md). Same gates as the rest
/// of Contract Ops: plan-gated, ManageContracts, tenant-scoped. The rules
/// live in IContractAssuranceService; this reports and never enforces.
/// </summary>
[Route("staffops/contracts")]
[RequireFeature(ProductFeature.ContractOps)]
[RequireModule(ProductModules.Contracts)]
public sealed class StaffContractAssuranceController(
    ICurrentStaff currentStaff,
    IContractAssuranceService assurance) : Controller
{
    [HttpGet("assurance")]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> Portfolio([CurrentTenant] Guid tenantId)
    {
        var model = await assurance.BuildPortfolioAsync(tenantId);

        ViewData["Title"] = "Contract Assurance";
        return View("~/Views/StaffOps/Contracts/Assurance.cshtml", model);
    }

    [HttpGet("{contractKey:guid}/obligations")]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> Obligations([CurrentTenant] Guid tenantId, Guid contractKey, string? message = null)
    {
        var model = await assurance.BuildContractPageAsync(tenantId, contractKey, message);
        if (model is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"Obligations: {model.Contract.Reference}";
        return View("~/Views/StaffOps/Contracts/Obligations.cshtml", model);
    }

    [HttpPost("{contractKey:guid}/obligations")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> AddObligation([CurrentTenant] Guid tenantId, Guid contractKey, ObligationInput input)
    {
        var caller = await currentStaff.GetProfileAsync();
        var outcome = await assurance.AddObligationAsync(tenantId, contractKey, input, caller?.StaffKey, await currentStaff.GetMemberIdAsync());

        return BackTo(contractKey, outcome.Succeeded ? "Obligation recorded." : outcome.Error!);
    }

    [HttpPost("{contractKey:guid}/obligations/{obligationKey:guid}/withdraw")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> WithdrawObligation([CurrentTenant] Guid tenantId, Guid contractKey, Guid obligationKey)
    {
        var caller = await currentStaff.GetProfileAsync();
        var outcome = await assurance.WithdrawObligationAsync(tenantId, obligationKey, caller?.StaffKey, await currentStaff.GetMemberIdAsync());

        return BackTo(contractKey, outcome.Succeeded ? "Obligation withdrawn. It stays on record in the audit trail." : outcome.Error!);
    }

    [HttpPost("{contractKey:guid}/obligations/attest")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> Attest(
        [CurrentTenant] Guid tenantId, Guid contractKey, string? repository,
        RepositoryControl control, ControlState state, string? evidenceReference)
    {
        // The form posts "provider|account|repository" from one select, so it
        // works without script; the service validates each part.
        var parts = (repository ?? string.Empty).Split('|');
        var caller = await currentStaff.GetProfileAsync();
        var outcome = await assurance.AttestAsync(tenantId,
            parts.ElementAtOrDefault(0), parts.ElementAtOrDefault(1), parts.ElementAtOrDefault(2),
            control, state, evidenceReference, caller?.StaffKey, await currentStaff.GetMemberIdAsync());

        return BackTo(contractKey, outcome.Succeeded
            ? $"Attestation recorded; it lapses in {RepositoryControlAttestation.ValidityDays} days."
            : outcome.Error!);
    }

    private IActionResult BackTo(Guid contractKey, string message) =>
        RedirectToAction(nameof(Obligations), new { contractKey, message });
}
