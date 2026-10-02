using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Migrations.ContractOps;
using ProgrammePulse.Services.ContractOps;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace ProgrammePulse.Composers;

/// <summary>
/// Registers the Contract Ops domain: the ContractOps_* schema migration,
/// the contract/document repository, disk-backed (outside wwwroot) document
/// storage, the commercial-position (burn-down) service, and its own audit
/// log. TimeProvider is registered once, by StaffOperationsComposer —
/// deliberately not re-registered here.
/// </summary>
public sealed class ContractOperationsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddScoped<IContractRepository, ContractRepository>();
        builder.Services.AddScoped<IContractAuditLogRepository, ContractAuditLogRepository>();
        builder.Services.AddScoped<IContractDocumentStorageService, ContractDocumentStorageService>();
        builder.Services.AddScoped<IContractCommercialService, ContractCommercialService>();
        builder.Services.AddScoped<IPortfolioMarginService, PortfolioMarginService>();
        builder.Services.AddScoped<IInvoiceGenerationService, InvoiceGenerationService>();
        builder.Services.AddScoped<IContractAdminService, ContractAdminService>();
        builder.Services.AddScoped<IContractObligationRepository, ContractObligationRepository>();
        builder.Services.AddScoped<IContractAssuranceService, ContractAssuranceService>();

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, ContractOpsMigrationStartupHandler>();
    }
}
