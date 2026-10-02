using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Models.ViewModels.Tenancy;

/// <summary>Settings → Modules: every benched module, whether it is on, and why it can't be switched on when it can't.</summary>
public sealed record ModulesPageViewModel(IReadOnlyList<ModuleToolboxRow> Rows, string? Message, string? Error);
