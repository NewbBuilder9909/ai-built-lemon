namespace ProgrammePulse.Services.Commercial;

public sealed record SelfServiceSignupRequest(
    string CompanyName,
    string ShortCode,
    string AdminFullName,
    string AdminEmail,
    string Password,
    string ConfirmPassword,
    string Plan,
    IReadOnlyList<string> RequestedModules);

public sealed record SelfServiceSignupResult(bool Succeeded, Guid? TenantKey, IReadOnlyList<string> Errors)
{
    public static SelfServiceSignupResult Success(Guid tenantKey) => new(true, tenantKey, []);

    public static SelfServiceSignupResult Failure(params string[] errors) => new(false, null, errors);
}

public interface ISelfServiceSignupService
{
    Task<SelfServiceSignupResult> CreateTrialAsync(SelfServiceSignupRequest request);
}
