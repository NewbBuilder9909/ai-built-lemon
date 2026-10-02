namespace ProgrammePulse.Models.Staff;

/// <summary>
/// What a signed-in member is allowed to *do*, independent of which
/// <see cref="StaffRole"/> group they hold. This is the one place capability
/// names are written, exactly as <see cref="StaffRole"/> is the one place
/// group names are written.
///
/// Why this layer exists: the seven predicates on IStaffAuthorizationService
/// are fixed unions of groups (IsTeamLeadOrAboveAsync and friends), so
/// "reporting" and "editing reporting" cannot be separated, a leave approver
/// inherits delivery write access, and a read-only Board role cannot be
/// expressed at all. Naming the action rather than the seniority fixes that,
/// and is the seam a customer-configurable role model needs later — see
/// docs/persona-harness-build-gate.md section 2.3.
///
/// Read and write are deliberately distinct capabilities throughout. That is
/// the whole point of the split; collapsing any pair back into one would
/// reintroduce the defect this was built to remove.
/// </summary>
public static class Capability
{
    // --- Self-service: every tenant role holds these. ---
    public const string ViewOwnProfile = "ViewOwnProfile";
    public const string ViewOwnWork = "ViewOwnWork";
    public const string SubmitOwnLeave = "SubmitOwnLeave";
    public const string ViewOwnSkills = "ViewOwnSkills";
    public const string DeclareOwnSkills = "DeclareOwnSkills";

    // --- Delivery, read. ---
    public const string ViewPortfolio = "ViewPortfolio";
    public const string ViewDeliveryReporting = "ViewDeliveryReporting";
    public const string ViewProjectRisk = "ViewProjectRisk";
    public const string ViewTeamCapacity = "ViewTeamCapacity";

    // --- Delivery, write. ---
    public const string ManageProjectRisk = "ManageProjectRisk";
    public const string LockBaseline = "LockBaseline";
    public const string DecideChangeRequest = "DecideChangeRequest";
    public const string CaptureTrend = "CaptureTrend";
    public const string ManageStakeholders = "ManageStakeholders";
    public const string TriggerSync = "TriggerSync";
    public const string ManageExecutiveDecisions = "ManageExecutiveDecisions";
    public const string ReviewExecutiveEvidence = "ReviewExecutiveEvidence";

    // --- Commercial. Cost and rate data is structurally separated elsewhere
    // (see CLAUDE.md); this is the access decision that matches it. ---
    public const string ViewCommercials = "ViewCommercials";
    public const string ManageContracts = "ManageContracts";

    // --- Estimate calibration. Aggregates and per-estimator history are
    // separate capabilities because the second identifies individuals. ---
    public const string ViewEstimateCalibration = "ViewEstimateCalibration";
    public const string ViewEstimatorHistory = "ViewEstimatorHistory";
    public const string ManageEstimateCalibration = "ManageEstimateCalibration";

    // --- Delivery load. Split for the same reason estimate calibration is:
    // the team distribution is a planning fact, while a named finding
    // identifies an individual. ViewPersonLoad is the narrower grant and is
    // never implied by ViewTeamCapacity. ---
    public const string ViewTeamLoad = "ViewTeamLoad";
    public const string ViewPersonLoad = "ViewPersonLoad";

    // --- Skills and evidence. Three separate grants because they answer
    // three different questions about one dataset: "how exposed is this
    // organisation" (an aggregate, no person in the answer), "what is on
    // this individual's record" (person-level, and the narrower grant the
    // module's privacy section asks for), and "may I change it". Splitting
    // them is what lets a delivery manager plan cover later without
    // handing them everyone's evidence. ---
    public const string ViewTeamSkillCoverage = "ViewTeamSkillCoverage";
    public const string ViewStaffSkillEvidence = "ViewStaffSkillEvidence";
    public const string ReviewStaffSkills = "ReviewStaffSkills";

    // --- Service health. Deliberately a *wider* grant than the skills
    // capabilities above, and the difference is the point: support demand
    // belongs first to a product and its owning team, so the component
    // view is about the service, not about employees. Person-level
    // support participation stays behind ViewStaffSkillEvidence.
    //
    // Confirming that a change caused a customer-affecting case is the
    // strongest claim this product makes, so it is its own capability
    // rather than something ViewServiceHealth implies. ---
    public const string ViewServiceHealth = "ViewServiceHealth";
    public const string ReviewRootCause = "ReviewRootCause";

    /// <summary>
    /// Declare who owns a component, approve cover, and raise or close
    /// the actions that follow. A management decision, so it sits with
    /// the delivery manager rather than with tenant administration.
    /// Reading the plan uses <see cref="ViewStaffSkillEvidence"/> — it
    /// names people, unlike the aggregate coverage view.
    /// </summary>
    public const string ManageContinuityPlan = "ManageContinuityPlan";

    /// <summary>
    /// Record the tenant's lawful basis, worker notice and DPIA decision
    /// for person-level evidence. Separate from
    /// <see cref="ManageContinuityPlan"/> because it is a legal
    /// attestation with a named signatory, not a delivery action — and
    /// it gates whether evidence may be collected at all.
    /// </summary>
    public const string RecordProcessingDecision = "RecordProcessingDecision";

    // --- Approvals. ---
    public const string ApproveLeave = "ApproveLeave";

    // --- Tenant administration. ---
    public const string ManageStaff = "ManageStaff";
    public const string ViewAudit = "ViewAudit";
    public const string ManageIntegrations = "ManageIntegrations";
    public const string ManageIdentityMappings = "ManageIdentityMappings";
    public const string ManageBranding = "ManageBranding";
    public const string ManageCustomers = "ManageCustomers";
    public const string ManageSkillTaxonomy = "ManageSkillTaxonomy";

    // Switching a benched module on or off for the whole organisation
    // (Settings → Modules). Admin only: it changes what every role sees.
    public const string ManageModules = "ManageModules";

    // --- Platform operation. Never implied by tenant administration. ---
    public const string ManagePlatform = "ManagePlatform";

    /// <summary>
    /// Every capability. A persona contract test asserts each one appears in
    /// at least one persona's allowed or forbidden list, so a capability
    /// cannot be added without someone deciding who holds it.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        ViewOwnProfile, ViewOwnWork, SubmitOwnLeave, ViewOwnSkills, DeclareOwnSkills,
        ViewPortfolio, ViewDeliveryReporting, ViewProjectRisk, ViewTeamCapacity,
        ManageProjectRisk, LockBaseline, DecideChangeRequest, CaptureTrend, ManageStakeholders, TriggerSync,
        ManageExecutiveDecisions, ReviewExecutiveEvidence,
        ViewCommercials, ManageContracts,
        ViewEstimateCalibration, ViewEstimatorHistory, ManageEstimateCalibration,
        ViewTeamLoad, ViewPersonLoad,
        ViewTeamSkillCoverage, ViewStaffSkillEvidence, ReviewStaffSkills,
        ViewServiceHealth, ReviewRootCause,
        ManageContinuityPlan, RecordProcessingDecision,
        ApproveLeave,
        ManageStaff, ViewAudit, ManageIntegrations, ManageIdentityMappings, ManageBranding, ManageCustomers,
        ManageSkillTaxonomy, ManageModules,
        ManagePlatform
    ];
}
