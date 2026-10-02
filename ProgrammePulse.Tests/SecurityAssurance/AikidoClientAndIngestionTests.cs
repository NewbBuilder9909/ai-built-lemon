using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.SecurityAssurance;
using ProgrammePulse.Services.Integrations.Aikido;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.SecurityAssurance;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.SecurityAssurance;

/// <summary>
/// The Aikido client against a scripted HTTP handler, and one ingestion run
/// against the in-memory repository. The rules: credentials only ever go to
/// an allow-listed Aikido host; Bronze never holds a file path; a partial
/// read never replaces observations or claims a clean sync; a rejected
/// credential marks the connection access-lost.
/// </summary>
public sealed class AikidoClientAndIngestionTests
{
    private static readonly Guid Tenant = Guid.Parse("c0a70000-0000-0000-0000-000000000005");
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    // ---- The client ----

    [Fact]
    public async Task The_token_request_uses_basic_auth_at_the_regional_host_and_reads_use_the_bearer_token()
    {
        var requests = new List<HttpRequestMessage>();
        var client = new AikidoClient(new HttpClient(new Handler(request =>
        {
            requests.Add(request);
            return request.RequestUri!.AbsolutePath == "/api/oauth/token"
                ? Json("""{"access_token":"tok","expires_in":3600}""")
                : Json("[]");
        })));

        Assert.True(await client.VerifyAsync("us", "id", "secret", CancellationToken.None));

        Assert.Equal("app.us.aikido.dev", requests[0].RequestUri!.Host);
        Assert.Equal(("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("id:secret"))),
            (requests[0].Headers.Authorization!.Scheme, requests[0].Headers.Authorization!.Parameter));
        Assert.Equal(("Bearer", "tok"), (requests[1].Headers.Authorization!.Scheme, requests[1].Headers.Authorization!.Parameter));
        Assert.StartsWith("/api/public/v1/repositories/code", requests[1].RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("app.aikido.dev.evil.example")]
    [InlineData("")]
    public async Task An_unknown_region_never_sends_the_credential_anywhere(string region)
    {
        var sent = 0;
        var client = new AikidoClient(new HttpClient(new Handler(_ => { sent++; return Json("{}"); })));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.VerifyAsync(region, "id", "secret", CancellationToken.None));
        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task A_rejected_credential_is_access_lost()
    {
        var client = new AikidoClient(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))));

        await Assert.ThrowsAsync<AikidoAccessLostException>(() => client.VerifyAsync("eu", "id", "bad", CancellationToken.None));
    }

    [Fact]
    public async Task Pages_are_redacted_and_a_page_number_the_API_ignores_does_not_loop()
    {
        // Every page returns the same full page of 1,000 issues: without the
        // repeat check this would read to the page budget.
        var fullPage = "[" + string.Join(",", Enumerable.Range(1, 1000).Select(i =>
            $$"""{"id":{{i}},"severity":"high","status":"open","code_repo_id":1,"first_detected_at":1758000000,"affected_file":"src/f{{i}}.js"}""")) + "]";
        var issueCalls = 0;
        var client = new AikidoClient(new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/oauth/token" => Json("""{"access_token":"tok"}"""),
            "/api/public/v1/issues/export" => Json(fullPage, () => issueCalls++),
            _ => Json("[]"),
        })));

        var read = await client.ReadAsync("eu", "id", "secret", CancellationToken.None);

        Assert.Equal(2, issueCalls);
        Assert.True(read.IsComplete);
        var page = Assert.Single(read.Issues);
        Assert.Equal(1000, page.Items.Count);
        Assert.DoesNotContain("affected_file", page.RedactedJson);
    }

    [Fact]
    public async Task A_failed_page_is_reported_incomplete_with_the_reason()
    {
        var client = new AikidoClient(new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/oauth/token" => Json("""{"access_token":"tok"}"""),
            "/api/public/v1/repositories/code" => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            _ => Json("[]"),
        })));

        var read = await client.ReadAsync("eu", "id", "secret", CancellationToken.None);

        Assert.False(read.IsComplete);
        Assert.Contains("/repositories/code", read.IncompleteReason);
    }

    // ---- The ingestion run ----

    private readonly FakeSecurityAssuranceRepository _repository = new();
    private readonly SecurityCredentialProtector _protector = new(new EphemeralDataProtectionProvider());
    private readonly SyncRunGuard _guard = new();

    [Fact]
    public async Task A_clean_run_replaces_observations_upserts_findings_and_records_success()
    {
        Connect();
        _repository.Observations.Add(SecurityRecords.Observation(Tenant, new CodeRepositoryRef("GitHub", "acme", "acme/gone"), toolRepositoryId: "99"));

        var result = await Ingestion(new ScriptedAikido(Read(complete: true))).RunAsync(Tenant, 3);

        Assert.Equal(CommandStatus.Succeeded, result.Status);
        Assert.Equal(["acme/api"], _repository.Observations.Select(o => o.Repository.RepositoryKey));
        Assert.Equal(["11"], _repository.Findings.Select(f => f.ExternalId));
        var connection = Assert.Single(_repository.Connections);
        Assert.Equal((Now, (string?)null), (connection.LastSyncSucceededAtUtc, connection.LastSyncError));
        Assert.Equal(1, _repository.PurgeCalls);
        Assert.All(_repository.Raw, raw => Assert.DoesNotContain("affected_file", raw.Json));
    }

    [Fact]
    public async Task A_partial_run_keeps_existing_observations_and_does_not_claim_a_clean_sync()
    {
        Connect();
        var gone = SecurityRecords.Observation(Tenant, new CodeRepositoryRef("GitHub", "acme", "acme/still-scanned"), toolRepositoryId: "99");
        _repository.Observations.Add(gone);

        var result = await Ingestion(new ScriptedAikido(Read(complete: false))).RunAsync(Tenant, 3);

        Assert.Equal(CommandStatus.Refused, result.Status);
        Assert.Contains(gone, _repository.Observations);
        var connection = Assert.Single(_repository.Connections);
        Assert.Null(connection.LastSyncSucceededAtUtc);
        Assert.Equal("rate limited", connection.LastSyncError);
        Assert.Equal(0, _repository.PurgeCalls);
    }

    [Fact]
    public async Task A_rejected_credential_marks_the_connection_access_lost()
    {
        Connect();

        var result = await Ingestion(new ScriptedAikido(null, new AikidoAccessLostException("rejected"))).RunAsync(Tenant, 3);

        Assert.Equal(CommandStatus.Refused, result.Status);
        Assert.Equal(SecurityConnectionStatus.AccessLost, Assert.Single(_repository.Connections).Status);
    }

    [Fact]
    public async Task A_disconnected_tenant_cannot_sync_and_one_run_at_a_time()
    {
        Assert.Equal(CommandStatus.Refused, (await Ingestion(new ScriptedAikido(Read(true))).RunAsync(Tenant, 3)).Status);

        Connect();
        using (_guard.TryEnter(Tenant, AikidoIngestionService.SourceName))
        {
            var blocked = await Ingestion(new ScriptedAikido(Read(true))).RunAsync(Tenant, 3);
            Assert.Contains("already running", blocked.Message);
        }
    }

    [Fact]
    public async Task Connecting_verifies_first_and_stores_the_secret_only_encrypted()
    {
        var service = new AikidoConnectionService(_repository, _protector, new ScriptedAikido(Read(true)), new FixedTime(Now));

        Assert.Equal(CommandStatus.Refused, (await service.ConnectAsync(Tenant, Guid.NewGuid(), 3, "mars", "id", "secret", CancellationToken.None)).Status);
        var result = await service.ConnectAsync(Tenant, Guid.NewGuid(), 3, " EU ", "id", "s3cret-value", CancellationToken.None);

        Assert.Equal(CommandStatus.Succeeded, result.Status);
        var stored = Assert.Single(_repository.Connections);
        Assert.Equal("eu", stored.Region);
        Assert.DoesNotContain("s3cret-value", stored.ProtectedCredentialJson);
        Assert.Equal("s3cret-value", _protector.Unprotect(stored.ProtectedCredentialJson)!.ClientSecret);
        Assert.DoesNotContain(_repository.Audit, a => a.Detail?.Contains("s3cret") == true);

        await service.DisconnectAsync(Tenant, 3, CancellationToken.None);
        Assert.Equal((SecurityConnectionStatus.Disconnected, (string?)null),
            (_repository.Connections[0].Status, _repository.Connections[0].ProtectedCredentialJson));
    }

    private AikidoIngestionService Ingestion(IAikidoClient client) => new(client, _repository, _protector, _guard, new FixedTime(Now));

    private void Connect() => _repository.Connections.Add(new SecurityToolConnection
    {
        ConnectionKey = Guid.NewGuid(), TenantId = Tenant, Tool = SecurityTools.Aikido, Region = "eu", ClientId = "id",
        ProtectedCredentialJson = _protector.Protect(new SecurityToolCredential("id", "secret")),
        Status = SecurityConnectionStatus.Active, CreatedAtUtc = Now, UpdatedAtUtc = Now,
    });

    private static AikidoReadResult Read(bool complete)
    {
        static AikidoPage Page(string endpoint, IReadOnlyList<string> fields, string json)
        {
            using var document = JsonDocument.Parse(json);
            var items = AikidoMapper.Items(document.RootElement).Select(i => AikidoClient.Redact(i, fields)).ToList();
            return new AikidoPage(endpoint, 0, items, JsonSerializer.Serialize(items));
        }

        return new AikidoReadResult(
            [Page("/repositories/code", AikidoClient.RepositoryFields, """[{"id":1,"provider":"github","url":"https://api.github.com/repos/acme/api"}]""")],
            [],
            [Page("/issues/export", AikidoClient.IssueFields,
                """[{"id":11,"severity":"high","status":"open","code_repo_id":1,"first_detected_at":1758000000,"affected_file":"src/a.js"}]""")],
            [],
            complete,
            complete ? null : "rate limited");
    }

    private sealed class ScriptedAikido(AikidoReadResult? read, Exception? failure = null) : IAikidoClient
    {
        public Task<bool> VerifyAsync(string region, string clientId, string clientSecret, CancellationToken cancellationToken) =>
            failure is null ? Task.FromResult(true) : Task.FromException<bool>(failure);

        public Task<AikidoReadResult> ReadAsync(string region, string clientId, string clientSecret, CancellationToken cancellationToken) =>
            failure is null ? Task.FromResult(read!) : Task.FromException<AikidoReadResult>(failure);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    private static HttpResponseMessage Json(string body, Action? onSend = null)
    {
        onSend?.Invoke();
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
