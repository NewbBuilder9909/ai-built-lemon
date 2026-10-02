using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ContractOps;

public sealed class ContractOpsMigrationPlan : MigrationPlan
{
    public ContractOpsMigrationPlan() : base("ContractOps")
    {
        From(string.Empty)
            .To<AddContractOpsTables>("2026-08-contractops-01")
            .To<AddNonLabourCostTable>("2026-08-contractops-02")
            .To<AddInvoiceTables>("2026-08-contractops-03")
            .To<AddContractOpsTenantColumns>("2026-09-contractops-04")
            .To<AddObligationTables>("2026-09-contractops-05")
            .To<AddInvoiceDraftStage>("2026-09-contractops-06");
    }
}
