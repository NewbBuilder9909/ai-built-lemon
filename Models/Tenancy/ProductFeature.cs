namespace ProgrammePulse.Models.Tenancy;

/// <summary>
/// Sellable feature keys a plan can include. Referenced by
/// [RequireFeature(...)] on controllers/actions and by the nav in
/// Views/StaffOps/_Layout.cshtml. Add a key here first, then map it to plans
/// in Services/Tenancy/PlanEntitlements — a key that isn't in any plan is
/// simply switched off for every tenant.
/// </summary>
public static class ProductFeature
{
    public const string ClickUpSync = "ClickUpSync";
    public const string HubPlannerSync = "HubPlannerSync";
    public const string JiraSync = "JiraSync";
    public const string TempoSync = "TempoSync";
    public const string ReportingHub = "ReportingHub";
    public const string ContractOps = "ContractOps";
    public const string Branding = "Branding";

    /// <summary>
    /// Repository evidence for the skills matrix. Enterprise-only by
    /// default: it collects person-level data from a customer's source
    /// control, which needs the worker-notice and DPIA decisions recorded
    /// in docs/data-governance.md before anyone switches it on. The
    /// manually reviewed skills matrix itself is not gated — only this.
    /// </summary>
    public const string GitHubEvidence = "GitHubEvidence";

    /// <summary>
    /// Support-desk service health. Gated separately from
    /// <see cref="GitHubEvidence"/> on purpose: the design document
    /// promises a customer can connect a desk without connecting a
    /// repository, and two flags are what make that a commercial reality
    /// rather than an architectural claim. Also a lighter privacy
    /// footprint — case metadata is about a product, not about employees
    /// — so this one sits from Professional upwards.
    /// </summary>
    public const string SupportEvidence = "SupportEvidence";

    /// <summary>
    /// Security findings and gate state from a scanning tool (Aikido first),
    /// feeding Contract Ops' assurance page. Enterprise by default, like
    /// ContractOps, which it serves; gated separately so a tenant can have
    /// contracts without connecting a scanner.
    /// </summary>
    public const string SecurityAssurance = "SecurityAssurance";

    /// <summary>
    /// Upload a sanitised delivery export (work items, time) instead of
    /// connecting a live source. On every plan: it is the entry route for a
    /// paid diagnostic, so gating it would gate the first sale.
    /// </summary>
    public const string FileImport = "FileImport";

    public static readonly IReadOnlyList<string> All = [FileImport, ClickUpSync, HubPlannerSync, JiraSync, TempoSync, ReportingHub, ContractOps, Branding, GitHubEvidence, SupportEvidence, SecurityAssurance];
}
