using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Models.ViewModels.ProgrammeOverview;

/// <summary>Data → Customers: the customers, and which customer each programme belongs to.</summary>
public sealed record CustomersPageViewModel(
    IReadOnlyList<Customer> Customers,
    IReadOnlyList<ProgrammePulse.Models.Programme.Programme> Programmes,
    string? Message);
