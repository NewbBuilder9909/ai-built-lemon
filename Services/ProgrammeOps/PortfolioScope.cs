using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;

namespace ProgrammePulse.Services.ProgrammeOps;

public static class PortfolioScope
{
    public static PortfolioScopeViewModel Resolve(IReadOnlyList<Programme> programmes,
        IReadOnlyList<Customer> customers, Guid? programmeKey, Guid? customerKey)
    {
        var programme = programmes.FirstOrDefault(p => p.ProgrammeKey == programmeKey);
        var customer = customers.FirstOrDefault(c => c.CustomerKey == customerKey);
        var valid = (programmeKey is null || programme is not null)
            && (customerKey is null || customer is not null)
            && (programme is null || customerKey is null || programme.CustomerKey == customerKey);
        return new(programmeKey, customerKey,
            $"{programme?.Name ?? "All programmes"} / {customer?.Name ?? "all customers"}", valid,
            programmes.OrderBy(p => p.Name).Select(p => new PortfolioScopeOption(p.ProgrammeKey, p.Name)).ToList(),
            customers.OrderBy(c => c.Name).Select(c => new PortfolioScopeOption(c.CustomerKey, c.Name)).ToList());
    }

    public static bool Includes(PortfolioScopeViewModel scope, Programme programme) => scope.IsValid
        && (scope.ProgrammeKey is null || programme.ProgrammeKey == scope.ProgrammeKey)
        && (scope.CustomerKey is null || programme.CustomerKey == scope.CustomerKey);
}
