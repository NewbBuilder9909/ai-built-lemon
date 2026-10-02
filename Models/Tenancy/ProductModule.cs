namespace ProgrammePulse.Models.Tenancy;

/// <summary>
/// A benched module: built and tested, but switched off until a tenant Admin
/// turns it on in Settings → Modules. Off is the default for every tenant
/// and every role, Admin included, and the toolbox is the only way on: no
/// configuration key, plan or role switches a module on by itself.
///
/// The core is never a module: the Evidence Check and its recorded reviews,
/// the Programme Overview, the data pages (import, connections, identities,
/// customers) and settings. A module still needs its plan feature
/// (<see cref="RequiresFeature"/>) and each page still needs its capability;
/// the switch only decides whether a module is showing at all.
/// </summary>
public sealed record ProductModule(string Key, string Name, string Summary, string? RequiresFeature = null);

public static class ProductModules
{
    public const string Reporting = "reporting";
    public const string DeliveryLoad = "delivery-load";
    public const string EstimateCalibration = "estimate-calibration";
    public const string Contracts = "contracts";
    public const string Skills = "skills";
    public const string ServiceHealth = "service-health";
    public const string SelfService = "self-service";
    public const string ExecutiveReview = "executive-review";

    public static readonly IReadOnlyList<ProductModule> All =
    [
        new(Reporting, "Reporting hub",
            "Effort variance, team capacity, risks and issues, governance, trend snapshots, alerts and the Jira and Tempo report.",
            ProductFeature.ReportingHub),
        new(DeliveryLoad, "Delivery load",
            "How much concurrent work each person carries, plus the workload and booking tables on the Programme Overview."),
        new(EstimateCalibration, "Estimate calibration",
            "How estimates compare with recorded time once work is finished.",
            ProductFeature.ReportingHub),
        new(Contracts, "Contracts and assurance",
            "Contract terms, documents, invoices, margin, repository links and security assurance.",
            ProductFeature.ContractOps),
        new(Skills, "Skills and continuity",
            "Skills matrix, coverage plan and repository evidence."),
        new(ServiceHealth, "Service health",
            "Support-desk demand, recurrence and time to restore by component.",
            ProductFeature.SupportEvidence),
        new(SelfService, "Staff self-service",
            "My Work, My Profile, leave requests and leave approvals."),
        new(ExecutiveReview, "Executive review",
            "Multi-market executive review packs. Also needs the deployment's executive review setting.",
            ProductFeature.ReportingHub)
    ];

    public static ProductModule? Find(string? key) =>
        All.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.Ordinal));
}
