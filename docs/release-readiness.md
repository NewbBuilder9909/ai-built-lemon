# Release readiness: Go-to-Market assessment (16 September 2026)

Outcome of a structured readiness pass over the whole application, run in
the order: baseline → audit → deployment/config → security → tenancy →
product/admin controls → integration resilience → governance → release
gate. Each phase was gated on `dotnet build` and `dotnet test` passing and
on a real boot of the app. This file is the living risk register — update
the **Status** column as items close.

> Security update (21 September 2026): [security-assurance.md](security-assurance.md) supersedes historical proxy, shared-credential and security-signoff claims below. Its open deployment gates remain release requirements.

## 1. Baseline (Phase 1) — root cause and fix

**Symptom.** `dotnet build` failed with MSB3027/MSB3021 (cannot copy
`ProgrammePulse.exe`, file in use).

**Root cause (evidence).** A `ProgrammePulse.exe` (PID 37276, started
21:01) was still running, spawned by a `dotnet run` (PID 33636) whose
parent was a stray `cmd.exe` (PID 22956) — not the IDE debugger. An
exclusive-open test on `bin\Debug\net10.0\ProgrammePulse.exe` confirmed
the lock. After terminating both processes the same test reported the file
free.

**Code blocker.** With the lock gone, the only compile error was in the
test project: the in-progress Hub Planner work had added a fifth
constructor parameter to `StaffProgrammeOverviewController` and
`StaffProgrammeOverviewControllerTests` still passed four. Fixed by passing
the fifth `null!` and adding the matching Admin-only gate test for
`SyncHubPlanner`.

**Evidence.** `dotnet restore` exit 0 → `dotnet build` exit 0, 0 warnings →
`dotnet test` exit 0, **233 passed / 0 failed**. (End of run: **344 passed /
0 failed**, 0 warnings.)

## 2. Risk register (Phase 2 audit, updated through the release gate)

Severity: **H**igh / **M**edium / **L**ow. Owner type: Eng = application
engineering, Ops = platform/DevOps, Prod = product/commercial, Legal = DPO/
legal. Phase = where it was (or should be) addressed.

| # | Risk | Sev | Impact | Recommended fix | Owner | Phase | Status |
|---|---|---|---|---|---|---|---|
| R1 | Stale app process locks build output | H | No builds/tests possible | Kill orphaned `dotnet run`; use IDE run task | Eng | 1 | **Fixed** |
| R2 | Login/sync rate limiters were one *global* bucket (5 login attempts/min for the whole site) | H | Trivial login denial-of-service; sync throttled for everyone | Partition per client IP (`RateLimitPartition.GetFixedWindowLimiter`) | Eng | 1 | **Fixed** (smoke: 5×400 then 429 from one client) |
| R3 | LocalDB connection string in base `appsettings.json`; empty string would boot Umbraco's unauthenticated installer | H | Prod silently on dev DB, or installer exposed | `ProductionConfigurationGuard` fails fast; LocalDB moved to `appsettings.Development.json`; `appsettings.Production.json` added | Eng | 0 | **Fixed** (smoke: Production start refused with clear message) |
| R4 | Proxy trust and real client address | M | Spoofed client IP or ineffective rate limits | Explicit ReverseProxy mode and KnownProxies; automatic trust rejected | Ops/Eng | Security hardening | **Code tested; deployment topology evidence required** |
| R5 | `/health` had no dependency check | M | LB keeps routing to an instance that lost SQL | `/health/ready` with `DatabaseHealthCheck`; `/health` stays liveness-only | Eng | 0 | **Fixed** |
| R6 | No request correlation in logs | L | Slow incident triage | `CorrelationIdMiddleware` → header + Serilog property | Eng | 0 | **Fixed** |
| R7 | MFA pending cookie not `Secure=Always`; HSTS default 30 days | M | Cookie replay over HTTP if proxy misconfigured | `SecurePolicy.Always` outside Development; HSTS 1 year + subdomains; `Umbraco:Global:UseHttps=true` enforced | Eng | 1 | **Fixed** |
| R8 | Staff onboarded via the admin form got **no tenant** → unresolved tenant → locked out of tenant-scoped pages | H | Broken product for every new member | `StaffOnboardingService` stamps creator's tenant; `BackfillStaffTenantColumn` repairs existing rows | Eng | 2 | **Fixed** (DB: 0 null-tenant rows) |
| R9 | No tenant lifecycle — could not suspend/archive a customer | H | Can't enforce non-payment / offboarding | `TenantStatus`, `TenantAccessPolicy`, global `TenantAccessFilter` (403 page) | Eng/Prod | 2 | **Fixed** |
| R10 | No plan/entitlement concept | H | Nothing to sell tiers against | `TenantPlan`, `PlanEntitlements` (+ config override), `IFeatureGate`, `[RequireFeature]` on Contracts + both syncs; nav hides Contracts | Eng/Prod | 2–3 | **Fixed** |
| R11 | No operator role distinct from a customer's Admin | H | A tenant Admin could change its own plan/suspension | `Platform Admin` Member Group (backoffice-granted only), `IsPlatformAdminAsync`, `/staffops/platform/tenants` console | Eng | 3 | **Fixed** |
| R12 | Staff roster and per-staff admin actions not tenant-scoped | H | Cross-tenant read/act by guessed `StaffKey` | `GetByTenantAsync`; NotFound for keys outside caller's tenant; Forbid when unresolved | Eng | 2/8/9 | **Fixed** — Staff admin, Reporting/Programme Overview/RAID/Governance/Alerts/Identity queue/sync state (increment 8), and as of increment 9 Contract Ops (`ContractOps_*` tenant columns + FK-ownership checks) and Approvals (`StaffOps_LeaveRequest.tenantId`, tenant-scoped `GetPendingAsync`/`ApproveAsync`/`RejectAsync`) |
| R13 | Member Groups are global — role ≠ tenant-aware | M | An "Admin" is an Admin everywhere once R12's open areas are scoped | Tenant-qualified role check (Admin *of tenant X*) before scoping a second data area | Eng | 9 | **Practically closed** — every tenant-scoped action (ProgrammeOps, Contract Ops, Approvals) now resolves the caller's tenant, `Forbid()`s if unresolved, and verifies the acted-on record's own `TenantId` before reading/writing it (`CrossTenantReferenceException` → `NotFound()` for a foreign/guessed key), so an Admin/Approver cannot reach another tenant's data via a guessed key even though Member Groups remain global. The underlying "Member Groups aren't tenant-scoped" architecture question (tenant-scoped Member Groups, or a per-tenant role table) is unchanged and still a deliberate post-launch item — see `docs/tenancy.md` — but is no longer a live risk for the current single-instance/named-tenant pilot model |
| R14 | Attaching an existing member to a tenant is a manual SQL update | M | Onboarding friction; error-prone | "Move member to tenant" action on the platform console | Eng/Prod | post-launch | **Open** (creating tenants is now UI) |
| R15 | ClickUp client had no retry/backoff/429 handling; Hub Planner had a single ad-hoc retry | H | Syncs fail on ordinary rate limits | Shared `TransientHttpRetryHandler` (3 retries, Retry-After honoured, capped, permanent codes never retried), 30s per-call timeout | Eng | 4 | **Fixed** |
| R16 | Sync failures not audited; partial runs invisible | H | Silent stale data | `SyncFailed` audit with stage + progress; re-runnable error message; console shows last success/failure | Eng | 4 | **Fixed** |
| R17 | Two concurrent sync triggers race the upserts (duplicate Silver rows); a worker that lost the lease could keep processing and publish/overwrite over the new owner | M | Data duplication; stale writes from a fenced-out worker | `SyncRunGuard` per source (process-wide) + `ProgrammeOps_SyncLease` database lease via `SyncRunCoordinator` (conditional UPDATE on the DB clock, heartbeat before every Silver write, abandoned-run takeover); heartbeat/completion now themselves fenced on `(source, ownerInstanceId, runKey)` — a stale worker's next touch throws `SyncLeaseLostException` and it stops, and `Fail`/`Complete` only transition a still-`Running` row so the abandoned marker is never overwritten | Eng | 4, 6, 7 | **Fixed** single- and multi-node (lease *and* fencing semantics unit-tested over the fake — five lease-loss scenarios in `SyncRunCoordinatorTests` — and an 8-step two-instance SQL smoke exercised against LocalDB; no live two-process integration test yet) |
| R18 | Sync runs synchronously inside the HTTP request | M | Proxy timeouts on large workspaces | Background/queued run, page polls audit log | Eng | post-launch | **Open** |
| R19 | Bronze capture tables grow without bound | M | Storage cost; PII-bearing JSON retained forever | `ProgrammeOps:RawPayloadRetentionDays` (90) purge after each successful sync; failure audited | Eng | 4 | **Fixed** |
| R20 | No replay path from Bronze | L | Re-mapping requires a fresh pull | Documented as out of scope; Silver is the system of record | Eng | — | **Accepted** |
| R21 | Hub Planner client unverified against a live account (envelope shape, timezones) | M | First real sync may return nothing | Verify with a read-only key before any customer demo | Eng | pre-pilot | **Open** |
| R22 | GDPR export omitted the audit trail; no retention automation | M | Incomplete Art. 15 response; manual purges | Audit trail added to export; retention table + runbooks in `data-governance.md` | Eng/Legal | 5 | **Partly fixed** (automation open) |
| R23 | No admin visibility of audit tables | M | Can't answer "who changed this" without SQL | `/staffops/admin/audit` (Staff Ops + Programme Ops merged) | Eng | 3/5 | **Fixed** |
| R24 | Umbraco backoffice (`/umbraco`) on the same public host | M | Second admin surface exposed | Edge IP allowlist / WAF rule for `/umbraco`; backoffice MFA | Ops | pre-pilot | **Open** |
| R25 | HWB demo dashboard (`/opshwb`) is anonymous | L | Confusing/unbranded surface in a customer deployment | Remove or gate before a customer-facing deploy | Prod | pre-pilot | **Open (decision)** |
| R26 | Login/MFA/tenant-resolution paths have no automated tests (concrete `MemberManager`) | M | Regressions only found by hand | Integration-test harness with a real Umbraco host, or a thin seam over `MemberManager` | Eng | post-launch | **Open** (decision logic is unit-tested; member lookup is not) |
| R27 | CI builds and scans but does not publish an artefact; central package management allows floating versions | L | Non-reproducible deploys | Add `dotnet publish` artefact + environment-specific deploy job; disable floating versions | Ops | post-launch | **Open** |
| R28 | Secrets in source control | — | — | Verified none: grep of tracked files, gitleaks in CI, tokens only via user-secrets/env vars | — | — | **OK** |
| R29 | Feature gate fails *open* for an unresolved tenant | L | A dangling-tenant member sees premium features (role checks still apply) | Reversed: `TenantFeatureGate` fails closed with `FeatureGateOutcome.TenantUnresolved` and a setup-not-plan message | Eng | 6 | **Fixed** |
| R30 | No login/MFA/role-change audit events | M | Access-control questions rely on Serilog | Audit `LoginSucceeded/Failed`, `MfaChallengePassed/Failed`, group changes | Eng | post-launch | **Open** |
| R31 | No outbound notification (email/webhook) for alerts or sync failures | L | Operators must look at the console | Wire an SMTP/webhook sink once configured | Eng | post-launch | **Open** |
| R32 | Hub Planner bookings became **Done work items** once their end date passed — time passing read as delivery progress in every Done-counting Gold view (GTM review B04) | H | Overstated % complete / earned value for any customer-facing KPI | `PlannedAllocation` Silver entity with no lifecycle stage; legacy rows removed by migration (audited); overview shows planned allocation and estimate-based workload separately with coverage figures | Eng/Prod | 6 | **Fixed** |
| R33 | Unmatched assignees/resources were **silently dropped**, so capacity looked healthier than it was (GTM review B05) | H | Missing people in workload/capacity with no signal | `IStaffIdentityResolver` + `ExternalIdentityLink` + `UnresolvedIdentity` queue (`/staffops/programme/identities`); unmatched counts in sync results and the overview header | Eng | 6 | **Fixed** (field-authority / override preservation still open) |
| R34 | No durable run state: "last published / running / failed" was inferred from audit rows, and a crashed run left no trace (GTM review B06) | M | Readers can't tell how fresh or complete the data is | `ProgrammeOps_SyncRun` + publication-state panel on the overview and platform console; abandoned runs marked Failed on lease takeover | Eng | 6 | **Fixed** (background execution R18 still open) |
| R35 | `ProgrammeOps_*` still keyed by `(ExternalSource, ExternalId)` with deployment-level source options — a second customer's account on the same instance would collide (GTM review B03) | H | Blocks shared multi-tenant SaaS, not a dedicated pilot | `tenantId` on every `ProgrammeOps_*` table, Silver upserts keyed on `(TenantId, ExternalSource, ExternalId)`, `ProgrammeOps_SourceConnection` registry (auto-provisioned per tenant per source, `ExternalIdentityLink.ConnectionKey` provenance), sync lease/run keyed on `(tenantId, source)`, per-tenant credential isolation (`ISourceCredentialProtector`, `/staffops/programme/connections`) | Eng | 8/9 | **Fixed** — schema isolation (increment 8) and credential isolation (increment 9, `ProgrammeOps_SourceConnection.protectedCredentialJson` encrypted via ASP.NET Core Data Protection, resolved once per sync run with a deployment-wide fallback for tenants that haven't configured their own) are both closed. Also closed this increment: `ProgrammeRepository`'s ~12 FK-carrying Upsert/Create/Add methods now verify a referenced parent (`ProgrammeKey`/`ProjectKey`/`WorkstreamKey`/`WorkItemKey`/`CustomerKey`) belongs to the caller's own tenant before writing (`CrossTenantReferenceException`), closing the gap where row-level `tenantId` filtering alone didn't stop a guessed foreign key from another tenant being accepted; `SyncRunGuard`'s in-process latch is now `(tenantId, source)`-keyed, closing the last documented gap from increment 8 |
| R36 | The management header said "showing the last successful publication" even though a failed/running sync commits Silver upserts as it goes — the live tables can mix that publication with a later run's partial writes (16 Sep 2026 maturity reassessment) | M | Readers can be shown a stale claim about data currency | `SourcePublicationStateViewModel.CurrentDataMayBePartial`/`PartialDataNote`: names the last complete publication, the failed run's stage, or (if none ever completed) says the figures are only what the failed run wrote | Eng | 7 | **Fixed** |
| R37 | Duplicate-email identity candidates resolved to the first dictionary match instead of being flagged; `ExternalIdentityLink` had no DB constraint stopping two links for the same external identity (16 Sep 2026 maturity reassessment) | M | Silent misattribution of work/bookings to the wrong person | `StaffIdentityResolver` refuses when more than one candidate matches (except a single active profile among inactive duplicates) and queues it as ambiguous; filtered unique indexes + `DuplicateIdentityLinkException` stop a conflicting second link | Eng | 7/8 | **Fixed** — tenant/source-account scoping of identity (R35) is now also closed, structurally |

## 3. Phase evidence

| Phase | What changed | Verification |
|---|---|---|
| 1 Baseline | Killed orphaned app process; fixed test ctor; added `SyncHubPlanner` gate test | build 0/0, 233 tests |
| 0 Deployment health | `ProductionConfigurationGuard`, `CorrelationIdMiddleware`, `DatabaseHealthCheck`, `/health` + `/health/ready`, per-IP rate limits, HSTS 1y, MFA cookie Secure, LocalDB → Development config, `appsettings.Production.json` | build 0/0, 250 tests; smoke: Production start refused; Dev `/health` 200, `/health/ready` "Healthy" 200, `X-Correlation-ID` echoed |
| 1 Security | (in Phase 0's `Program.cs` work) + every controller re-checked: all POSTs carry `[ValidateAntiForgeryToken]`; every action gates in-body; open-redirect safe (`Url.IsLocalUrl`) | controller gate tests (existing + new), smoke 5×400→429 |
| 2 Tenancy | `TenantStatus`/`TenantPlan`/`ProductFeature`; lifecycle columns migration; `TenantAccessPolicy`; `TenantContextAccessor` blocked state; `TenantAccessFilter`; `PlanEntitlements`/`IFeatureGate`/`[RequireFeature]`; onboarding stamps tenant; staff backfill; tenant-scoped Staff admin | build 0/0, 316 tests (`TenantAccessPolicyTests`, `PlanEntitlementsTests`, `TenancyFilterTests`, `StaffAdminControllerTests` cross-tenant NotFound) |
| 3 Product/admin | `Platform Admin` role; `StaffTenantAdminController` + view (create, status, plan, support signals); `/staffops/admin/audit`; nav gating | `StaffTenantAdminControllerTests` (Admin ≠ Platform Admin) |
| 4 Integration resilience | `TransientHttpRetryHandler`, `HttpFailureClassifier`, `SyncRunGuard`, `SyncFailed`/`RetentionPurgeFailed` audits, Bronze retention purge, 30s upstream timeout | build 0/0, 344 tests (`TransientHttpRetryHandlerTests`, `SyncRunGuardTests`, `ClickUpSyncServiceTests`) |
| 5 Governance | `data-governance.md` (classification, lifecycles, retention, DSR, audit matrix, runbooks); export includes audit trail; docs updated (`gdpr.md`, `tenancy.md`, `programme-ops.md`, `CLAUDE.md`, `README.md`) | `GdprServiceTests` audit-trail test |
| Gate | Live boot against LocalDB: all five migration plans applied (`Tenancy` reached `2026-09-tenancy-02` after fixing a T-SQL reserved-word column name that the first boot exposed), anonymous `/staffops*` → 302 to login, `/umbraco` 200, no leftover process | see §4 |
| 6 Trust foundation (GTM review B04/B05/B06/B08, 16 Sep 2026) | `PlannedAllocation` (+ `AddPlannedAllocationTable` legacy cleanup), `IStaffIdentityResolver`/`ExternalIdentityLink`/`UnresolvedIdentity` + identity queue page, `SyncRun`/`SyncLease` + `SyncRunCoordinator`/`SyncRunHandle` + `ISyncStatusQueryService` header panel, `TenantFeatureGate` fail-closed with `FeatureGateOutcome`, `IStaffDataParticipant` for GDPR export/erasure of identity links, retention for the new tables | build 0/0, **389 tests** (`HubPlannerSyncServiceTests`, `SyncRunCoordinatorTests`, `StaffIdentityResolverTests`, `SyncStatusQueryServiceTests`, `TenantFeatureGateTests`, `StaffIdentityControllerTests`, new cases in the overview/mapping/sync/GDPR/filter tests). Live boot: ProgrammeOps plan `…-09` → `…-12`, five new tables present, 0 error/fatal log events during the boot, `/health` and `/health/ready` 200, anonymous `/staffops/programme` and `/staffops/programme/identities` → 302, anonymous `/staffops/contracts` → 403 (gate now fails closed). Lease SQL (acquire / refuse while held / acquire after expiry / abandoned-run mark) and the sighting `COALESCE` match executed against LocalDB via sqlcmd, smoke rows removed. **Not exercised live:** an actual ClickUp/Hub Planner sync (no API token in this environment), so the run-state path end-to-end against SQL Server is covered by unit tests over the fake repository plus the SQL smoke, not by a real run |
| 7 Trust-work follow-through (R36/R37, three findings from the 16 Sep 2026 maturity reassessment) | Honest publication claims: `SourcePublicationStateViewModel.CurrentDataMayBePartial`/`PartialDataNote` replace "showing the last successful publication" on the overview and platform console. Lease-loss fencing: `ISyncRunRepository.HeartbeatAsync`/`CompleteAsync` now conditional UPDATEs on `(source, ownerInstanceId, runKey)` judged on the database clock (`SYSUTCDATETIME()`), `SyncRunHandle` throws `SyncLeaseLostException` and stops the sync (both `RunCoreAsync` loops now touch before every Silver write) on loss, `FailAsync`/`CompleteAsync` status-guarded so an abandoned marker is never overwritten. Identity ambiguity: `StaffIdentityResolver` refuses (queues, doesn't guess) when more than one candidate matches, with a single-active-profile exception for stale leaver duplicates; `AddIdentityLinkUniqueness` filtered unique indexes + `DuplicateIdentityLinkException` stop a conflicting second `ExternalIdentityLink` | build 0/0, **403 tests** (5 new `SyncRunCoordinatorTests` lease-loss scenarios, a `ClickUpSyncServiceTests` mid-run lease-loss case, 4 new `SyncStatusQueryServiceTests` partial-data cases, 7 new `StaffIdentityResolverTests` ambiguity/uniqueness cases, a `HubPlannerSyncServiceTests` ambiguous-resource case). Live boot: ProgrammeOps plan `…-12` → `…-13` applied cleanly, `/health` and `/health/ready` 200, `/staffops/programme`/`/staffops/programme/identities` → 302, `/staffops/contracts` → 403, 0 error/fatal log lines. Two SQL smokes against LocalDB, rows removed after: an 8-step two-instance lease-fence smoke (acquire, refuse-while-held, heartbeat-while-owner, expired-lease takeover, fenced heartbeat/complete/fail after takeover, release-after-takeover, new-owner completes) all matched expectation, and a link-uniqueness smoke confirming both filtered indexes exist and reject duplicate user-id/email pairs while still allowing multiple NULLs. **Not exercised live:** a real two-process concurrent sync against ClickUp/Hub Planner (no API token here) and tenant/source-account scoping of identity, which remains R35's job, not this pass's |
| 8 Tenant isolation (R12's ProgrammeOps half + R35, 18 Sep 2026) | `tenantId` on all 23 `ProgrammeOps_*` tables (nullable, backfilled to the default tenant); `IProgrammeRepository`/`IIdentityResolutionRepository`/`ISyncRunRepository`/`IAuditLogRepository`/both raw-payload repositories filter reads and stamp writes by it; `ProgrammeOverviewQueryService`/`ReportingQueryService` and every controller in the area resolve the caller's tenant and forbid if unresolved; per-key mutations scope by tenant (NotFound/no-op for a foreign key). Sync lease/run re-keyed `(source)` → `(tenantId, source)` (`RekeySyncLeaseByTenant`), so two tenants' syncs never serialize against each other; `SyncRunGuard`'s in-process latch stays source-keyed only — a narrow, documented, self-resolving gap, not a cross-tenant leak. `ProgrammeOps_SourceConnection` registry (structural B03): auto-provisioned per tenant per source, `ExternalIdentityLink.ConnectionKey` provenance; identity-link uniqueness indexes re-keyed tenant-qualified. Contract Ops' `ContractCommercialService`/`InvoiceGenerationService`/`PortfolioMarginService` thread the caller's tenant into their `IProgrammeRepository` reads so they don't mix another tenant's programme data in, though `ContractOps_*` itself stays untenanted | build 0/0, **405 tests** (two new `ProgrammeOverviewQueryServiceTenancyTests` cross-tenant-leak cases; every other ProgrammeOps/ContractOps/Integrations test updated for the new `tenantId` parameters, net test count otherwise unchanged). Live boot: ProgrammeOps plan `…-13` → `…-16` applied cleanly (`AddProgrammeTenantColumns`, `RekeySyncLeaseByTenant`, `AddSourceConnectionTable`), `/health` and `/health/ready` 200, `/staffops/programme`, `/staffops/programme/identities`, `/staffops/reporting`, `/staffops/platform/tenants`, `/staffops` → 302, `/staffops/contracts` → 403, 0 error/fatal log lines. **Not exercised live:** a real second tenant's ClickUp/Hub Planner sync against a live account (no API token here, and per-tenant credentials are explicitly out of scope for this increment — see R35), and per-tenant credential isolation itself, which remains open |
| 9 Tenant-safe pilot foundation (R12's Contract Ops/Approvals half, R13, R35's credential residual, 19 Sep 2026) | **Cross-tenant FK validation**: `ProgrammeRepository`'s ~12 FK-carrying Upsert/Create/Add methods now verify the referenced parent row belongs to the caller's tenant before writing (new `CrossTenantReferenceException`, mapped to `NotFound()` in `StaffReportingController`); `UpsertWorkItemAllocationsAsync`'s delete query is now tenant-scoped. **Per-tenant source credentials**: `ProgrammeOps_SourceConnection.protectedCredentialJson` (encrypted via `ISourceCredentialProtector`/ASP.NET Core Data Protection, purpose `ProgrammePulse.SourceConnection.Credential.v1`), resolved once per run by `ClickUpSyncService`/`HubPlannerSyncService` via `IClickUpApiClient.UseCredential`/`IHubPlannerApiClient.UseCredential`, deployment-wide fallback preserved for tenants that haven't configured their own; new Admin page `/staffops/programme/connections` (`StaffSourceConnectionController`) to set/clear a tenant's own ClickUp token+workspace or Hub Planner key, write-only, audited. `SyncRunGuard`'s in-process latch is now `(tenantId, source)`-keyed. **Contract Ops + Approvals tenant scoping**: `tenantId` added to all 6 `ContractOps_*` tables and `StaffOps_LeaveRequest`; `IContractRepository`/`IContractAuditLogRepository`/`ILeaveRequestRepository`/`ILeaveApprovalService` all thread `tenantId` (filter reads, stamp writes, FK-ownership checks on document/cost/invoice writes); `StaffContractController`/`StaffApprovalsController`/`StaffPortalController` resolve tenant and `Forbid()` if unresolved. **Architecture gate**: `docs/programme-ops.md` gained "Canonical domain" and "Provenance rules" subsections; new `SourceIndependenceDemonstrationTests` proves a third, hypothetical source (`FakeSyncSource`, not a new real connector) flows through a real `SyncSourceRegistry`/`SyncStatusQueryService` identically to the two real adapters, with zero source-specific code | build 0/0, **433 tests** (4 new real-database integration tests under `ProgrammePulse.Tests/Integration/` — first in this repo to hit a real LocalDB via `WebApplicationFactory<Program>` rather than a fake, proving the FK-tenant check against actual SQL Server behaviour; 4 new `SourceCredentialProtectorTests` against a real, disposable Data Protection key ring; 4 new credential-resolution demonstration tests in `ClickUpSyncServiceTests`/`HubPlannerSyncServiceTests`; 6 new `SourceIndependenceDemonstrationTests`; every Contract Ops/Approvals test updated for the new `tenantId` parameters). Live boot: ProgrammeOps plan `…-16` → `…-17` (`AddSourceConnectionCredentials`), ContractOps plan `…-03` → `…-04` (`AddContractOpsTenantColumns`), StaffOps plan `…-07` → `…-08` (`AddLeaveRequestTenantColumn`) all applied cleanly against the real dev LocalDB, `/health` and `/health/ready` 200, 0 error/fatal log lines. **Backup/restore rehearsal** (new this phase): `BACKUP DATABASE`/`RESTORE DATABASE` cycle against the just-migrated dev database, restored under a scratch name, confirmed all three new schema elements present (`ProgrammeOps_SourceConnection.protectedCredentialJson`, `ContractOps_Contract.tenantId`, `StaffOps_LeaveRequest.tenantId`) plus intact core Umbraco schema (`umbracoKeyValue` row count unchanged), rehearsal database and backup file then removed — see the runbook below. **Sync failure/recovery**: `ClickUp:WorkspaceId` is unset in this environment (no live account), which is itself the guard-clause case — `Missing_workspace_id_fails_before_taking_the_lease` and 36 other sync-lease/failure/recovery tests (`ClickUpSyncServiceTests`, `HubPlannerSyncServiceTests`, `SyncRunCoordinatorTests`, `SyncRunGuardTests`) all pass, confirming a misconfigured/failed sync never takes the lease or leaves a dangling `Running` row. **Not exercised live:** a real sync against a live ClickUp/Hub Planner account (still no API token in this environment) |

**Defect found only by the live boot:** the first cut named the plan column
`plan`, a T-SQL reserved word; the migration's `UPDATE` failed and the
Tenancy plan stopped at step 1 (DDL rolled back cleanly — verified via
`INFORMATION_SCHEMA.COLUMNS`). Renamed to `planName`; second boot applied
the step. This is exactly the class of failure unit tests over fakes cannot
catch — keep a boot smoke test in the release gate.

### Backup/restore runbook (rehearsed in Phase 9, 19 Sep 2026)

Rehearsed against the LocalDB dev instance (`(localdb)\GardnerDB`); the
same shape applies to any SQL Server instance, adjusting paths. Run after
every migration-bearing release, not only when this document says so:

```sql
-- 1. Find the live database's logical file names (needed for MOVE below —
--    a restore under a different database name can't reuse the same
--    physical file paths as the original).
SELECT name, physical_name FROM sys.master_files WHERE database_id = DB_ID('UmbracoBase');

-- 2. Back up.
BACKUP DATABASE [UmbracoBase] TO DISK = N'<path>\UmbracoBase_rehearsal.bak' WITH INIT, STATS = 25;

-- 3. Restore under a scratch name — never over the live database.
RESTORE DATABASE [UmbracoBase_rehearsal] FROM DISK = N'<path>\UmbracoBase_rehearsal.bak'
    WITH MOVE 'UmbracoBase' TO '<path>\UmbracoBase_rehearsal.mdf',
         MOVE 'UmbracoBase_log' TO '<path>\UmbracoBase_rehearsal_log.ldf',
         STATS = 25;

-- 4. Verify: schema present, core Umbraco tables intact.
SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('<a table from the latest migration>') AND name = '<its new column>';
SELECT COUNT(*) FROM umbracoKeyValue;

-- 5. Clean up the rehearsal copy (never the original).
ALTER DATABASE [UmbracoBase_rehearsal] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [UmbracoBase_rehearsal];
```

Phase 9's run: backed up the post-migration dev database, restored it as
`UmbracoBase_ReadinessRehearsal`, confirmed
`ProgrammeOps_SourceConnection.protectedCredentialJson`,
`ContractOps_Contract.tenantId` and `StaffOps_LeaveRequest.tenantId` all
present and `umbracoKeyValue` row count unchanged (12 rows), then dropped
the rehearsal database and deleted the `.bak` file. No data loss, no
schema drift between the live database and its restore.

## 4. Release gate

| Check | Result |
|---|---|
| Build passes | ✅ exit 0, 0 warnings |
| Tests pass | ✅ 344/344, exit 0 |
| App starts (Development, LocalDB) | ✅ `/health` 200 in ~6s; all migration plans current |
| App refuses an unsafe Production config | ✅ exits with `Refusing to start … umbracoDbDSN is empty` |
| Security baseline | ✅ HTTPS redirect, HSTS (non-dev), Secure/HttpOnly cookies, CSP on app routes, anti-forgery on every POST, per-IP rate limits, Admin TOTP MFA + recovery codes, in-body role gates on every action |
| Tenant boundaries enforced | ✅ Branding, Staff admin, onboarding, suspension, Reporting/Programme Overview/RAID/Governance/Alerts/Identity queue/sync state, Contract Ops, Approvals (R12, fully closed); ProgrammeOps source credentials tenant-isolated with a deployment-wide fallback (R35, fully closed); cross-tenant foreign-key writes refused, not just row-level filtered; practical tenant-aware admin/approver checks in place (R13, practically closed) — Member Groups themselves remain global, a documented post-launch item |
| Admin roles clear | ✅ four tenant roles + Platform Admin, all seeded; console gated on Platform Admin only |
| Monitoring present | ✅ liveness/readiness, correlation ids, structured Serilog; ⚠️ no alerting sink (R31) |
| Sync resilience acceptable | ✅ retry/backoff/429, failure audit, in-process guard + database lease with ownership fencing on takeover (R17), durable run state and publication freshness (R34), honest partial-data disclosure on failure/in-flight (R36), retention; ⚠️ synchronous request (R18), no live two-process test |
| Reporting semantics trustworthy | ✅ planned time never counts as delivery (R32); unmatched people visible and countable (R33); ambiguous identity matches queued rather than guessed (R37); coverage figures on workload/planned views; ⚠️ Hub Planner hour semantics unverified live (R21), no Bronze replay (R20) |
| Product lifecycle defined | ✅ Trial/Active/Suspended/Archived + plans, audited |
| Compliance gaps explicit | ✅ `data-governance.md` §8 |
| No secrets committed | ✅ |

**Release score: 8 / 10.**

**Recommendation: CONDITIONAL GO for a controlled, multi-tenant pilot** —
one hosting instance, a small number of named customer tenants provisioned
by the operator, Hub Planner verified against a live key first (R21),
`/umbraco` restricted at the edge (R24), demo dashboard removed or gated
(R25).

**Upgraded from a single-tenant to a multi-tenant pilot this phase**: R12
(Contract Ops + Approvals tenant scoping), R13 (practical tenant-aware
admin/approver checks) and R35's credential residual (per-tenant ClickUp/
Hub Planner credentials) are now closed, alongside the cross-tenant
foreign-key validation and backup/restore rehearsal this exit gate
required. Every tenant-scoped write in the application now verifies the
record it is acting on — not just the row it is creating — belongs to the
caller's tenant, and two tenants can each configure and sync their own
ClickUp/Hub Planner account without a shared token.

**NO-GO for open / self-service multi-tenant SaaS** until R14 (member→tenant
provisioning UI is still a manual SQL update) and R18 (syncs still run
synchronously inside the request) are closed, and until Member Groups
themselves become tenant-scoped rather than global (R13's deeper
architecture question, deliberately deferred — see `docs/tenancy.md`). None
of these block a small, operator-provisioned pilot with named tenants.

## 5. Next 30 days (post-launch backlog, in priority order)

1. **Verify Hub Planner against a live read-only key** (R21) — envelope
   shape and booking timezones; adjust `HubPlannerApiClient` if wrapped.
2. **Edge policy for `/umbraco`** (R24) and remove/gate `/opshwb` (R25).
3. **Move syncs off the request thread** (R18): queued background run,
   overview page polls `ProgrammeOps_SyncRun`; reuse `SyncRunCoordinator`.
4. ~~Database-backed sync lease with takeover fencing (R17)~~ — done
   (`ProgrammeOps_SyncLease`, `SyncLeaseLostException`).
4a. ~~Honest publication claims on failure/in-flight (R36)~~ and
    ~~identity ambiguity refused rather than guessed (R37)~~ — done.
4b. ~~Tenant-scope ProgrammeOps and tenant-owned source connections
    (R12's ProgrammeOps half + R35)~~ — done, structurally.
4c. ~~Tenant-scope Contract Ops and Approvals, isolate per-tenant
    ProgrammeOps credentials, close cross-tenant FK writes (R12's remaining
    half, R35's residual half, R13's practical half)~~ — done (increment 9,
    19 Sep 2026): `ISourceCredentialProtector` (ASP.NET Core Data
    Protection) + `/staffops/programme/connections`; `ContractOps_*`/
    `StaffOps_LeaveRequest` tenant columns; `CrossTenantReferenceException`
    on every FK-carrying ProgrammeOps write. **Still open**: Member Groups
    themselves are not tenant-scoped (R13's deeper architecture question —
    a tenant Admin is still, at the Member Group level, indistinguishable
    from an Admin of any other tenant; every *action* now checks the
    record's own tenant regardless, so this is a defence-in-depth gap, not
    a live one).
5. **"Move member to tenant" + "deactivate leaver"** actions on the
   platform/admin consoles (R14, data-governance §2).
6. **Audit login/MFA outcomes and role changes** (R30).
7. **Retention automation** (R22): a `RecurringHostedServiceBase` job for
   the staff-record and archived-tenant purges in `data-governance.md` §6.
8. **Integration test harness** for `MemberManager`-backed paths (R26):
   login, MFA challenge, tenant resolution — note `ProgrammePulse.Tests/Integration/`
   now has a working `WebApplicationFactory<Program>` real-database
   pattern (see the ProgrammeRepository tenant-isolation tests) that a
   `MemberManager` harness could extend.
9. **CI publish artefact + environment deploy job**, disable floating
   package versions (R27); alerting sink for `SyncFailed` and threshold
   alerts (R31).
