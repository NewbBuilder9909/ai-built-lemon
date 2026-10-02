using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using ProgrammePulse.Middleware;
using ProgrammePulse.Services.Security;
using ProgrammePulse.Services.ExecutiveReview;
using ProgrammePulse.Startup;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

AzureKeyVaultBootstrap.Configure(builder);
AzureAppConfigurationBootstrap.Configure(builder);
OpenTelemetryBootstrap.Configure(builder);

TrustedProxyConfiguration.Configure(builder.Services, builder.Configuration);

DataProtectionKeyRingConfiguration.Configure(builder.Services, builder.Configuration, builder.Environment.EnvironmentName);

// Login and the sync triggers are the two endpoint groups worth throttling
// independently of Umbraco Member lockout (which only limits a single
// account, not a distributed attempt across many).
//
// Partitioned per client address, not one global bucket — a single shared
// fixed window would let five bad password attempts from anywhere lock the
// login page for every user for a minute. Behind a reverse proxy the real
// client address is accepted only from ReverseProxy:KnownProxies, with
// ReverseProxy:Enabled explicitly declared outside Development. Forwarded
// headers run before HTTPS handling and this limiter.
// Limits are configurable (RateLimiting:Login / :Sync) with the previous
// hardcoded values as defaults — see Startup/RateLimitSettings.
//
// Bound from the *composed* IConfiguration via DI rather than read from
// builder.Configuration here. Reading eagerly at this point silently misses
// any source added after the builder is constructed — which is exactly what
// WebApplicationFactory does, so an integration test could not override the
// limit and its persona sign-ins were rejected with 429 while the
// configuration looked correct.
builder.Services.AddSingleton(sp => RateLimitSettings.FromConfiguration(sp.GetRequiredService<IConfiguration>()));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context =>
    {
        var limits = context.RequestServices.GetRequiredService<RateLimitSettings>();
        return RateLimitPartition.GetFixedWindowLimiter(
            ClientPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.LoginPermitLimit, Window = limits.LoginWindow, QueueLimit = 0 });
    });
    options.AddPolicy("sync", context =>
    {
        var limits = context.RequestServices.GetRequiredService<RateLimitSettings>();
        return RateLimitPartition.GetFixedWindowLimiter(
            ClientPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.SyncPermitLimit, Window = limits.SyncWindow, QueueLimit = 0 });
    });

    static string ClientPartitionKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
});

// Two probes: /health (liveness — process up, pipeline wired, no
// dependencies, so a database outage never triggers a restart loop) and
// /health/ready (readiness — includes the database check, so an instance
// that can't reach SQL is taken out of rotation). Both AllowAnonymous: a
// probe has no Umbraco Member session to authenticate with.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name, tags: [DatabaseHealthCheck.ReadyTag]);

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

// A second, short-lived cookie scheme that holds an Admin's identity between
// a correct password and a correct TOTP code (MfaChallengeStore) — entirely
// separate from Umbraco's own Member sign-in cookie, so holding only this
// one grants no access to anything. AddAuthentication() with no default
// scheme argument only registers this additional scheme; it does not
// change Umbraco's own default authentication scheme set by AddBackOffice/
// AddWebsite below. Umbraco's own cookies get Secure from
// Umbraco:CMS:Global:UseHttps (enforced non-Development by
// ProductionConfigurationGuard); this one has to be told explicitly.
var mfaCookieSecurePolicy = Enum.TryParse<CookieSecurePolicy>(builder.Configuration["Security:MfaCookieSecurePolicy"], true, out var configuredPolicy)
    ? configuredPolicy
    : CookieSecurePolicy.SameAsRequest;

builder.Services.AddAuthentication().AddCookie(MfaChallengeStore.SchemeName, options =>
{
    options.Cookie.Name = "ops-mfa-pending";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = mfaCookieSecurePolicy;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    options.SlidingExpiration = false;
});

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();

// Fail fast on a misconfigured non-Development deployment before Umbraco
// boots (and before it could, e.g., present its installer on an empty
// connection string). Warnings are logged and startup continues.
var configurationFindings = ProductionConfigurationGuard.Validate(app.Configuration, app.Environment.EnvironmentName);
foreach (var warning in configurationFindings.Warnings)
{
    app.Logger.LogWarning("Configuration warning: {ConfigurationWarning}", warning);
}

if (configurationFindings.HasErrors)
{
    foreach (var error in configurationFindings.Errors)
    {
        app.Logger.LogCritical("Configuration error: {ConfigurationError}", error);
    }

    throw new InvalidOperationException(
        $"Refusing to start in environment '{app.Environment.EnvironmentName}' — {configurationFindings.Errors.Count} configuration error(s): "
        + string.Join(" | ", configurationFindings.Errors));
}

// Outermost, so an unexpected exception anywhere below becomes the
// friendly error page with a quotable correlation id instead of a bare 500.
// Development keeps the developer exception page (see ErrorPagePolicy).
if (ErrorPagePolicy.UseErrorPage(app.Configuration, app.Environment.EnvironmentName))
{
    app.UseExceptionHandler(ErrorPagePolicy.Path);
}

if (app.Configuration.GetValue<bool>("ReverseProxy:Enabled"))
    app.UseForwardedHeaders();
app.UseHttpsRedirection();
if (app.Configuration.GetValue("Security:RequireHsts", defaultValue: true))
{
    app.UseHsts();
}

await app.BootUmbracoAsync();

// Languages, formats and markets are configuration (the "Localization"
// section, Services/Localization/LocalizationSettings) — adding a language is
// a settings entry plus a translation file; see docs/localization.md. A bad
// entry stops startup here rather than surfacing as a broken switcher or a
// report in an unknown time zone.
var localizationSettings = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProgrammePulse.Services.Localization.LocalizationSettings>>().Value;
var localizationProblems = localizationSettings.Validate();
if (localizationProblems.Count > 0)
{
    throw new InvalidOperationException("Invalid Localization configuration: " + string.Join(" | ", localizationProblems));
}

foreach (var untranslated in localizationSettings.CulturesWithoutTranslations(typeof(ProgrammePulse.Resources.SharedResource).Assembly))
{
    app.Logger.LogWarning(
        "Language {Culture} is offered but has no Resources/SharedResource.{Culture}.resx, so its screens show English.",
        untranslated, untranslated);
}

var localizationOptions = MarketConfiguration.LocalizationOptions(localizationSettings);

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.AppBuilder.UseMiddleware<CorrelationIdMiddleware>();
        u.AppBuilder.UseMiddleware<SecurityEventMiddleware>();
        u.AppBuilder.UseMiddleware<SecurityHeadersMiddleware>();
        u.AppBuilder.UseRateLimiter();
        u.AppBuilder.UseRequestLocalization(localizationOptions);
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
        u.EndpointRouteBuilder.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false
        }).AllowAnonymous();
        u.EndpointRouteBuilder.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(DatabaseHealthCheck.ReadyTag)
        }).AllowAnonymous();
    });

await app.RunAsync();

/// <summary>
/// Exposes the top-level-statement entry point to `WebApplicationFactory
/// &lt;Program&gt;` in ProgrammePulse.Tests/Integration — the standard,
/// zero-behaviour-change marker ASP.NET Core integration tests need to boot
/// this real composition (real DI, real LocalDB) in-process. No effect on
/// the running application.
/// </summary>
public partial class Program;
