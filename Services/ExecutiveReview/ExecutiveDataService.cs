using Microsoft.Extensions.Options;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Services.ExecutiveReview;

// Data controls remain available when the reporting feature is disabled.
public sealed class ExecutiveDataService(IReviewAccess access, IExecutiveDataRepository data, IOptions<ExecutiveDataOptions> options)
{
    public async Task<ExecutiveDataPage> GetPageAsync()
    {
        var actor = await access.RequireAsync(Capability.ManageStaff);
        return new(options.Value.RetentionDays, await data.GetEventsAsync(actor.TenantId));
    }

    public async Task<ExecutiveDataPreview> PreviewAsync(ExecutiveDataRequest request)
    {
        var actor = await access.RequireAsync(Capability.ManageStaff);
        if (request.Operation == ExecutiveDataOperation.PurgeTenantData)
            await access.RequireAsync(Capability.ManagePlatform);
        return await data.PreviewAsync(actor.TenantId, request);
    }

    public async Task<ExecutiveDataReceipt> ApplyAsync(ExecutiveDataRequest request, string confirmationToken, bool confirmed)
    {
        var actor = await access.RequireAsync(Capability.ManageStaff);
        if (request.Operation == ExecutiveDataOperation.PurgeTenantData)
            await access.RequireAsync(Capability.ManagePlatform);
        if (!confirmed || string.IsNullOrWhiteSpace(confirmationToken)) throw new ReviewValidationException("Review.Data.ConfirmationRequired");
        return await data.ApplyAsync(actor, request, confirmationToken);
    }
}
