using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.BrandingOps;

public sealed class BrandingOpsMigrationPlan : MigrationPlan
{
    public BrandingOpsMigrationPlan() : base("BrandingOps")
    {
        From(string.Empty)
            .To<AddBrandingOpsTables>("2026-08-branding-01")
            .To<AddBrandingTenantColumns>("2026-08-branding-02");
    }
}
