namespace ProgrammePulse.Models.ViewModels.Staff;

/// <summary>A one-time reset link, shown once to the Admin who issued it.</summary>
public sealed record PasswordResetLinkViewModel(Guid StaffKey, string FullName, string Link);

/// <summary>The page a person opens from a reset link.</summary>
public sealed record PasswordResetFormViewModel(int MemberId, string Token, IReadOnlyList<string> Errors, bool Done);
