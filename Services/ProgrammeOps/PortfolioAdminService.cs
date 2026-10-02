using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Portfolio set-up that the Reporting Hub and Cost Summary expose to an
/// Admin: customers, which programme belongs to which customer, and
/// programme budgets. The hub's customer and programme filter lists come
/// from here too. Moved out of StaffReportingController, which used to write
/// these straight to the repository.
/// </summary>
public interface IPortfolioAdminService
{
    Task<(IReadOnlyList<Customer> Customers, IReadOnlyList<Programme> Programmes)> GetFilterOptionsAsync(Guid tenantId);

    Task<CommandOutcome> CreateCustomerAsync(Guid tenantId, string? name);

    Task AssignProgrammeToCustomerAsync(Guid tenantId, Guid programmeKey, Guid? customerKey);

    Task SetProgrammeBudgetAsync(Guid tenantId, Guid programmeKey, decimal? budgetAmount, string? budgetCurrency);
}

public sealed class PortfolioAdminService(IProgrammeRepository programmeRepository, TimeProvider timeProvider) : IPortfolioAdminService
{
    public const string CustomerNameRequired = "Enter a customer name.";

    public async Task<(IReadOnlyList<Customer> Customers, IReadOnlyList<Programme> Programmes)> GetFilterOptionsAsync(Guid tenantId) =>
        (await programmeRepository.GetCustomersAsync(tenantId), await programmeRepository.GetProgrammesAsync(tenantId));

    public async Task<CommandOutcome> CreateCustomerAsync(Guid tenantId, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return CommandOutcome.Invalid(CustomerNameRequired);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await programmeRepository.UpsertCustomerAsync(new Customer
        {
            CustomerKey = Guid.NewGuid(),
            Name = name.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, tenantId);

        return CommandOutcome.Ok;
    }

    public Task AssignProgrammeToCustomerAsync(Guid tenantId, Guid programmeKey, Guid? customerKey) =>
        programmeRepository.AssignProgrammeToCustomerAsync(programmeKey, customerKey, tenantId);

    public Task SetProgrammeBudgetAsync(Guid tenantId, Guid programmeKey, decimal? budgetAmount, string? budgetCurrency) =>
        programmeRepository.SetProgrammeBudgetAsync(
            programmeKey,
            budgetAmount,
            string.IsNullOrWhiteSpace(budgetCurrency) ? null : budgetCurrency.Trim().ToUpperInvariant(),
            tenantId);
}
