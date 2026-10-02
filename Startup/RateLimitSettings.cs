using Microsoft.Extensions.Configuration;

namespace ProgrammePulse.Startup;

/// <summary>
/// The per-client-IP fixed-window limits applied to the login and sync
/// endpoint groups (see Program.cs). Previously hardcoded at the call site;
/// bound from configuration so a deployment can tighten them, and so the
/// persona end-to-end harness can raise the login limit in its own test
/// host without replacing the real limiter — see
/// docs/persona-harness-build-gate.md (B1). Six personas need eight login
/// POSTs (two of them Admins, whose MFA step shares the login policy),
/// which the production default of five deliberately refuses.
///
/// Defaults reproduce the previous hardcoded values exactly, so an
/// appsettings file that says nothing about rate limiting behaves as it did
/// before this type existed.
///
/// Pure and static (IConfiguration in, settings out) to match
/// ProductionConfigurationGuard — unit-testable without a host. Invalid or
/// missing values fall back to the default rather than throwing: a typo in
/// a rate limit must not take the site down, and a zero permit limit would
/// lock every user out of login entirely.
/// </summary>
public sealed record RateLimitSettings(int LoginPermitLimit, TimeSpan LoginWindow, int SyncPermitLimit, TimeSpan SyncWindow)
{
    public const string SectionName = "RateLimiting";

    public const int DefaultLoginPermitLimit = 5;
    public const int DefaultSyncPermitLimit = 3;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);

    public static readonly RateLimitSettings Defaults =
        new(DefaultLoginPermitLimit, DefaultWindow, DefaultSyncPermitLimit, DefaultWindow);

    public static RateLimitSettings FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        return new RateLimitSettings(
            ReadPermitLimit(section.GetSection("Login"), DefaultLoginPermitLimit),
            ReadWindow(section.GetSection("Login")),
            ReadPermitLimit(section.GetSection("Sync"), DefaultSyncPermitLimit),
            ReadWindow(section.GetSection("Sync")));
    }

    private static int ReadPermitLimit(IConfigurationSection section, int fallback)
    {
        var value = section["PermitLimit"];
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static TimeSpan ReadWindow(IConfigurationSection section)
    {
        var value = section["WindowSeconds"];
        return int.TryParse(value, out var parsed) && parsed > 0
            ? TimeSpan.FromSeconds(parsed)
            : DefaultWindow;
    }
}
