namespace ProgrammePulse.Services.Staff;

public sealed record StaffOnboardingRequest(
    string FullName,
    string Email,
    string Password,
    string Role,
    string? JobTitle,
    string? Department,
    string? Team,
    decimal DefaultWorkHoursPerWeek);

public sealed record StaffOnboardingResult(bool Succeeded, Guid? StaffKey, IReadOnlyList<string> Errors)
{
    public static StaffOnboardingResult Success(Guid staffKey) => new(true, staffKey, []);

    public static StaffOnboardingResult Failure(params string[] errors) => new(false, null, errors);
}

public interface IStaffOnboardingService
{
    /// <param name="tenantId">The resolved tenant the new person joins; never inferred.</param>
    Task<StaffOnboardingResult> CreateStaffAsync(StaffOnboardingRequest request, Guid tenantId);
}
