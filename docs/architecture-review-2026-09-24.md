# Architecture, design and structure review — 24 September 2026

## Summary

**The domain design is strong. The two layers around it, the web layer and the data-access layer, have not kept up with the number of feature areas. They are now what limits quality and scale.**

The deliberate rules are well chosen and mostly enforced by tests: Bronze/Silver/Gold layering, source neutrality, cost data kept structurally away from non-Admin paths, append-only assertions, and "declared, never inferred". Keep the modular monolith. The problems are in the plumbing that every area repeats:

1. **Queries load whole tables and aggregate in memory.** Gold and reporting services read every row a tenant has for each entity type on every page load. No `tenantId` column has a plain index. This is the main barrier to serving larger tenants.
2. **All work runs inside the HTTP request.** There are no hosted or background services. Syncs, evidence ingestion, retention purges, alert detection and snapshots all run in a user's request, and some of them only run as a side effect of one.
3. **Controllers do too much.** 135 imperative capability checks and 93 tenant-resolution calls (with 13 copies of the same private helper) are repeated by hand. Controllers call repositories directly and hold command logic. `StaffReportingController` is 831 lines.
4. **Tenant isolation depends on convention.** It is enforced by passing `tenantId` by hand into each repository method, plus tests. There is no structural guard, and 34 tables still have a nullable `tenantId`.
5. **Cross-cutting concerns are duplicated or missing.** There are five separate audit-log implementations and a logger in only 4 source files. There is no global exception handling, and in-process state (cache, rate limiter) will behave inconsistently once the app runs on more than one instance.

None of this is a correctness emergency today, at demo and pilot data volumes. All of it gets more expensive to fix with each new feature area, so it should be fixed before more areas are added.

This review complements, and does not repeat, [the 19 September architecture/GTM review](archive/architecture-gtm-review-2026-09-19.md) (release gates, security posture) and [the maturity reassessment](archive/solution-maturity-reassessment-2026-09-16.md) (product and GTM readiness). Where a finding overlaps with those (for example the durable sync worker), this document adds the code-level design detail.

## Scope and method

This was a static source review of commit `d38c85f` (branch `modernize/dotnet-20260923`, clean tree), covering 26 controllers, 225 service files, 56 NPoco DTOs, 58 migration files, 10 composers, 80 views, CI, and the test project (877 `[Fact]`/`[Theory]` methods; 1,086 test cases on the last full run recorded in [consolidation-2026-09-23.md](archive/consolidation-2026-09-23.md)). Counts below come from `grep`/`find` over the tree and can be reproduced.

**Not done:** load testing, runtime profiling, SQL execution-plan capture, or running the test suite. The scalability findings are inferred from query shape and index definitions, not measured. Phase 0 below exists to measure them before anything changes.

## What to keep

These are good decisions. The roadmap must not dilute them.

| Strength | Where it lives |
|---|---|
| Modular monolith split by feature area, one composer and one migration plan per area | `Composers/`, `Migrations/*` |
| Medallion layering with a single Bronze→Silver seam per vendor, and an architecture test that fails the build if Silver/Gold reaches a vendor namespace | `Services/Integrations/Abstractions`, `Tests/Architecture/SourceIndependenceTests.cs` |
| Cost/rate data unreachable on non-Admin paths (the fields don't exist in those view models, rather than being hidden) | `ProgrammeOverviewQueryService`, `ProgrammeOverviewViewModel` |
| Lease with fencing for syncs, judged on the database clock | `SyncRunCoordinator`, `SyncRunRepository` |
| Explicit `…AcrossTenants` naming for the few deliberate cross-tenant reads | `SyncRunRepository.cs:124-128` |
| `TimeProvider` injected rather than `DateTime.UtcNow` (5 stray uses left) | throughout |
| Fakes rather than a mocking framework; real-SQL `WebApplicationFactory` integration and persona tests | `ProgrammePulse.Tests/` |
| Startup guard that refuses unsafe production configuration; startup is pure and unit-tested | `Startup/ProductionConfigurationGuard.cs` |
| Central package management; Razor view type-check in CI | `Directory.Packages.props`, `scripts/check-views.ps1` |

## Findings

Severity reflects impact on scale and maintainability, not security. The security gates are covered in the 19 September review.

### A. Data access and query scalability

| # | Sev | Finding | Evidence | Consequence |
|---|---|---|---|---|
| A1 | **High** | Gold and reporting queries fetch every row a tenant has for each entity type, then filter and aggregate in LINQ. The Reporting Hub loads all programmes, projects, workstreams, work items, **time entries**, customers, staff and baselines on every request, even when the user asked for a one-month window. | `ReportingQueryService.cs:37-44`, `ProgrammeOverviewQueryService.cs:27-33`, `ContractCommercialService.cs:195-215`, `StaffReportingController.cs:689-696` | Memory and latency grow linearly with a tenant's *entire history*, not with the period being viewed. Time entries grow without limit. |
| A2 | **High** | No tenant-scoped table has a plain index on `tenantId`: 49 of 49 DTOs have none. The composite indexes on ProgrammeOps tables are **filtered** (`WHERE externalId IS NOT NULL`), so SQL Server cannot use them for a plain `WHERE tenantId = @0`, and manually created rows (null `externalId`) are not covered at all. | `Data/Dtos/*.cs`; `AddProgrammeTenantColumns.cs:94` | Every tenant read is a full table scan, so cost grows with the size of *all tenants combined*. That undermines the multi-tenant model the platform is moving towards. |
| A3 | **High** | Syncs write one row at a time, with at least five round trips per upstream task, each in its own scope: raw capture, lease touch, ownership check, lookup by external ID, then save. Allocations add more. | `ClickUpSyncService.cs:252-257`, `ProgrammeRepository.cs:212-230` | A 10,000-task workspace means tens of thousands of sequential round trips inside one HTTP request (see B1). Partial publication is also structural, because there is no batch transaction boundary. |
| A4 | Med | Lookups load a whole list to find a single row: `GetRisksAsync(tenant).FirstOrDefault(r => r.RiskKey == key)`. | `StaffReportingController.cs:415, 480` | Avoidable O(n) reads. There's also a small risk of a time-of-check/time-of-use race. |
| A5 | Med | `IProgrammeRepository` has 39 methods across roughly 20 tables and serves Silver writes, Gold reads and admin commands. | `Services/ProgrammeOps/IProgrammeRepository.cs` | A wide interface: every consumer and fake depends on all of it. This is also why read queries can't easily be tuned independently. |
| A6 | Med | 34 tables still have a nullable `tenantId` (legacy ProgrammeOps/Branding backfill). | `Data/Dtos/*` (`Guid? TenantId`) | Nulls can't be used safely in an index key or a future row-level-security predicate, and every consumer has to reason about the unscoped legacy case. |
| A7 | Low | Almost no pagination. List pages and audit views return everything. | Only a handful of repositories use `OFFSET`/paging | The page gets heavier as tenants age. |

### B. Execution model and scale-out

| # | Sev | Finding | Evidence | Consequence |
|---|---|---|---|---|
| B1 | **High** | Nothing runs in the background: no `IHostedService`, `BackgroundService` or Umbraco recurring job anywhere. All seven sync/ingestion paths (ClickUp, Hub Planner, Jira, Tempo, GitHub, Azure DevOps, Freshdesk) run in the request that triggered them. | `grep AddHostedService` returns nothing; `StaffProgrammeOverviewController.Sync`, `Staff*Controller.Sync` | Proxy and App Service request timeouts (about 230 s on Azure) put a hard ceiling on the size of tenant that can be synced. A restart or disconnect kills a sync partway through. (Already raised as P1 on 19 September; still open.) |
| B2 | **High** | Housekeeping only happens as a side effect of other work. The retention purge runs only after a *successful* sync, and it is global even though one tenant's sync triggers it. Alert detection runs on a **GET** of the Reporting Hub. | `ClickUpSyncService.cs:160`, `StaffReportingController.cs:71` | If syncs stop, or keep failing, raw payloads are never purged, which breaks the documented retention period ([data-governance.md](data-governance.md)). Alerts only exist if someone happens to open the page. |
| B3 | Med | Alert detection is check-then-insert with no uniqueness constraint. It also re-reads the full workstream and work-item tables that the same request has just loaded. | `AlertDetectionService.cs:53-76` | Two concurrent page loads can raise duplicate alerts, and the Reporting Hub reads its largest table twice. |
| B4 | Med | State that only works on one instance: the branding theme `IMemoryCache` (10-minute TTL, invalidated only on the local node) and the ASP.NET rate limiter (limits counted per instance). | `BrandingThemeResolverService.cs:20,47`, `Program.cs:42` | Once scaled out, a published theme or a rollback shows inconsistently for up to 10 minutes, and rate limits multiply by the number of instances. |
| B5 | Med | `AddDataProtection()` is registered inside a feature composer, as well as the central key-ring configuration in `Program.cs`. | `ProgrammeOperationsComposer.cs:69`, `Startup/DataProtectionKeyRingConfiguration.cs` | The result is correct only by call order. Cross-cutting infrastructure belongs in one place. |
| B6 | Med | `CancellationToken` appears in only 29 of 225 service files. Most repository and query methods can't be cancelled. | `grep CancellationToken Services` | Abandoned requests keep running their full-table reads. |
| B7 | Low | Umbraco load-balancing roles (scheduling publisher vs subscriber) aren't configured. This matters as soon as background jobs exist (B1): they must run on exactly one node. | `appsettings*.json` | A prerequisite for Phase 3. |

### C. Web layer design

| # | Sev | Finding | Evidence | Consequence |
|---|---|---|---|---|
| C1 | **High** | Authorization and tenant resolution are written out by hand in almost every action: 135 `HasAsync`/`IsMemberAuthorizedAsync` checks, 93 `ResolveTenantAsync()` calls, and 13 identical private `ResolveTenantAsync` helpers. Only 4 controllers use a declarative attribute (`[RequireFeature]`). | `Controllers/*.cs` | Each new action is one forgotten `if` away from a permissions bug. Reviewers have to read every method body to see who can reach it. (The persona tests catch this after the fact, which is good, but it should be impossible by construction.) |
| C2 | **High** | 18 controllers inject repositories directly and contain command logic: they build domain records, stamp timestamps and catch `CrossTenantReferenceException`. | `StaffReportingController.cs:376-395` and similar | Business rules are split between controllers and services, can only be tested through HTTP, and are duplicated when a second entry point (API, MCP, job) needs them. |
| C3 | Med | Oversized controllers: `StaffReportingController` (831 lines, roughly 8 unrelated concerns: hub, alerts, RAID, governance, baselines, change requests, stakeholders, customers), `StaffServiceController` (567), `StaffSkillsController` (518), `StaffEvidenceConnectionController` (514). | `Controllers/` | A wide blast radius for reviews and permission changes (also flagged on 19 September). |
| C4 | Med | No global exception handling: no `UseExceptionHandler`, `ProblemDetails` or `IExceptionHandler`. Domain exceptions (`CrossTenantReferenceException`, 5 per-area `*ValidationException`s, 54 `InvalidOperationException`s in services) are translated by hand with try/catch in each action. | `Program.cs`; `Controllers/*` | Error handling varies by action, and any exception an action doesn't catch becomes a raw 500. |
| C5 | Med | Weakly typed views: 114 `ViewData[...]` assignments, 57 hard-coded `Redirect("/staffops/...")` paths, and `ModelState` used in only 14 places (invalid input is mostly ignored silently, e.g. a blank risk title does nothing). | `Controllers/*` | Renaming a route or property breaks things at runtime, not at compile time. The Razor check can't see through `ViewData`. |
| C6 | Low | Only 23 of 80 views are localised, and views carry 42 inline `style=` attributes. | `Views/` | Tracked product work, not architecture. It's listed here because a shared view-component and layout convention (Phase 1) makes finishing it cheaper. |

### D. Structure, boundaries and cross-cutting concerns

| # | Sev | Finding | Evidence | Consequence |
|---|---|---|---|---|
| D1 | Med | Feature areas have a dependency **cycle**: Staff → ProgrammeOps (4 files) and ProgrammeOps → Staff (9). SkillsEvidence (7), ContractOps (6), ServiceOps (2) and ExecutiveReview (1) all import ProgrammeOps, largely for shared types such as `CrossTenantReferenceException`. | Coupling matrix: `using ProgrammePulse.Services.*` per area | ProgrammeOps has become an accidental shared kernel. The areas can't be extracted or reasoned about independently, which undermines the ServiceOps rule that "a desk-only customer is a real configuration". |
| D2 | Med | Five separate audit-log implementations (StaffOps, ProgrammeOps, BrandingOps, ContractOps, SkillsEvidence; ServiceOps and ExecutiveReview have their own event tables too), each with its own DTO, repository and shape. | `Data/Dtos/*AuditLogDto.cs` | Each new area builds another one. There's no single place to answer "what did this person do" across the platform, which matters for DSR and security investigation. |
| D3 | Med | Observability is thin. An `ILogger` is injected in only **4** source files. Operational state is written to audit/run tables instead. OpenTelemetry is configured, but the services emit no custom metrics or activities. | `grep ILogger Services Controllers` | In Azure you can see *that* a request was slow, but not which sync stage, how many rows were read, or which tenant's query caused it. |
| D4 | Med | Tenant isolation is enforced by convention (a `tenantId` parameter on each repository method), backed by tests but with no structural guard. Several methods are keyed only by a global ID (`GetBackupsForStaffAsync(staffKey)`, `GetAgentLinksForStaffAsync(staffKey)`, invoice lines by `invoiceKey`), which relies on the caller having checked ownership first. | `ContinuityRepository.cs:89`, `ServiceOpsRepository.cs:334`, `ContractRepository.cs:166` | This is correct today. However, nothing prevents the next repository method from leaving out the predicate, and no test would detect it unless someone writes a cross-tenant case for that exact method. |
| D5 | Low | The migration-plan rule is broken: the ExecutiveReview tables are migrated by the **Tenancy** plan (`2026-09-tenancy-03..05`). | `Migrations/Tenancy/TenancyMigrationPlan.cs:12-14` | ExecutiveReview can't upgrade or fail independently of tenancy, and the CLAUDE.md rule is misleading. Don't move existing steps, because plan state is keyed by name. Start an `ExecutiveReview` plan for future steps. |
| D6 | Low | Only 2 of the options classes use `ValidateOnStart` or data annotations. | `Composers/`, `Startup/` | A bad value in `ProgrammeOps:*`, `SkillsEvidence:*` and similar sections fails at first use rather than at boot. |
| D7 | Low | Documentation and naming drift: CLAUDE.md says Umbraco 18.1.0 (the packages are 18.2.0), its composer list leaves out five composers and it doesn't mention Jira, Tempo, Azure DevOps or Commercial. The test project is still named `ProgrammePulse.Tests` and sits inside the web project folder. The anonymous `/opshwb` demo still ships. | `CLAUDE.md`, `Directory.Packages.props`, `ProgrammePulse.csproj` | Contributors, human or agent, act on outdated guidance. (`/opshwb` is already tracked as R25.) |
| D8 | Low | One project holds everything: the host, all 10 areas, integrations and views. Module boundaries are enforced only by namespace and two architecture tests. | `ProgrammePulse.csproj` | Acceptable for now. Split only when a trigger fires (see Phase 5). |

## Target shape

This is not a rewrite. It keeps the same monolith, with four structural additions:

```text
 Web (Umbraco host)
   Controllers ── [RequireCapability] + TenantScope filter ── thin: bind → call → map
        │
 Application services (per area)  ← commands and queries; own validation, timestamps, audit
        │                    │
   Read models (Gold)   Write repositories (Silver, per aggregate)
   SQL-side filtering,  batched upserts, one transaction per page
   date-scoped, paged
        │                    │
   SharedKernel: TenantId, CrossTenantReferenceException, IAuditWriter, Result/validation
        │
   Jobs: durable JobQueue table → hosted worker (publisher node only)
         sync · ingestion · retention · alert detection · snapshots · run reconciliation
```

## Improvement roadmap

Phases are ordered by dependency and by risk reduction per unit of effort. Durations are rough estimates of engineering time for one developer and should be refined after Phase 0. Each phase ends with evidence, following the repository's usual pattern of exit criteria.

```text
Phase 0  Guardrails and baseline      ██              ~1–2 wks
Phase 1  Web layer consolidation        ███           ~2–3 wks
Phase 2  Data access and queries           ████       ~3–4 wks   ← largest scale win
Phase 3  Background execution + scale-out     ████    ~3–4 wks   (needs 2's batching)
Phase 4  Operability + cross-cutting        ██ ║ ██   ~2 wks, runs alongside 2–3
Phase 5  Modularisation                               trigger-based, not scheduled
```

### Phase 0: guardrails and baseline (~1–2 weeks)

**Goal:** make the implicit rules machine-checked and *measure* the scalability findings before changing anything.

| Work item | Addresses | Size |
|---|---|---|
| **Scale baseline fixture**: a SQL-backed integration test that seeds one large tenant (e.g. 20k work items, 250k time entries, 5 years) plus one small tenant, then records timings and query counts for the Programme Overview, Reporting Hub, contracts overview and one ClickUp sync against a fake client. Save the results under `artifacts/`. | A1–A3, B1 | M |
| **Shared kernel**: move `CrossTenantReferenceException`, the validation exception base and tenant primitives into `Services/Shared` (or `SharedKernel`). Add an architecture test for **no cycles between feature-area namespaces**. | D1 | S |
| **Tenant predicate architecture test**: scan repository source for `Sql.Builder.Where(...)` and `FROM [..]` over tenant-scoped tables. Each must contain `tenantId`, or come from a method named `*AcrossTenants`, or be on an allowlist with a documented ownership precondition. | D4 | S |
| **Controller gate test**: every action on a `Staff*` controller is covered by a capability gate. Allowlist the anonymous endpoints (account, health, language). Replace the heuristic with the attribute check once Phase 1 lands. | C1 | S |
| Update CLAUDE.md (versions, composers, areas). Record the decision on `/opshwb`. | D7 | S |

**Exit criteria:** the baseline numbers are recorded, the new architecture tests are green (or have explicit allowlists), and the full suite is green.

### Phase 1: web layer consolidation (~2–3 weeks)

**Goal:** make controllers thin and declarative, so permissions are visible from an action's signature and business rules live in services.

| Work item | Addresses | Size |
|---|---|---|
| `[RequireCapability(Capability.X)]` async authorization filter backed by the existing scoped `IStaffAuthorizationService` (reusing its per-request profile cache). Keep imperative checks only for *mixed* decisions such as "Admin sees cost". | C1 | M |
| `TenantScope` filter that resolves `ITenantContext` once and returns 403 if unresolved, plus a `TenantId` action-argument binder. Delete the 13 private helpers. Leave `TenantAccessFilter` as is; it already handles the blocked case. | C1 | S |
| Global exception mapping: an `IExceptionHandler` plus MVC exception filter mapping `CrossTenantReferenceException`→404, `*ValidationException`→ModelState/400 and concurrency conflicts→409, and a consistent error view. Remove the per-action try/catch. | C4 | M |
| Move command logic into application services (`RaidService.CreateRiskAsync`, `ChangeRequestService…`). Controllers stop injecting `I*Repository`. Do this area by area, starting with Reporting. | C2 | L |
| Split `StaffReportingController` into Hub, Alerts, Raid, Governance and Customers controllers with **unchanged routes**, relying on the persona and render tests as the safety net. Then do the same for Service, Skills and EvidenceConnection if they are still over roughly 400 lines. | C3 | M |
| Typed view models in place of `ViewData`. Use `RedirectToAction`/named routes in place of literal paths. Add input validation attributes and `ModelState` feedback on command forms. | C5 | M |
| Take alert detection off the GET path (it moves to a job in Phase 3; until then, run it after sync completion). | B2, B3 | S |

**Exit criteria:** zero `ResolveTenantAsync` helpers and zero repository injections in controllers; the controller gate test uses attribute inspection; persona and render suites unchanged and green.

### Delivery record: Phases 0 and 1 (24–25 September 2026)

**Both phases are complete.** The work is on branch `modernize/dotnet-20260923`. The first slice (the declarative gates) was committed as `5363117`; everything after it is uncommitted for review.

#### Evidence

| Checkpoint | Result |
|---|---|
| Baseline before any change | 1,206 passed, 1 failed. The failure was a test-layout bug: `appsettings.json` was found by a fixed `../../../..`, which broke under `--artifacts-path`. Fixed. |
| After Phase 0 | 1,257 passed, 0 failed |
| Midpoint of Phase 1 | 1,278 passed, 0 failed |
| **End of Phase 1 (full SQL-backed suite, Release)** | **1,287 passed, 0 failed, 0 skipped** |
| Razor type-check | All 80 views compile |
| Real boot against LocalDB | `/health` and `/health/ready` 200; every gated page refuses anonymous callers exactly as before; `theme.css` and login 200; `/error` renders 500 with the caller's correlation id; 0 error events in the boot log |
| Mutation checks | Removing a capability gate, a tenant predicate, or the new audit scoping each fails its guard test |

#### Phase 0: guardrails and baseline

- **Scale baseline** (`Integration/ScaleBaselineTests`, opt-in with `PP_RUN_SCALE_BASELINE=1`). It seeds two tenants with identical current-month activity but 1× and 5× history, then times the Gold entry points. Evidence: `artifacts/scale-baseline/`.

  | Gold entry point | 1× history | 5× history |
  |---|---|---|
  | Cost Summary | 176 ms | 701 ms (**4.0×**) |
  | Delivery Load | 241 ms | 858 ms (**3.6×**) |
  | Reporting Hub | 975 ms | 969 ms, but allocation 122 → 371 MB |
  | Programme Overview | 75 ms | 105 ms |

  Finding A1 is confirmed for Cost Summary and Delivery Load. The Reporting Hub costs about 1 s even at 1× history, so something other than history depth dominates it. Phase 2 should profile it first.
- **Shared kernel** (`Services/Shared`). `CrossTenantReferenceException` moved here. `CommandOutcome` and `CommandResult` live here too.
- **No cycles between feature areas** (`Architecture/FeatureAreaDependencyTests`). This found and removed three cycles:
  - Staff ⇄ ProgrammeOps, caused by where the shared exception lived.
  - Staff ⇄ Tenancy. `StaffOnboardingService` read `ITenantContext` and *defaulted to the platform tenant when none was resolved*, which was fail-open. The tenant is now passed in.
  - Security ⇄ Staff. MFA was split across the two areas. `IMfaRepository` moved to Security, and Staff defines an `IStaffMfaAdministration` port that Security implements.
- **Tenant predicate guard** (`Architecture/TenantPredicateTests`). Every method that filters, mutates or reads rows must mention `tenantId` or be listed with a reason: platform table, one-person erasure, retention purge, or a key the caller has already proven. Writing it removed an unused, unscoped `IAuditLogRepository.GetLatestAsync`. Its second version, which also inspects plain reads, found the leak described below.
- CLAUDE.md is corrected: versions, composers, feature areas, and the ExecutiveReview migration-plan exception is now on record.

#### Phase 1: web layer

- **Declarative gates.** `[RequireCapability]` is an action filter placed so that refusal precedence is unchanged. `[CurrentTenant] Guid tenantId` is bound only from the tenant context, never from the request; a real-HTTP persona test proves `?tenantId=` cannot select another tenant. Global `CrossTenantReferenceExceptionFilter` → 404. `Architecture/ControllerGateTests` requires every `/staffops` action to declare a gate or appear in a listed exemption.
- **One current-caller service** (`ICurrentStaff`). It replaced 14 duplicated helpers. The authorization service and tenant resolution share it, so each request does one member lookup and one staff lookup.
- **No repositories in controllers.** This was 17 controllers and about 180 repository calls. The logic moved verbatim into application services: `RaidService`, `GovernanceService`, `PortfolioAdminService`, `ProgrammeSyncService`, `MyWorkQueryService`, `LeaveQueryService`, `IdentityQueueService`, `SourceConnectionAdminService`, `BrandingAdminService`, `ContractAdminService`, `TenantAdminService`, `StaffAdminService`, `AuditTrailQueryService` (in its own top-level `Services/Audit` area), `AzureDevOpsConnectionService`, `GitHubConnectionService`, `EvidenceSourceAdminService`, `ContinuityPageService`, `SkillsPageService`, `DeskAdminService`, `FreshdeskConnectionService` and `SupportParticipationQueryService`. `SignInEligibility` and `MfaVerificationService` now cover the sign-in path. `Architecture/ControllerLayerTests` keeps it this way.
- **`StaffReportingController` split** (831 lines) into Reporting, ReportingAlerts, Raid, Governance and PortfolioAdmin controllers (389 lines in total). `Architecture/RouteContractTests` snapshots all `/staffops` endpoints: verb, URL, capabilities, tenant and antiforgery. The split changed none of them. The only intended contract changes were one new endpoint and three OAuth actions becoming *declared* tenant-scoped.
- **Typed view models.** `ViewData` now carries page chrome only (`Title`, `Message`, `ErrorKey`), enforced by a test. About 40 data entries became typed page models.
- **Redirects and view links are checked.** `Architecture/ViewLinkTests` resolves every `asp-controller`/`asp-action` pair and matches every literal `/staffops` redirect against the route contract. This matters because a stale tag helper renders an empty link rather than failing the build.
- **Validation feedback.** A blank risk, issue or change-request title, a blank customer name, or a stakeholder without a person is now refused with a message. Previously these were silently skipped.
- **Alert detection is off the GET path.** It runs across the whole tenant after each successful programme sync, and on demand ("Refresh alerts"). It used to run on every Hub view, against only the viewer's own team. A detection failure after a published sync is logged, not reported as a failed sync.
- **Unhandled exceptions** outside Development go to a self-contained error page that shows the correlation id (`Startup/ErrorPagePolicy`, `ErrorController`).

#### Defects found and fixed along the way

| Defect | Impact | Fix and guard |
|---|---|---|
| **Cross-tenant disclosure on `/staffops/admin/audit`.** `StaffOps_AuditLog` has no tenant column, and the page read the latest 100 rows platform-wide. | Any tenant's Admin saw other organisations' new-staff emails, **cost-rate changes**, GDPR erasures and platform tenant actions. | Entity-scoped query: rows for people on the tenant's roster plus Tenant rows for that tenant itself (`GetRecentForTenantAsync`). The unscoped method is deleted. Covered by the SQL test `StaffAuditTenantIsolationIntegrationTests`, which fails against the old query, and by the read check in `TenantPredicateTests`. |
| Onboarding defaulted an unresolved tenant to the platform tenant | Fail-open placement of a new person | The tenant is required and passed by the caller |
| Jira and Tempo credential storage was not audited | ClickUp and Hub Planner credential changes were audited; OAuth ones were not | `SourceConnectionAdminService` audits every source the same way |
| Audit detail JSON was built by string interpolation (Contract Ops, tenant console, staff admin) | A quote in a reference or file name wrote malformed JSON into the audit trail | `JsonSerializer`, with the same property names |
| Leave approval queue made one staff lookup per request (N+1) | Queue cost grew with its length | One roster read per queue |

#### Left open, deliberately

- Portal and Skills self-service home pages, the ProgrammeOverview `Sync` action and the OAuth callbacks keep imperative gates, each for a stated reason recorded as an exemption.
- Per-area validation exceptions are still translated per action, because each re-renders its own form with its own message. That is correct behaviour, not debt.
- The `/opshwb` demo is still shipped. **This is a product decision for you**, recorded as open.
- `Refresh alerts` has a Welsh string, "Adnewyddu rhybuddion", that needs the professional Welsh review already outstanding.
- Duplicate alerts under concurrency (B3) and all query shape and indexing work are Phase 2.

### Phase 2: data access and query scalability (~3–4 weeks)

**Goal:** a page's cost scales with what it shows, not with the tenant's history or the platform's total size.

| Work item | Addresses | Size |
|---|---|---|
| **Indexes (one migration step per area plan):** a non-filtered `(tenantId)` index at minimum on every tenant table. Add covering composites where there are range queries: `TimeEntry(tenantId, entryDate) INCLUDE (staffKey, workItemKey, hours)`, `WorkItem(tenantId, workstreamKey)`, `PlannedAllocation(tenantId, startDate)`, `AuditLog(tenantId, timestampUtc)`, raw payloads `(capturedAtUtc)` for purges, and evidence/support facts by `(tenantId, occurredAtUtc)`. Verify against the Phase 0 plans. | A2 | M |
| **Make `tenantId` non-nullable** on the 34 legacy tables. First run a pre-flight migration step that fails loudly if any null rows remain, rather than defaulting them. Rehearse against a restored copy of the database. | A6 | M |
| **Push filtering into SQL.** Add date- and scope-bounded read methods (`GetTimeEntriesAsync(tenant, from, to, programmeKey?)`), `Get*ByKeyAsync` point lookups, and SQL `GROUP BY` aggregation for hours per staff/workstream/customer where Gold only needs totals. Keep complex rules in C# where they are the product, e.g. the delivery-load anti-inversion rules. | A1, A4 | L |
| **Split repositories into reads and writes, not one per entity** (this keeps the deliberate `ProgrammeRepository` precedent). Silver write repositories per aggregate cluster, plus `I*ReadModel` query interfaces for Gold. `IProgrammeRepository` becomes roughly three focused interfaces. | A5 | M |
| **Batched sync writes.** Per list or page: preload an `externalId → key` map in one query, upsert in one scope and transaction, and bulk-insert raw payloads. Touch the lease once per batch, not once per row. This also turns "partial publication" into "complete up to page N". | A3 | L |
| Unique filtered index `Alert(tenantId, type, entityKey) WHERE status = 'Open'`, with insert-if-absent. | B3 | S |
| Paging on audit, identity-queue, evidence and alert list views. | A7 | M |
| `CancellationToken` through repository and query signatures (mechanical, do it while touching them). | B6 | M |

**Exit criteria:** the Phase 0 baseline re-run shows Reporting Hub and Overview times flat when history grows 5× with a fixed viewing window; sync round trips per task are under 1 on average; no plan regressions in the persona suite.

### Delivery record: Phase 2 (25 September 2026)

**The page-timing exit criterion is met by measurement, and the sync round-trip criterion by analysis of the code paths (no SQL command count was captured). Three work items remain partly or wholly open: non-nullable `tenantId` (A6), which needs a rehearsal this environment cannot provide; indexing (A2), done for the hot ProgrammeOps tables only; and batched writes for Hub Planner, Jira and Tempo (ClickUp is done).** The work is on `modernize/dotnet-20260923`; paging and cancellation (25–26 September) are folded into this record.

**Root cause first.** The baseline now times the individual reads each page is built from. Materialising every time entry the tenant has ever logged took 697 ms and 302 MB at 5× history. That one read dominated Reporting Hub, Cost Summary and Delivery Load. The Hub's roughly 1 s at 1× history was that read, plus the overview, plus about 120 per-person round trips. The missing tenant-leading indexes were the second cause, not the first.

| Gold entry point (current month) | Before, 5× history | After, 1× history | After, 5× history |
|---|---|---|---|
| Reporting Hub | 1,272 ms, 369 MB | 276–460 ms¹ | 264 ms, 65 MB |
| Cost Summary | 971 ms, 305 MB | 32 ms | 26 ms, 9 MB |
| Programme Overview | 106 ms | 89 ms | 76 ms |
| Delivery Load | 1,136 ms, 352 MB | 150 ms | 333 ms, 28 MB² |

¹ The 1× Hub run goes first and includes warm-up; its minimum is shown alongside the median. ² See the open decision below.

**Sync writes** were measured on LocalDB, where a round trip is nearly free:

| Operation | Single-row calls | Batched |
|---|---|---|
| 1,000 tasks with 2 assignees, insert | 2,096 ms | 736 ms |
| 1,000 tasks with 2 assignees, re-sync | 2,273 ms | 602 ms |
| 1,000 time entries, insert | 1,038 ms | 368 ms |
| 1,000 time entries, re-sync | 980 ms | 309 ms |

By analysis of the code paths, round trips per task fall from about 7–9 to about 0.05, so the saving grows with network latency.

**What changed:**

- **Bounded reads** on the new `IProgrammeReadRepository`:
  - A period read that seeks `(tenantId, workDate)`. `workDate` is now defined as the entry's `ReportDate`, and one shared `Apply` writes it on every path.
  - Per-work-item logged hours computed with SQL `GROUP BY`, optionally for one person, which My Work uses.
  - Weekly distinct-project counts for Delivery Load, grouped in SQL with a `DATEFIRST`-independent ISO week.
  - Coverage facts: undated hours and whether any Tempo entries exist.
  - Point lookups by key: A4 is fixed for risks, issues and work items.
  - The calibration guards now read only the one work item.
- **Every bounded read has a body on the interface.** The body is the definition, written over the plain list reads, and the fakes use it. `Integration/BoundedReadIntegrationTests` proves the SQL overrides agree with those definitions, including an entry dated only by a start just before midnight, undated rows, and another tenant sharing a person's key.
- **Batched person reads** for rate history, work-hours history and availability, using `IN` lists chunked under SQL Server's parameter limit (`SqlInList`). This removes the N+1 in contributor capacity, Cost Summary, Budget, Contract and Invoice.
- **The contract portfolio summary** walks the tenant's programme graph and time entries once per request, not once per contract.
- **Indexes** (`AddTenantReadIndexes`, ProgrammeOps step 22):
  - 12 tenant-leading indexes.
  - The TimeEntry period index covers every column the read selects.
  - The migration backfills `workDate` first.
- **Read/write split.** Ten read-only services now take `IProgrammeReadRepository`. `Architecture/ProgrammeRepositoryWriterTests` keeps an allow-list, with reasons, of the nine types that may take the writable repository, and requires every `*QueryService` to take the read half.
- **Batched sync writes (A3):**
  - `UpsertWorkItemsAsync` (with allocations) and `UpsertTimeEntriesAsync` run as one set-based transaction per batch through `SqlMultiRow` (multi-row `INSERT`, and an `UPDATE … FROM (VALUES …)` join that carries the tenant predicate).
  - A batch that names another tenant's parent fails whole, before anything is written.
  - ClickUp captures Bronze, touches the lease and writes Silver per 200-row batch. That keeps the "touch before every Silver write" rule, now per batch.
  - `Integration/BatchedSyncWriteIntegrationTests` is differential: it replays the same upserts through the single-row path in one tenant and the batched path in another, then compares every persisted field. The replay includes a re-upsert, a duplicate within one batch, a row with no external id, and enough rows to cross statement chunks.
- **Duplicate open alerts (B3)** are now impossible:
  - `UX_ProgrammeOps_Alert_open` enforces it (step 23). The migration first acknowledges existing duplicates rather than deleting them.
  - `RaiseAlertAsync` inserts under a key-range lock and returns the open alert that already exists.
  - A test races eight concurrent raises against real SQL.
- **Paging (A7)** on the alert list, the audit trail, the identity queue, the unidentified-account queue and each person's evidence portfolio:
  - `PageRequest` and `ResultPage<T>` (in `Services/Shared/Paging.cs`) fetch one row past the page, so there is no `COUNT(*)` over the whole list. `SqlPaging.ForPage` appends `OFFSET … FETCH` to an order that ends on a unique column, so rows never repeat or vanish between pages. One `_Pager` partial renders every list and keeps the other query-string values.
  - Pages that only needed a number now count in SQL: the Hub's open-alert badge, the sync's unresolved-identity count, and the evidence coverage and readiness "unidentified accounts" figures.
  - A person's evidence portfolio shows one page, but its totals (count per role, language hints, earliest and latest date, and "absence is inconclusive") come from a SQL summary of the whole record. Before, they came from reading every artefact.
  - The audit trail merges four separately stored logs, so page *n* reads the newest skip + size rows of each. Paging stops at 2,000 entries, and the page says when that hides older ones.
  - `Integration/PagingIntegrationTests` walks every page of each list at a size of three and requires the stitched result to equal the full list in order. It also checks each SQL count and summary against the definition it replaced.
- **Cancellation (B6)** on the heavy read path:
  - The Reporting Hub, Cost Summary, CSV exports, Programme Overview, Delivery Load, My Work and Alerts actions take the request's token, and it reaches NPoco through the report services, `IProgrammeReadRepository` (every read), the staff roster and the batched per-person reads.
  - `Architecture/CancellationTokenTests` fails the build if a read on those interfaces lacks a token, or if a repository method accepts one and does not pass it on. `BoundedReadIntegrationTests` shows a cancelled token stops both a repository read and a whole Hub build at the database.
  - Scope, stated plainly: writes, and the other feature areas' reads, do not take a token yet.
- **Defect found while doing this:** rate and contracted-hours history were ordered only by `effectiveFromUtc`, which is `datetime` (about 3 ms). Two changes in quick succession tie, and SQL Server returned them in either order; one integration test failed intermittently because of it. Costing was unaffected, because the older row's effective range is empty. Both reads now break ties on `id`, and a test forces a three-way tie.

**Evidence:**

| Check | Result |
|---|---|
| Full SQL-backed suite (Release) | 1,307 passed, 0 failed, 0 skipped |
| Integration and persona suite on a brand-new database (all 23 ProgrammeOps migrations from empty) | 266 passed |
| Plan state and indexes on the fresh database | `2026-09-programmeops-23` recorded; all 13 new indexes present |
| Real boot on the fresh database | `/health` and `/health/ready` 200; every changed page, including with `?page=`, refuses anonymous callers as before; 59 log events, 0 errors |
| Razor check | All 81 views compile |
| Build | 0 warnings |
| Mutation checks | Caught by the new tests: shifting the ISO week anchor; dropping the tenant predicate from the coverage query; removing the alert insert guard (the concurrent raises then hit the unique index); swapping two columns in the batched work-item and time-entry values; fetching exactly one page instead of one row past it (all four paging tests fail); dropping one repository call's token; removing the rate-history tie-break |

**Left open, deliberately:**

- **Delivery Load still grows with history** (150 → 333 ms), although it now allocates 28 MB instead of 352 MB. Its baseline is "the last 8 weeks *that have data*", which has no fixed lookback, so the SQL aggregate must scan all dated history. Bounding the lookback (for example to 52 weeks) would make it flat, but it changes what the page means for a sparse logger. **That is a product decision for you.**
- **Non-nullable `tenantId` (A6).** The plan requires rehearsing this against a restored copy of the real database, which is not available here. It also has to drop and recreate every index containing `tenantId`, including the 12 added in this phase. It should be its own reviewed step.
- **Cancellation beyond the report path.** Writes and the other feature areas' reads (Skills, Service Ops, Contracts documents, Branding) still take no token. Extend it area by area, and add each interface to `CancellationTokenTests`.
- **The Welsh pager strings** ("Tudaleniad", "Tudalen flaenorol", "Tudalen nesaf", "Tudalen {0}") join "Adnewyddu rhybuddion" in the professional Welsh review.
- **Indexing (A2) outside the hot ProgrammeOps tables.** `AddTenantReadIndexes` covers the 12 indexes this phase's reads needed. The roadmap asks for a tenant-leading index on every tenant table, plus retention indexes (raw payload purges) and evidence/support fact indexes across areas. That needs a per-area inventory first, and a migration step in each area's own plan.
- **Hub Planner, Jira and Tempo still write one row at a time.** The batched repository methods are ready for them.
- **The sync round-trip figure (about 0.05 per task) is analysis, not measurement.** The measured numbers are the LocalDB elapsed times above. Counting SQL commands per sync (e.g. an NPoco interceptor in the scale baseline) would turn it into evidence.
- **Budget burn and contract costing still read all of a tenant's time entries.** They price lifetime cost at the rate in force on each entry's date. Now each does it once per request rather than once per contract, but bounding them needs a SQL costing aggregate.
- **Observation, unchanged:** invoice generation selects entries by UTC start date, so an entry with only a provider work date (Tempo) is never invoiced, while contract costing does include it. The bounded read keeps that behaviour exactly. Whether it is intended is for you to decide.

### Phase 3: background execution and scale-out (~3–4 weeks)

**Goal:** no user request does unbounded work, and the app is safe to run on two or more instances. This builds on the target design in the [19 September review, §3](archive/architecture-gtm-review-2026-09-19.md#3-ingestion-publication-and-data-meaning), and Phase 2's batching makes each job step bounded.

| Work item | Addresses | Size |
|---|---|---|
| **Durable job table** `Platform_Job(tenantId, kind, source, idempotencyKey, status, attempts, notBefore, leaseOwner, …)` in its own `Platform` migration plan. All seven sync/ingestion triggers enqueue and return **202** plus a run URL. The existing status UI reads the job and `SyncRun` rows. | B1 | L |
| **Hosted worker** (`BackgroundService` with a fresh DI scope per job, reusing `SyncRunCoordinator` leases) with bounded concurrency per tenant and per source, and retries with backoff and a dead-letter state. Add a restart test: kill mid-job, then the job resumes or is explicitly failed. | B1 | L |
| **Scheduled jobs**, via Umbraco `IRecurringBackgroundJob` or the same worker: retention purges per area (independent of sync success), alert detection, reporting snapshots, and reconciling stale `Running` runs. | B2 | M |
| **Umbraco load-balancing configuration**: an explicit `SchedulingPublisher` / `Subscriber` server role, with jobs running on the publisher only. | B7 | S |
| **Distributed cache invalidation**: an Umbraco `ICacheRefresher` for the branding theme (broadcast invalidation across nodes), or `HybridCache` with a distributed backing store. | B4 | S |
| **Rate limiting at the edge** (Front Door / App Gateway WAF rules) for login and sync. Keep the in-process limiter as a second layer, and document that its limits are per instance. | B4 | S |
| Single Data Protection registration in `DataProtectionKeyRingConfiguration`; remove it from the feature composer. Add a two-instance decrypt test. | B5 | S |

**Exit criteria:** no controller action awaits an upstream API; a two-instance local run (two Kestrel processes, one database) passes sync, branding publish/rollback and the login rate-limit scenarios; queue-age and job-failure metrics are emitted.

### Phase 4: operability and cross-cutting concerns (~2 weeks, alongside Phases 2–3)

| Work item | Addresses | Size |
|---|---|---|
| **Structured logging standard**: `ILogger<T>` in every application service, sync, ingestion and job, with tenant, run key and correlation ID in scope (`BeginScope`). Log counts and durations, never record contents. Extend the `SecurityEvents` pattern. | D3 | M |
| **Custom OpenTelemetry signals**: an `ActivitySource` per area for syncs and Gold queries, plus meters for rows ingested, sync duration, job queue age and query duration by page. Dashboards and alerts in Application Insights. | D3 | M |
| **`IAuditWriter` contract in the shared kernel**, with one shape (actor, tenant, entity, action, before/after summary, correlation ID). Keep the per-area tables to preserve area independence, or converge them on a single `Platform_AuditLog`; decide in an ADR. Add a cross-area "activity for person X" query to support DSR export. | D2 | M |
| `ValidateOnStart` + `IValidateOptions` for every options class. | D6 | S |
| Readiness checks for Blob Storage and Key Vault alongside SQL. | ops | S |
| New `ExecutiveReview` migration plan for future steps; document the historical exception. | D5 | S |

### Phase 5: modularisation (trigger-based, not scheduled)

Split `ProgrammePulse.csproj` into a host project, a `SharedKernel` project and one project per feature area (with the vendor integrations under their owning area) **only when one of these triggers occurs**: a second team working in parallel, incremental build time above roughly 60 s, a customer-driven need to deploy an area separately, or a boundary architecture test being bypassed more than once. Until then, the namespace boundaries plus the Phase 0 cycle and tenant tests give most of the benefit without the extra build and packaging cost. When it happens, rename `ProgrammePulse.Tests` and move it out of the web project folder at the same time.

## Recommendations deliberately not made

- **Microservices, or a separate service per area.** There's one database, one team, and pilot-scale traffic. The modular monolith is the right shape; Phases 2–3 remove its scaling limits without adding distributed-system failure modes.
- **Switching to EF Core.** The NPoco choice is documented and deliberate. The problems above come from query shape and indexing, not from the ORM.
- **A mediator or CQRS library.** Plain application-service classes and read-model interfaces get the separation without the indirection.
- **A generic `IRepository<T>`.** That would reintroduce the wide-interface problem from A5 and hide the tenant predicate, which D4 needs to stay visible.
- **SQL Server row-level security, for now.** It's worth revisiting as defence in depth *after* Phase 2 makes `tenantId` non-nullable and indexed. Before then it would add risk and give little benefit.

## How this relates to the planned product branches

The [consolidation record](archive/consolidation-2026-09-23.md#outstanding-work-proposed-next-branches) proposes product branches (executive data lifecycle, reviewed-pack publication, commercial effort allocation, market calendars, evidence component registry, pilot release assurance). Recommended ordering:

- Do **Phase 0 and Phase 1 before** starting another feature area or controller-heavy branch. Every new controller written in the old style adds to the backlog.
- **Phase 2 indexes and date-scoped reads** should come before `feature/commercial-effort-allocation`, which adds more time-entry-heavy aggregation.
- **Phase 3** should come before any pilot tenant whose ClickUp or Jira workspace is large enough for a sync to approach the request timeout. Phase 0's baseline will show where that threshold is.
- `chore/pilot-release-assurance` can run in parallel. It is operating evidence, not code structure.
