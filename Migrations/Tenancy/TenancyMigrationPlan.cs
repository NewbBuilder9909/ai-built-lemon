using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.Tenancy;

public sealed class TenancyMigrationPlan : MigrationPlan
{
    public TenancyMigrationPlan() : base("Tenancy")
    {
        From(string.Empty)
            .To<AddTenancyTables>("2026-09-tenancy-01")
            .To<AddTenantLifecycleColumns>("2026-09-tenancy-02")
            .To<AddExecutiveReviewTables>("2026-09-tenancy-03")
            .To<AddExecutiveDecisionTable>("2026-09-tenancy-04")
            .To<AddExecutiveDataLifecycleTable>("2026-09-tenancy-05")
            .To<AddTenantFeatureSelectionTable>("2026-09-tenancy-06")
            .To<AddTenantModuleSwitchTable>("2026-10-tenancy-07");
    }
}
