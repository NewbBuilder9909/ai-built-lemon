using System.ComponentModel.DataAnnotations;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;

namespace ProgrammePulse.Models.ViewModels.Tenancy;

public sealed class TenantAdministratorInput
{
    [Required, StringLength(200)] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)]
    public string Password { get; set; } = "";
}

public sealed record TenantOnboardingViewModel(
    Tenant Tenant, bool AccessAllowed, string? AccessReason, int ActiveStaffCount,
    IReadOnlyList<SourcePublicationStateViewModel> Sources);
