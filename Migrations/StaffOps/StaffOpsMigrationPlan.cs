using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.StaffOps;

public sealed class StaffOpsMigrationPlan : MigrationPlan
{
    public StaffOpsMigrationPlan() : base("StaffOps")
    {
        From(string.Empty)
            .To<AddStaffOpsTables>("2026-08-staffops-01")
            .To<AddMemberMfaTable>("2026-08-staffops-02")
            .To<AddStaffAuditLogTable>("2026-09-staffops-03")
            .To<AddStaffTenantColumn>("2026-09-staffops-04")
            .To<AddWorkHoursHistoryTable>("2026-09-staffops-05")
            .To<AddMemberMfaRecoveryCodes>("2026-09-staffops-06")
            .To<BackfillStaffTenantColumn>("2026-09-staffops-07")
            .To<AddLeaveRequestTenantColumn>("2026-09-staffops-08")
            .To<ProtectMfaSecrets>("2026-09-staffops-09")
            .To<AddMfaChallengeFailedAttempts>("2026-09-staffops-10")
            .To<AddStaffAuditLogTenantColumn>("2026-09-staffops-11")
            .To<AddMemberMfaLockout>("2026-09-staffops-12");
    }
}
