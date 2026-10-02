using Xunit.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Umbraco.Cms.Core.DependencyInjection;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Boots the real ProgrammePulse composition (real DI, real composers, real
/// NPoco/IScopeProvider) against a dedicated, disposable LocalDB database —
/// never the developer's own `UmbracoBase` — so tests here exercise the
/// actual SQL Server behaviour of the tenant-isolation code (real unique
/// indexes, real FK-tenant checks) that a hand-rolled in-memory fake cannot.
///
/// Deliberately NOT Umbraco's own `Umbraco.Cms.Tests.Integration` package:
/// its `UmbracoIntegrationTest` base class is built for NUnit's
/// [SetUp]/[OneTimeSetUp] lifecycle, which this project's xUnit runner never
/// invokes — mixing it in here would silently skip Umbraco's own
/// composition setup. `WebApplicationFactory&lt;Program&gt;` is the
/// framework-agnostic, Microsoft-supported equivalent: it boots the same
/// `Program.cs` entry point `dotnet run` uses, just in-process.
/// </summary>
public sealed class ProgrammePulseWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ProgrammePulseWebApplicationFactory() : this(ConnectionString)
    {
    }

    /// <summary>
    /// A host on a different database from the shared integration one, for a
    /// suite whose data would otherwise leak into every other test's tenant
    /// (the Northstar demo seeds 25 staff into the default tenant). Internal
    /// because xUnit requires a collection fixture to have exactly one
    /// public constructor.
    /// </summary>
    internal ProgrammePulseWebApplicationFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>The shared server with a different database name.</summary>
    public static string ConnectionStringFor(string databaseName) =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = databaseName }.ConnectionString;

    private const string DatabaseName = "UmbracoBase_IntegrationTests";
    private const string LocalDbServerInstance = @"(localdb)\GardnerDB";
    private const string TestSqlEnvironmentVariable = "PP_TEST_SQL_CONNECTION";
    private const string RequireSqlEnvironmentVariable = "PP_REQUIRE_SQL_TESTS";
    public static readonly string ConnectionString = Environment.GetEnvironmentVariable(TestSqlEnvironmentVariable)
        ?? $"Server={LocalDbServerInstance};Database={DatabaseName};Integrated Security=true;TrustServerCertificate=true;";

    /// <summary>
    /// Legacy name retained for existing tests. In CI, PP_TEST_SQL_CONNECTION
    /// points to a SQL Server service and PP_REQUIRE_SQL_TESTS makes an
    /// unavailable database a hard test failure rather than a green no-op.
    /// </summary>
    public static readonly bool LocalDbAvailable = ProbeDatabase();

    private const string AcknowledgeSkipEnvironmentVariable = "PP_SKIP_SQL_TESTS";

    /// <summary>
    /// The guard every SQL-backed test opens with:
    /// <c>if (!HasDatabaseOrFail("...", output)) return;</c>
    ///
    /// A test that silently returns when the database is missing reports
    /// "Passed", and a reviewer reading the run summary cannot tell "verified"
    /// from "never ran". For tests whose entire purpose is to be the evidence
    /// that something works — a page renders, a persona is refused, a unique
    /// index holds — that is worse than a red build, because it launders an
    /// absence of evidence into a green tick.
    ///
    /// So a missing database fails by default. Someone who genuinely has no
    /// SQL Server acknowledges it with <c>PP_SKIP_SQL_TESTS=1</c>, which makes
    /// the no-op an explicit choice that prints what went unproven, rather
    /// than a silent one. CI sets <c>PP_REQUIRE_SQL_TESTS=1</c>, which also
    /// makes <see cref="ProbeDatabase"/> retry instead of giving up on the
    /// first connection failure.
    /// </summary>
    /// <param name="evidenceNotProduced">
    /// What this test would have proved, phrased so the failure message names
    /// the missing evidence rather than just the missing database.
    /// </param>
    /// <returns>True to run the test; false only when a skip was acknowledged.</returns>
    public static bool HasDatabaseOrFail(string evidenceNotProduced, ITestOutputHelper? output = null)
    {
        if (LocalDbAvailable)
        {
            return true;
        }

        if (Environment.GetEnvironmentVariable(AcknowledgeSkipEnvironmentVariable) == "1")
        {
            // Known limitation: xUnit surfaces ITestOutputHelper output only
            // for failing tests, and vstest swallows the test host's stdout
            // and stderr, so this line is invisible in a passing run and the
            // summary still reads "Passed". xUnit 2.9 has no dynamic skip
            // (that needs v3 or Xunit.SkippableFact), so an acknowledged skip
            // cannot be reported as "Skipped" here. That is why the flag is
            // opt-in and off by default: the person who sets it is the person
            // accepting the unverified run.
            output?.WriteLine($"NO SQL EVIDENCE ({AcknowledgeSkipEnvironmentVariable}=1): {evidenceNotProduced}");
            return false;
        }

        Assert.Fail(
            $"No database reachable, so this test proved nothing: {evidenceNotProduced} "
            + $"Start SQL LocalDB, point {TestSqlEnvironmentVariable} at a SQL Server, or set "
            + $"{AcknowledgeSkipEnvironmentVariable}=1 to accept a run that does not verify it.");
        return false;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        EnsureTestDatabaseExists(_connectionString);

        builder.UseEnvironment(Environments.Development);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:umbracoDbDSN"] = _connectionString,
                // Pinned empty so the public /purchase page's enquiry route has
                // a deterministic baseline. The host above runs as Development,
                // which loads user secrets — so without this, a developer who
                // has run `dotnet user-secrets set "Commercial:EnquiryEmail"`
                // gets a different rendered page from CI, and a test asserting
                // the unconfigured state passes on one machine and fails on the
                // other. A test that depends on ambient machine configuration
                // is not a test. PurchasePageRenderIntegrationTests overrides
                // this per-case to cover the configured branch.
                ["Commercial:EnquiryEmail"] = "",
                // Local-only per-test host; no HTTPS cert, no backoffice access needed.
                ["Umbraco:CMS:Global:UseHttps"] = "false",
                // A fresh database has no Umbraco core schema — normally created by
                // the installer wizard on first browser request. Unattended install
                // runs that same schema creation (umbracoNode, umbracoKeyValue, ...)
                // during startup instead, which is what a headless test host needs.
                ["Umbraco:CMS:Unattended:InstallUnattended"] = "true",
                ["Umbraco:CMS:Unattended:UnattendedUserName"] = "Integration Test",
                ["Umbraco:CMS:Unattended:UnattendedUserEmail"] = "integration-test@example.com",
                ["Umbraco:CMS:Unattended:UnattendedUserPassword"] = "Integration-Test-P@ssw0rd-1",
                // Without a key in configuration, Umbraco generates one on first
                // boot and writes it into the tracked appsettings.json — a
                // secret one "git add ." away from being committed. Generated
                // per run, so no secret-looking literal sits in source either.
                ["Umbraco:CMS:Imaging:HMACSecretKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64)),
                // Production default is 5 login attempts per minute per client
                // IP; in-process every request shares one partition, so six
                // personas (two of them Admins, whose MFA step shares the
                // login policy) would 429 before the suite finished signing
                // in. Raised here only — Startup/RateLimitSettings keeps the
                // production default, and RateLimitSettingsTests asserts an
                // absent configuration still yields 5/minute.
                ["RateLimiting:Login:PermitLimit"] = "1000"
            });
        });
    }

    /// <summary>
    /// Umbraco keeps one process-wide service provider,
    /// <see cref="StaticServiceProvider.Instance"/>, set by whichever host
    /// booted last. A derived host (<c>WithWebHostBuilder</c> in
    /// PurchasePageRenderIntegrationTests) or a second factory (the Northstar
    /// demo) repointed it, and disposing that host left it on a disposed
    /// container: the next test in the collection that went through
    /// <c>MemberManager</c> failed with ObjectDisposedException, but only
    /// when test order happened to put it afterwards. Each host now puts
    /// back the provider it displaced when it is disposed.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var displaced = StaticServiceProvider.Instance;
        return new RestoringHost(base.CreateHost(builder), displaced);
    }

    private sealed class RestoringHost(IHost inner, IServiceProvider? displaced) : IHost, IAsyncDisposable
    {
        public IServiceProvider Services => inner.Services;

        public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken = default) => inner.StopAsync(cancellationToken);

        public void Dispose()
        {
            var services = inner.Services;
            inner.Dispose();
            Restore(services);
        }

        public async ValueTask DisposeAsync()
        {
            var services = inner.Services;
            if (inner is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else
                inner.Dispose();
            Restore(services);
        }

        // Only if this host still owns it: hosts are not always disposed in
        // the reverse of the order they booted.
        private void Restore(IServiceProvider services)
        {
            if (displaced is not null && ReferenceEquals(StaticServiceProvider.Instance, services))
                StaticServiceProvider.Instance = displaced;
        }
    }

    private static bool ProbeDatabase()
    {
        var required = Environment.GetEnvironmentVariable(RequireSqlEnvironmentVariable) == "1";
        for (var attempt = 0; attempt < (required ? 15 : 1); attempt++)
        {
            try
            {
                using var connection = new SqlConnection(MasterConnectionString());
                connection.Open();
                return true;
            }
            catch (Exception)
            {
                if (!required) return false;
                if (attempt < 14) Thread.Sleep(TimeSpan.FromSeconds(2));
            }
        }

        if (required)
            throw new InvalidOperationException("SQL integration tests are required, but the configured test database server is unavailable.");
        return false;
    }

    private static string MasterConnectionString()
    {
        var builder = new SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 3
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Creates whichever database the *effective* connection string names, not
    /// the hardcoded default: PP_TEST_SQL_CONNECTION can point at a different
    /// database (one per worktree, or a CI service), and creating the default
    /// instead left that database absent and every integration test failing.
    ///
    /// LocalDB does not auto-create a database just because a connection
    /// string names one — Umbraco's own install path expects the empty
    /// shell database to already exist and populates schema into it on
    /// first boot, exactly like a fresh `dotnet run` against a brand-new
    /// LocalDB database. Idempotent: safe to call before every test run.
    /// </summary>
    private static void EnsureTestDatabaseExists(string connectionString)
    {
        var targetDatabase = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (string.IsNullOrWhiteSpace(targetDatabase))
        {
            return;
        }

        using var connection = new SqlConnection(MasterConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = @name)
            BEGIN
                DECLARE @sql NVARCHAR(MAX) = N'CREATE DATABASE ' + QUOTENAME(@name);
                EXEC sp_executesql @sql;
            END
            """;
        command.Parameters.AddWithValue("@name", targetDatabase);
        command.ExecuteNonQuery();
    }
}
