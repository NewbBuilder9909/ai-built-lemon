using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

public sealed class ProgrammeOpsMigrationPlan : MigrationPlan
{
    public ProgrammeOpsMigrationPlan() : base("ProgrammeOps")
    {
        From(string.Empty)
            .To<AddProgrammeOpsTables>("2026-08-programmeops-01")
            .To<AddReportingTables>("2026-08-programmeops-02")
            .To<AddTimeEntryBillableFlag>("2026-08-programmeops-03")
            .To<AddPmoGovernanceTables>("2026-08-programmeops-04")
            .To<AddProgrammeBudgetColumns>("2026-08-programmeops-05")
            .To<AddReportingSnapshotTable>("2026-08-programmeops-06")
            .To<AddAlertTable>("2026-08-programmeops-07")
            .To<AddWorkItemAllocationTable>("2026-08-programmeops-08")
            .To<AddHubPlannerRawTable>("2026-09-programmeops-09")
            .To<AddPlannedAllocationTable>("2026-09-programmeops-10")
            .To<AddIdentityResolutionTables>("2026-09-programmeops-11")
            .To<AddSyncRunTables>("2026-09-programmeops-12")
            .To<AddIdentityLinkUniqueness>("2026-09-programmeops-13")
            .To<AddProgrammeTenantColumns>("2026-09-programmeops-14")
            .To<RekeySyncLeaseByTenant>("2026-09-programmeops-15")
            .To<AddSourceConnectionTable>("2026-09-programmeops-16")
            .To<AddSourceConnectionCredentials>("2026-09-programmeops-17")
            .To<AddEstimateBaselineTable>("2026-09-programmeops-18")
            .To<AddRawConnectorPayloadTable>("2026-09-programmeops-19")
            .To<AddTimeEntryWorkDate>("2026-09-programmeops-20")
            .To<AddTimeEntryBillabilityKnown>("2026-09-programmeops-21")
            .To<AddTenantReadIndexes>("2026-09-programmeops-22")
            .To<AddOpenAlertUniqueness>("2026-09-programmeops-23")
            .To<AddCodeRepositoryLinkTable>("2026-09-programmeops-24")
            // #18 (Evidence Review) and the modernize branch both shipped a
            // step named -22 before either reached the other. The modernize
            // steps keep -22..-24, so a database that ran them is untouched;
            // Evidence Review moves to -25. A database that ran #18's -22
            // resumes at -23 and would skip the read indexes, so -26 applies
            // them again. Every step here checks before it creates, so a
            // repeat is a no-op.
            .To<AddEvidenceReviewTables>("2026-09-programmeops-25")
            .To<AddTenantReadIndexes>("2026-09-programmeops-26")
            .To<AddUnresolvedIdentitySuggestion>("2026-09-programmeops-27")
            .To<AddTimeEntrySourceWorkItem>("2026-09-programmeops-28")
            .To<AddImportStagingTable>("2026-09-programmeops-29")
            .To<AddEvidenceReviewScope>("2026-09-programmeops-30");
    }
}
