# Security assurance and release evidence

For the **25 September 2026** government-supplier comparison, current GitHub
findings and prioritized release gates, see [ITHC readiness](assurance/ithc-readiness.md).
The validation and CI observations below are historical; they do not certify
the ongoing architecture changes or the latest release candidate.

Updated: 22 September 2026. Scope: the supplied OpsHwb security review and the
current repository. This register separates implemented controls from operating
evidence. It does not claim certification or production sign-off. Hosting and
secret-management choices remain undecided.

## Controls and evidence

| Control / report finding | Implementation | Repeatable evidence | Status |
|---|---|---|---|
| Cross-request authorization leakage | ClickUp/Hub Planner `WithCredential` returns an immutable client; authorization is attached to each request; Jira/Tempo already use request headers | `OutboundSecurityTests.Concurrent_tenants_share_transport_without_sharing_credentials` | Implemented |
| Shared tenant credentials | Startup rejects the shared flag and shared provider tokens outside Development; both sync services independently require Development before fallback; incomplete tenant credentials cannot mix with shared defaults | `ProductionConfigurationGuardTests`; both sync service suites | Implemented |
| Outbound request destinations | Fixed HTTPS hosts, port 443, approved API paths, no user info or fragments; configured bases also reject queries; request-time checks; ClickUp path IDs escaped; all five transports disable redirects and cookies | `OutboundSecurityTests`; `JiraTempoApiClientTests` | Implemented; network egress policy still required |
| Proxy IP spoofing / ineffective limits | Explicit `ReverseProxy:Enabled`; configured immediate proxy IPs only; one forwarded hop; forwarded scheme/address processed before HTTPS and rate limiting; automatic forwarded-header mode rejected outside Development | `TrustedProxyConfigurationTests`; startup guard suite | Implemented; deployed topology test required |
| Secrets in configuration files | Nonblank supported database, certificate, OAuth, imaging and installer secrets rejected from file providers outside Development, even if overridden | `ProductionConfigurationGuardTests.Production_rejects_file_secrets_even_when_overridden_and_does_not_echo_them` | Implemented; vault origin cannot be proven from an environment variable |
| Authentication / MFA visibility | Stable structured events for failed passwords, lockouts, MFA failure/missing challenge, recovery redemption, enrollment and successful login; authorization denial including Umbraco cookie redirects; throttling; credential changes and OAuth refresh failures | `SecurityEventsTests`; real HTTP MFA tests | Events implemented; central collection/alerts unprovisioned |
| MFA final sign-in | Rechecks staff active status, member sign-in eligibility and lockout; missing/invalid code does not create a member session; enrollment-confirm does not regenerate recovery codes for an already-enabled credential | `SecurityBoundaryIntegrationTests`; existing persona tests | Implemented |
| Recovery-code reuse | SQL compare-and-swap accepts an unused code only once, including concurrent attempts; only enabled enrollments can redeem | `SecurityBoundaryIntegrationTests.Concurrent_recovery_code_redemption_succeeds_only_once` | Implemented |
| MFA guessing budget | Each code spends an attempt under a row lock before it is checked; consecutive failures lock for 15 minutes at 10, doubling to 24 hours, and until an Admin reset at 100; recovery codes verify only as salted PBKDF2 | `MfaStorageIntegrationTests.Parallel_wrong_codes_cannot_outrun_the_lock`, `Locks_escalate_…`; `MfaLockoutPolicyTests`; `MfaRecoveryCodeGeneratorTests` | Implemented; alert on reaching the hard stop unprovisioned |
| Contract document reads | Download location rebuilt from the document's own keys under the configured root, share or container; any other stored path or host is a 404 | `ContractDocumentStorageServiceTests` | Implemented |
| Expensive requests | Every `/sync` POST carries the per-client `sync` limit (build rule); report periods bounded to 731 days within 20 years back / 2 ahead | `SyncRateLimitTests`; `ReportingPeriodTests` | Implemented; limits not tuned against real traffic |
| TOTP seed protection | Per-member, versioned Data Protection; write-only protector; dedicated verification service; legacy migration blanks plaintext; failed migration aborts startup | `MfaStorageIntegrationTests`; `MfaSecretProtectionTests`; `DataProtectionKeyRingConfigurationTests` | Implemented; production migration and key custody require evidence |
| TOTP replay | Strictly increasing accepted time step updated by SQL compare-and-swap; enrollment and recovery hashes committed in the same update | `MfaStorageIntegrationTests` concurrent enrollment/verification and same/older-step tests | Implemented and tested on SQL |
| Pending MFA revocation | Expiring single-use SQL challenge; current member stamp, active/approved state and credential version; resets and deactivation revoke pending browsers; separate binding cookie | `MfaChallengeIntegrationTests`; `SecurityBoundaryIntegrationTests` | Implemented; complete cookie theft is not device proof of possession |
| Tenant boundary / source connection / sync state | Tenant predicates in SQL; empty tenant rejected for source connections; wrong-tenant heartbeat/completion fails without changing the owner's run | `SecurityBoundaryIntegrationTests.Source_credentials_and_sync_state_are_isolated_in_real_database`; existing repository/persona boundary suites | Implemented and tested on SQL |
| Data Protection rotation | Durable certificate-encrypted ring; previous certificate permits decryption after rotation | `DataProtectionKeyRingConfigurationTests`; `SourceCredentialProtectorTests` | Existing control verified; live recovery drill required |
| Browser controls | Staff portal CSP strengthened with base/form/object restrictions; every route including backoffice receives base/object/frame restrictions; account pages are `no-store` | `SecurityHeadersMiddlewareTests` | Baseline enforced; full backoffice script CSP unverified |
| Provider payload rendering | Source review found no `Html.Raw`, `HtmlString`, `document.write` or `insertAdjacentHTML` sinks in app views/services/JS. The `innerHTML` use in `mfa-enroll.js` inserts local QR-generated SVG with static alt text. Bronze JSON has no direct UI rendering surface | Re-run source search and browser checks when rendering changes | Source review only; not a full browser/XSS assessment |
| Dependency and secret hygiene | Weekly/manual/PR CI; least-privilege workflow token; JSON dependency audit fails on findings/incomplete evidence; pinned/checksummed gitleaks release; evidence artifacts; Dependabot | CI workflow, `scripts/test-dependency-audit.ps1`, redacted scan reports | Repository configured; branch protection and reviewer rules require repository administration |

The OAuth exchange endpoints were already fixed to Atlassian/Tempo. Jira's
accessible-resources URL is metadata for the selected cloud ID, not an arbitrary
HTTP request destination; its validation is now stricter too. No new runtime
packages were introduced to make these changes.

## Local validation for this change

The MFA follow-up Release build/test run passed **584 tests, zero failures and zero
skips**, with `PP_REQUIRE_SQL_TESTS=1` and the disposable LocalDB test database.
An additional fresh-database run passed **22 focused security tests**. Compilation
emitted no warnings. The current NuGet audit reported no known vulnerable
direct or transitive dependencies. Gitleaks 8.30.1 reported no findings across
74 Git commits. A separate scan covers the tracked/untracked, non-ignored source snapshot. These are
working-copy results, not evidence of a deployed environment or a future commit.
Reports and `mfa-evidence-manifest.json` are under `artifacts/security/`.

GitHub Actions for `1b507cc` completed successfully: [CI run 35652847681](https://github.com/NewbBuilder9909/OpsHwb/actions/runs/35652847681),
including `build-test-audit` and `secret-scan`. The subsequent `ccae385` run
[35692714672](https://github.com/NewbBuilder9909/OpsHwb/actions/runs/35692714672) failed
one deactivation test because revoked challenges now refuse the form before the
test can fetch its antiforgery token. The working-tree correction captures that
token before deactivation and verifies that the POST is denied. The new tests and
corrections still require a passing CI run on their final reviewed commit; local
results do not substitute for that gate. See [MFA rollout and recovery](mfa-security-runbook.md).

## Produce an evidence bundle

Run from the repository root against the exact reviewed revision:

```powershell
dotnet restore
dotnet build --no-restore --configuration Release
$env:PP_REQUIRE_SQL_TESTS = '1'
# On a non-LocalDB runner, inject PP_TEST_SQL_CONNECTION for a disposable test database.
dotnet test --no-build --configuration Release --logger 'trx;LogFileName=security-hardening.trx' --results-directory artifacts/security
./scripts/test-dependency-audit.ps1
# Using the verified gitleaks 8.30.1 binary:
gitleaks git . --redact --report-format json --report-path artifacts/security/gitleaks-history.json
```

Without `PP_REQUIRE_SQL_TESTS=1`, older SQL tests can return early when SQL is
unavailable; such a run is not acceptable tenant-isolation evidence. CI sets this
flag. Record the commit SHA, UTC time, SDK, tool versions, dirty-worktree status,
TRX and audit/scan reports. Local artifacts are ignored by Git. A dirty checkout
must be identified as working-copy evidence and rerun after the final reviewed
commit. CI artifacts are access-controlled evidence, not public marketing assets.

## Production configuration contract

Direct HTTPS requires `ReverseProxy__Enabled=false`. Behind a single proxy:

```text
ReverseProxy__Enabled=true
ReverseProxy__KnownProxies__0=<immediate proxy IP address>
```

Add more indexed entries for alternative immediate proxies. The fixed forward
limit is one: multi-hop deployments must present the correct client address at
the trusted immediate proxy and test it end to end. Leave both
`ASPNETCORE_FORWARDEDHEADERS_ENABLED` and `FORWARDEDHEADERS_ENABLED` unset. Restrict
direct application ingress to that proxy, strip untrusted forwarded headers at
the edge, and enforce approved hostnames there. Verify redirects, Secure cookies,
real client IP in events, and separate client rate-limit buckets. Do not declare
direct mode just to bypass startup validation on a proxied deployment.

Keep `ProgrammeOps:AllowSharedSourceCredentials=false`; do not inject
`ClickUp:ApiToken` or `HubPlanner:ApiKey` into Staging/Production. Reconnect every
tenant explicitly. Missing tenant credentials must fail, not use another account.

Inject deployment secrets from the selected secret manager using workload
identity and least privilege. The guard detects file secrets; it cannot tell
whether an environment variable came from a vault, a terminal or a deployment
manifest. That provenance needs platform evidence. Tenant source credentials
currently remain encrypted in SQL through `ISourceCredentialProtector`; they
have **not** been migrated to a managed vault. Plan reference-based storage and
rotation/migration tests once the platform is chosen. Do not delete the old ring
or replace ciphertext with vault references without a tested migration.

## Monitoring and incident response

Ship structured security events to a collector in a separate administrative
boundary. Preserve `SecurityEvent`, UTC event time, `MemberId`, `TenantId` where
available, `SourceIp`, `Path`, `CorrelationId` and `Provider`. Login has no tenant
context yet; unknown accounts have no member ID. Do not add entered email,
password, OTP/recovery codes, provider bodies, query strings or bearer tokens.
Correlation IDs aid investigation and are not authorization evidence.

Initial alert rules, to be implemented and exercised in the chosen collector:

| Signal | Initial rule | Response |
|---|---|---|
| `LoginFailed`, `AccountLockedOut` | 10 failures per member or source IP in 5 minutes; also monitor global spikes | Investigate spraying, confirm throttling; tune against real traffic |
| `MfaFailed`, `MfaRecoveryFailed`, `MfaPendingMissing`, `MfaSignInDenied` | 5 failures per member/IP in 5 minutes | Investigate account compromise and cookie/session failures |
| `MfaRecoveryRedeemed` | Every event | Notify designated security operator through the approved monitoring integration; verify with account owner |
| `OAuthRefreshFailed` | 3 events per tenant/provider in 15 minutes | Check revoked grant, provider health and rotation; reconnect only after authorization |
| `SourceCredentialSet`, `SourceCredentialCleared` | Every event retained; flag outside approved change windows | Correlate with authorized operator and tenant |
| `AuthorizationDenied`, `RateLimitRejected` | Spike above established baseline | Investigate route probing and denial of service |
| Startup configuration errors / missing log heartbeat | Every configuration error; absent heartbeat per platform SLO | Block rollout or investigate collector failure |

Local Serilog and SQL audit tables are **not tamper-evident storage**. Configure
append-only/immutable retention, restricted deletion, collector access auditing,
clock synchronization and a tested alert destination. Agree retention and IP/user
access controls with the data owner. Record alert test IDs and acknowledgments;
logging code alone does not close this control.

## Rotation and recovery runbook

1. Inventory deployment and tenant credentials with owners, purpose, scope,
   expiry and last-rotation date; never put their values in the register.
2. Back up the encrypted key ring and protect the associated certificates in the
   selected vault. Prove restore and source-credential decryption in an isolated
   environment before changing production keys.
3. For certificate rotation, configure the new `DataProtection:CertificatePath`
   and password plus the old `PreviousCertificatePath` and password. Keep the same
   durable ring and application identity on all instances. New keys use the new
   certificate; old keys still need the old one. Existing key XML is not magically
   re-encrypted. Retire old material only after proving every required historical
   key/ciphertext and backup remains recoverable.
4. For provider rotation, issue a minimally scoped replacement, update only the
   owning tenant's connection, verify a sync against that tenant, then revoke the
   old credential. Confirm a credential-change event and recovery readiness. Never
   enable shared fallback to recover a failed tenant connection.
5. A compromised certificate/ring needs incident response, new protected
   credentials and session invalidation; ordinary certificate rollover is not
   proof that previously stolen material is harmless. Record the affected time
   range, operator, approvals and evidence without secret values.

## Open release gates

Assign named owners and evidence links in the release ticket. These remain open:

| Gate | Owner role | Required evidence |
|---|---|---|
| Production host, vault and tenant secret storage decision | Platform + engineering | Workload identity, scoped grants, network policy, vault audit, tested tenant credential migration or documented approved exception |
| Central monitoring and tamper-resistant retention | Security operations | Collector configuration, retention/delete controls, exercised alert rules and incident contacts |
| Backoffice isolation and full CSP | Platform + application owner | Private/admin ingress or access proxy with MFA, approved hostnames; browser-tested script CSP for Umbraco and installed extensions |
| MFA rollout and independent review | Application security + platform | Rehearsed legacy upgrade, key custody and restore; deployed cross-instance replay/revocation checks; explicit acceptance of cookie-binding limits or a proof-of-possession design |
| Final candidate CI | Engineering | Successful build/test/audit and secret-scan jobs on the commit containing all follow-up fixes and tests |
| Restore and rotation drill | Platform + security | SQL + key-ring restore, old/new certificate decryption, representative tenant reconnect, outage/rollback exercise |
| Repository governance | Repository owner | Required CI checks, protected branches, independent review for security changes, private disclosure route enabled and tested |
| Independent assessment | Security reviewer | Authenticated tenant-boundary, MFA, backoffice, XSS and infrastructure testing against the deployed candidate; scope and outstanding exceptions signed off |

The baseline CSP on the backoffice is deliberately limited: it restricts base
URLs, objects and framing, but does not claim to constrain its scripts. Exact
outbound host checks also do not replace DNS/network egress controls.

Implementation references: [ASP.NET Core proxy trust](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0),
[HTTP redirect behavior](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.allowautoredirect).
