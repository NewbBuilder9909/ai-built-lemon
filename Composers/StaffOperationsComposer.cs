using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Migrations.StaffOps;
using ProgrammePulse.Services.Security;
using ProgrammePulse.Services.Staff;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace ProgrammePulse.Composers;

/// <summary>
/// Registers the Staff Operations domain: repositories/services for the
/// StaffOps_* tables, role authorization, the leave workflow, and the two
/// startup notification handlers (schema migration + Member Group seeding).
/// Discovered and run automatically by Umbraco's AddComposers() call in
/// Program.cs — no manual wiring needed there.
/// </summary>
public sealed class StaffOperationsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

        builder.Services.AddScoped<IStaffRepository, StaffRepository>();
        builder.Services.AddScoped<IStaffRateRepository, StaffRateRepository>();
        builder.Services.AddScoped<IWorkHoursHistoryRepository, WorkHoursHistoryRepository>();
        builder.Services.AddScoped<IAvailabilityRepository, AvailabilityRepository>();
        builder.Services.AddScoped<ILeaveRequestRepository, LeaveRequestRepository>();
        builder.Services.AddScoped<IMfaRepository, MfaRepository>();
        builder.Services.AddSingleton<MfaSecretProtection>();
        builder.Services.AddScoped<MfaVerificationService>();
        builder.Services.AddScoped<ISignInEligibility, SignInEligibility>();
        builder.Services.AddScoped<IMfaChallengeStore, MfaChallengeStore>();
        builder.Services.AddScoped<IStaffAuditLogRepository, StaffAuditLogRepository>();

        builder.Services.AddScoped<ICurrentStaff, CurrentStaff>();
        builder.Services.AddScoped<IStaffAuthorizationService, StaffAuthorizationService>();
        // Which members hold only oversight roles — capacity views leave them out (StaffRole.OversightOnly).
        builder.Services.AddScoped<IDeliveryRoleDirectory, DeliveryRoleDirectory>();
        builder.Services.AddScoped<ILeaveApprovalService, LeaveApprovalService>();
        builder.Services.AddScoped<ILeaveQueryService, LeaveQueryService>();
        builder.Services.AddScoped<GdprService>();
        builder.Services.AddScoped<IGdprService, TransactionalGdprService>();
        builder.Services.AddScoped<IStaffOnboardingService, StaffOnboardingService>();
        builder.Services.AddScoped<IStaffAdminService, StaffAdminService>();
        builder.Services.AddScoped<IStaffMfaAdministration, StaffMfaAdministration>();
        builder.Services.AddScoped<IMemberAccountAdministration, MemberAccountAdministration>();

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, StaffOpsMigrationStartupHandler>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, StaffGroupSeeder>();
        builder.AddNotificationAsyncHandler<MemberSavedNotification, RevokeMfaChallengesOnMemberSaved>();
    }
}
