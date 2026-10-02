namespace ProgrammePulse.Models.Staff;

/// <summary>
/// The role → capability matrix: which <see cref="Capability"/> each
/// <see cref="StaffRole"/> group grants. Pure data and pure lookups, with no
/// Umbraco types and nothing async, so the access model can be reviewed and
/// unit-tested on its own — the same instinct as
/// Startup/ProductionConfigurationGuard and Services/Tenancy/PlanEntitlements.
///
/// **Deny by default.** An unrecognised group grants nothing rather than
/// falling back to a default set; a typo in a group name must lose access,
/// never gain it. A member's effective capabilities are the union across the
/// groups they hold, so holding two roles adds, never subtracts.
///
/// This matrix encodes the role decisions of 19 September 2026 recorded in
/// docs/persona-harness-build-gate.md section 8. Three of them deliberately
/// differ from the legacy predicates on IStaffAuthorizationService, and
/// RoleCapabilitiesTests pins every one of those differences so that an
/// unintended divergence fails the build.
/// </summary>
public static class RoleCapabilities
{
    /// <summary>What any employee can do for themselves, whatever their role.</summary>
    private static readonly string[] SelfService =
    [
        Capability.ViewOwnProfile,
        Capability.ViewOwnWork,
        Capability.SubmitOwnLeave,
        // Everyone can see and correct their own skills record. Deliberate:
        // a skills matrix people cannot inspect or contest is the kind of
        // worker monitoring the ICO guidance in
        // docs/staff-skills-evidence-module.md warns about, and the
        // correction route is the mitigation.
        Capability.ViewOwnSkills,
        Capability.DeclareOwnSkills
    ];

    /// <summary>Delivery information, read-only. No state is changed by any of these.</summary>
    private static readonly string[] DeliveryRead =
    [
        Capability.ViewPortfolio,
        Capability.ViewDeliveryReporting,
        Capability.ViewProjectRisk,
        Capability.ViewTeamCapacity
    ];

    /// <summary>
    /// The delivery load distribution, without names. Kept out of
    /// <see cref="DeliveryRead"/> so the two halves of the load view can be
    /// granted apart: counts answer "are we overloading anyone" for a reader
    /// who has no one to reassign, which is every role below Team Lead.
    /// </summary>
    private static readonly string[] TeamLoadRead =
    [
        Capability.ViewTeamLoad
    ];

    /// <summary>
    /// Named load findings. Only roles who can actually relieve load hold
    /// this — the finding exists to be acted on, and a reader who cannot
    /// reassign work can only use a name to form a judgement about a person.
    /// </summary>
    private static readonly string[] PersonLoadRead =
    [
        Capability.ViewPersonLoad
    ];

    /// <summary>Delivery information, write. The half a read-only role must never receive.</summary>
    private static readonly string[] DeliveryWrite =
    [
        Capability.ManageProjectRisk,
        Capability.LockBaseline,
        Capability.DecideChangeRequest,
        Capability.CaptureTrend,
        Capability.ManageStakeholders,
        Capability.ManageExecutiveDecisions,
        Capability.ReviewExecutiveEvidence
    ];

    /// <summary>
    /// Other people's skills. Held by the delivery-manager role and by
    /// Admin, and by nobody else — decision of 20 September 2026.
    ///
    /// Not given to Analyst or Board, although both are otherwise
    /// read-only roles that see delivery reporting. Even the aggregate is
    /// derived from employees' personal records, and the module's privacy
    /// section is explicit that "ordinary users do not receive raw
    /// employee-level activity". Widening this later is a deliberate act
    /// with a named requester, not a default.
    /// </summary>
    private static readonly string[] SkillsReview =
    [
        Capability.ViewTeamSkillCoverage,
        Capability.ViewStaffSkillEvidence,
        Capability.ReviewStaffSkills,

        // Declaring who owns a component and what to do about a gap is
        // the same management job as reviewing the skills underneath it,
        // so it travels with them rather than needing its own role.
        Capability.ManageContinuityPlan
    ];

    /// <summary>
    /// Service health, read. Decision of 21 September 2026: this goes
    /// wider than the skills capabilities — Analyst and Board get it,
    /// where they get no skills access at all.
    ///
    /// The distinction is what the data is *about*. A skills coverage
    /// figure is derived from employees' personal records even in
    /// aggregate. A component's support demand is about a product and how
    /// well it works; an analyst reporting on service quality needs it,
    /// and withholding it would push them towards a role that also grants
    /// delivery write access — exactly the defect the capability model
    /// was built to remove.
    /// </summary>
    private static readonly string[] ServiceHealthRead = [Capability.ViewServiceHealth];

    private static readonly string[] TenantAdministration =
    [
        // Pulling data from an external source is an integration action, not
        // a delivery-management one: it spends a customer's API quota, can
        // overwrite imported records, and belongs with the credentials that
        // authorise it. It sat under delivery write until the persona
        // contracts caught that the application had always gated it on Admin.
        Capability.TriggerSync,
        Capability.ViewCommercials,
        Capability.ManageContracts,
        Capability.ViewEstimateCalibration,
        Capability.ViewEstimatorHistory,
        Capability.ManageEstimateCalibration,
        Capability.ApproveLeave,
        Capability.ManageStaff,
        Capability.ViewAudit,
        Capability.ManageIntegrations,
        Capability.ManageIdentityMappings,
        Capability.ManageBranding,
        Capability.ManageCustomers,
        Capability.ManageModules,
        // Who may add or retire a skill the whole tenant is then measured
        // against. Admin only: a taxonomy anyone can extend stops being
        // comparable, and the coverage view built on it stops meaning
        // anything.
        Capability.ManageSkillTaxonomy,

        // An Admin can confirm a root cause too. Team Lead holds it
        // explicitly as well, so this is a union rather than a
        // seniority ladder.
        Capability.ReviewRootCause,

        // Recording the lawful basis, worker notice and DPIA decision is
        // a legal attestation on behalf of the organisation, and it
        // gates whether person-level evidence may be collected at all.
        // Admin only — a delivery manager should not be able to unblock
        // collection of their own team's activity.
        Capability.RecordProcessingDecision
    ];

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Map =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            // An ordinary employee: their own work and their own leave.
            [StaffRole.Staff] = Set(SelfService),

            // Decision 1: a real group, so an Admin can assign it and a
            // customer's analyst is not forced into Team Lead. Read-only by
            // construction — DeliveryWrite is absent, not filtered out later.
            [StaffRole.Analyst] = Set(SelfService, DeliveryRead, TeamLoadRead, ServiceHealthRead),

            // Decision 2: the executive view is read-only. Adding
            // DeliveryWrite here would let a non-executive director lock a
            // baseline, which is what the brief means by "normally read-only".
            [StaffRole.Board] = Set(SelfService, DeliveryRead, TeamLoadRead, ServiceHealthRead, [Capability.ViewEstimateCalibration]),

            // Decision 3: approving leave no longer implies delivery write.
            // Reporting *read* is kept — an approver reasonably needs to see
            // capacity before approving — but RAID, baselines, change
            // requests, trend capture and stakeholders are gone.
            [StaffRole.HolidayApprover] = Set(SelfService, DeliveryRead, TeamLoadRead, [Capability.ApproveLeave]),

            // Decision 4: the delivery manager role. Gains ApproveLeave,
            // which the legacy IsHolidayApproverOrAdminAsync withheld, so a
            // project manager can approve their team's leave.
            // Decision 5 (20 September 2026): also the skills reviewer.
            // "Manager review" in the skills module means the delivery
            // manager — the person who already plans the team's cover — not
            // a second approval role nobody has been assigned to.
            // Also the root-cause reviewer: the delivery manager already
            // owns the incident follow-through, and confirming a cause is
            // an engineering judgement, not an administrative one.
            [StaffRole.TeamLead] = Set(SelfService, DeliveryRead, DeliveryWrite, TeamLoadRead, PersonLoadRead, SkillsReview, ServiceHealthRead,
                [Capability.ApproveLeave, Capability.ReviewRootCause]),

            // Everything tenant-scoped. Deliberately not ManagePlatform.
            [StaffRole.Admin] = Set(SelfService, DeliveryRead, DeliveryWrite, TeamLoadRead, PersonLoadRead, SkillsReview, ServiceHealthRead, TenantAdministration),

            // Platform operation only — no tenant business data, ever. The
            // determination in docs/persona-harness-build-gate.md section 3,
            // expressed as the absence of every other capability rather than
            // as a check somewhere downstream.
            [StaffRole.PlatformAdmin] = Set([Capability.ManagePlatform])
        };

    /// <summary>Capabilities granted by one group; empty for an unknown group.</summary>
    public static IReadOnlySet<string> For(string? role) =>
        role is not null && Map.TryGetValue(role, out var capabilities)
            ? capabilities
            : EmptySet;

    /// <summary>The union of capabilities across every group a member holds.</summary>
    public static IReadOnlySet<string> ForRoles(IEnumerable<string>? roles)
    {
        if (roles is null)
        {
            return EmptySet;
        }

        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            union.UnionWith(For(role));
        }

        return union;
    }

    public static bool Has(IEnumerable<string>? roles, string capability) =>
        ForRoles(roles).Contains(capability);

    /// <summary>Which groups grant a capability — used by the equivalence tests and useful for support.</summary>
    public static IReadOnlyList<string> RolesGranting(string capability) =>
        [.. Map.Where(entry => entry.Value.Contains(capability)).Select(entry => entry.Key).Order(StringComparer.Ordinal)];

    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>(StringComparer.Ordinal);

    private static IReadOnlySet<string> Set(params string[][] groups)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            set.UnionWith(group);
        }

        return set;
    }
}
