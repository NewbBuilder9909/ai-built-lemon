# CLAUDE.md

> **This project stopped on 2 October 2026 and is published as a lesson.**
> Read [READ-ME-FIRST.md](READ-ME-FIRST.md) first. This file is the
> instruction set the AI agents worked under, kept as part of the record. It
> shows how a long agent-built project was steered: every rule below is about
> building correctly, and none is about whether to build at all.

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
dotnet restore
dotnet build
dotnet run                                    # runs the Umbraco site (Program.cs)
dotnet test                                   # runs ProgrammePulse.Tests
dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --filter "FullyQualifiedName!~Integration&FullyQualifiedName!~Personas" # fast unit tests
dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --filter "FullyQualifiedName~Integration|FullyQualifiedName~Personas" # SQL-backed integration/render tests
dotnet test --filter "FullyQualifiedName~ClickUpMappingServiceTests"   # single test class
dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --filter "FullyQualifiedName~NorthstarDemo" # load the Northstar demo (PP_DEMO_SQL_CONNECTION; docs/demo-data.md)
dotnet user-secrets list --project .
dotnet user-secrets set "ClickUp:ApiToken" "<token>" --project .        # required for a local ClickUp sync
curl -k https://localhost:44329/health          # liveness (no dependencies)
curl -k https://localhost:44329/health/ready    # readiness (SQL round-trip)
./scripts/prune-remote-branches.ps1             # branch clean-up; dry run unless -Apply
```

The LocalDB connection string lives in `appsettings.Development.json` only;
the base `appsettings.json` deliberately leaves `umbracoDbDSN` empty so a
non-Development start without `CONNECTIONSTRINGS__UMBRACODBDSN` fails fast
(`Startup/ProductionConfigurationGuard`) instead of showing Umbraco's
installer.

There is one test project, `ProgrammePulse.Tests` (nested under the repo
root but excluded from the main `.csproj`'s compile glob — see the
`<Compile Remove>` block in `ProgrammePulse.csproj` if a new subfolder
ever needs the same treatment). Tests use fakes (e.g.
`ProgrammePulse.Tests/ProgrammeOps/FakeProgrammeRepository.cs`,
`FakeStaffRepository.cs`) rather than a mocking framework — follow that
pattern for new service tests instead of introducing Moq/NSubstitute.

**Planning:** [`docs/direction.md`](docs/direction.md) is the single source
of truth for where the product stands and what to build next. Read it before
starting product work, and update it in the same PR as any status change.
Dated reviews are evidence, not plans: fold a new review's actions into
`direction.md` and move the review it replaces to `docs/archive/`, whose
items are never a backlog. [`docs/README.md`](docs/README.md) maps where
every other document lives. When more than
one agent is working, follow [`docs/parallel-agent-working.md`](docs/parallel-agent-working.md):
own worktree and test database, one owner per shared file, and route
snapshots regenerated only on integration.

**Delivery:** `main` moves only through a pull request with green CI. A
session that commits ends by pushing and opening a PR, or says why it
didn't. A branch with no PR is invisible, and on 27 September that left
four streams to integrate by hand. Never add commits to a branch whose PR
has merged; start again from `main`. Cloud sessions get the .NET SDK and
PowerShell from `.claude/hooks/session-start.sh`, so run the fast unit tests
and `./scripts/check-views.ps1` before pushing rather than using CI as the
compiler. Rules 9–14 in `docs/parallel-agent-working.md` have the detail.

## Big-picture architecture

This is a single Umbraco 18.2.0 / .NET 10 site containing several feature
areas built as domain layers on top of Umbraco, not as separate services:

1. **Operations demo** (`/demo`; the old `/opshwb` paths were retired along
   with every other "Hwb" name — see `ProductNameTests`) — the original demo: an in-memory ERP
   pipeline (`Models/Erp`, `Services/Transformation`,
   `Services/DemoData/DemoErpDataProvider`) that goes raw → validated →
   transformed → aggregated → view model. No real backing store; this area
   established the pipeline/test conventions the later ClickUp Bronze/Silver
   flow reuses.
2. **Staff Operations** (`/staffops*`) — staff profiles, five tenant-role access
   control, leave request submit/approve, admin-only hourly cost rates,
   TOTP MFA for the Admin group, and GDPR export/erasure — see
   [`docs/gdpr.md`](docs/gdpr.md) for the export/erasure/retention detail.
3. **Programme Ops** (`/staffops/programme`) — a medallion pipeline
   (Bronze/Silver/Gold) that syncs ClickUp task data into a source-agnostic
   domain model. Full design detail — sync flow, extension points — is in
   [`docs/programme-ops.md`](docs/programme-ops.md); read it before touching
   anything under `Services/Integrations/ClickUp` or `Services/ProgrammeOps`.
4. **Branding Ops** (`/staffops/branding`) — a controlled design-token admin
   area (colours, fonts, logo/favicon, layout style) with a draft →
   publish → rollback lifecycle, audit log, and a cached runtime theme
   resolver with a safe platform-default fallback. Was single-tenant by
   design; is now the **pilot for real multi-tenant data isolation** (see
   point 6 below) — every table and repository method here is tenant-scoped.
   See [`docs/branding.md`](docs/branding.md) for the token-system detail
   and [`docs/tenancy.md`](docs/tenancy.md) for the isolation mechanism,
   and read both before touching anything under `Services/BrandingOps`.
5. **Contract Ops** (`/staffops/contracts`) — commercial control: contract
   terms (Fixed Price / Time & Materials / Ongoing), a document repository
   stored outside `wwwroot` (never web-servable, unlike Branding's public
   assets), a value-vs-cost burn-down (now including non-labour costs) with
   a portfolio-wide margin rollup (`/staffops/contracts/overview`), and
   invoice generation/documents. Admin-only throughout — more sensitive than
   the cost/rate data Staff Ops already gates, so it isn't reachable by Team
   Lead the way the Reporting Hub is. See
   [`docs/contract-ops.md`](docs/contract-ops.md) before touching anything
   under `Services/ContractOps`.
6. **Tenancy** (`Services/Tenancy`) — the multi-tenant foundation: a
   `Tenant` entity with a commercial lifecycle (`TenantStatus`:
   Trial/Active/Suspended/Archived, `TenantPlan`, trial end), a per-request
   `ITenantContext` resolved from the signed-in member's
   `StaffProfile.TenantId` (member-based only — no subdomain/custom-domain
   routing) that also applies `TenantAccessPolicy` (blocked tenants get a
   403 from the global `TenantAccessFilter`), and plan entitlements
   (`PlanEntitlements`, `IFeatureGate`, `[RequireFeature]`). Isolation is
   enforced on Branding Ops, the Staff admin roster/onboarding, all of
   Programme Ops (including per-tenant ClickUp/Hub Planner source
   credentials and cross-tenant foreign-key validation), Contract Ops and
   Approvals; `StaffRate`/availability queries and Member Groups themselves
   remain global (`docs/tenancy.md`'s enforcement matrix has the detail).
   Tenant lifecycle is
   managed by a **Platform Admin** at `/staffops/platform/tenants`, with an
   assisted onboarding workspace per organisation
   (`/staffops/platform/tenants/{key}/onboarding`) that provisions its first
   Admin through the normal identity path ([`docs/saas-onboarding.md`](docs/saas-onboarding.md)). See
   [`docs/tenancy.md`](docs/tenancy.md) — including its enforcement matrix —
   before extending tenant isolation to a second feature area, and before
   touching anything that resolves the current tenant.
7. **Skills and Evidence** (`/staffops/skills`) — a tenant-scoped,
   **manually reviewed** skills matrix: a versioned skill taxonomy, a
   four-level plain-language proficiency rubric, self-declaration,
   manager/admin validation with a required rationale, a staff correction
   ("challenge") flow, and an aggregate coverage view that flags
   single-maintainer continuity risk without naming anyone. Assertions are
   **append-only** — every transition supersedes rather than overwrites, so
   no prior review is ever lost. Nothing here is inferred from activity;
   that is a product rule, not an omission.

   **Repository evidence** (Slice 2) adds a tenant-scoped, read-only
   GitHub App connection (`SkillsEvidence_Connection` — several accounts
   per tenant, credential encrypted under its own Data Protection purpose
   and with **no deployment-wide fallback**, unlike ClickUp/Hub Planner),
   a provider-neutral `EngineeringEvidence` contract keyed on
   `(tenantId, connectionKey, sourceType, externalId, role)`, incremental
   cursors with explicit partial/permission-lost coverage, and an
   evidence section on `/staffops/skills/staff/{staffKey}`. Two rules are
   structural rather than conventional: evidence reaches a named person
   **only** through an approved `EvidenceActorLink` (an email match is a
   queue suggestion, never a resolution — which is why this area has its
   own `IEvidenceActorResolver` rather than reusing ProgrammeOps'
   `IStaffIdentityResolver`, whose links are per source rather than per connection),
   and no evidence service may reference `ISkillAssertionService`, so
   activity can never raise a proficiency. Both fail the build if broken.

   **Reviewed coverage** (Slice 4) adds a manager-declared component map
   (`SkillsEvidence_ComponentOwnership`, `_ComponentBackup`), the
   Platinum action layer (`_CoverageAction` — owner, rationale, evidence
   snapshot, follow-up outcome) and a key-person coverage view at
   `/staffops/skills/continuity`. Ownership is **declared, never
   inferred**: the person who touches a component most is often the
   person left holding it, which is the exposure the view exists to
   find, not the answer to it.

   `SkillsEvidence_ProcessingDecision` records the tenant's lawful
   basis, worker notice and DPIA decision, and **gates evidence
   collection** — `GitHubEvidenceIngestionService` refuses to run
   without a current one. Superseded rather than edited (filtered unique
   index on the live row), so "what were we relying on in March" stays
   answerable. It deliberately does not gate the skills matrix. See
   [`docs/skills-evidence-gtm-claims.md`](docs/skills-evidence-gtm-claims.md)
   for what may and may not be claimed, and
   [`docs/skills-evidence-validation-protocol.md`](docs/skills-evidence-validation-protocol.md)
   for the design-partner validation that has **not** been run.

   **Suggestions** (Slice 5, `SkillsEvidence_Suggestion`) propose skill
   tags from repeated language hints and coverage actions from the
   component map, each with a confidence *band* (threshold counts, never
   a percentage), a rationale and citations. The ceiling on acceptance
   is the whole slice: accepting a skill tag creates a self-declared,
   Submitted assertion at `Awareness` and is refused if the person
   already has a record for that skill, so it can never raise a level;
   there is no path to a confirmed root cause, and `SuggestionService`
   cannot reach the Service Ops namespace. Dismissals are kept so a
   proposal is never re-raised.

   Gated on `ProductFeature.GitHubEvidence` (Enterprise by default).
   **Never exercised against a live GitHub App** — the app credentials
   are unset on every environment, and the UI labels evidence as an
   unverified replay until a run completes cleanly. All five slices are
   built (Slice 3, the support desk, is its own area: Service Ops, below);
   none has run against live sources. Read
   [`docs/staff-skills-evidence-module.md`](docs/staff-skills-evidence-module.md)
   before touching anything under `Services/SkillsEvidence` or
   `Services/Integrations/GitHub`.
8. **Service Ops** (`/staffops/service`) — support-desk service health:
   demand, recurrence, reopen rate and median time to restore by
   component, over a stated period with a like-for-like previous window.
   Freshdesk is the first adapter (Slice 3 of the same design doc).

   **Its own feature area, not part of SkillsEvidence, on purpose.** The
   design promises a customer can connect a desk without connecting a
   repository; a separate migration plan, composer and `ProductFeature`
   make that structurally true rather than merely intended. A
   `SupportCodeLink` points at an artefact by plain string id, never by a
   typed reference into the evidence area, and a test fails the build if
   that changes.

   The load-bearing rule is `SupportLinkMethod`: an issue-key match is a
   **relationship**, and only `ConfirmedRootCause` — which requires a
   named reviewer, a timestamp and a rationale — may be described as
   causal. Nothing is derived from commit timing or `git blame`;
   `SupportCodeLink` and `SupportCaseRole` have nowhere to record blame
   at all, and an automatic suggestion can never overwrite a reviewer's
   verdict. Only ticket **metadata** is stored — never bodies,
   attachments or requester details.

   Gated on `ProductFeature.SupportEvidence` (Professional upwards —
   lighter privacy footprint than repository evidence, since case
   metadata is about a product). **Never exercised against a live
   desk.** Read the design doc before touching `Services/ServiceOps` or
   `Services/Integrations/Freshdesk`.
9. **Security Assurance** (`/staffops/security`) — step 3 of
   [`docs/delivery-evidence-and-contract-assurance.md`](docs/delivery-evidence-and-contract-assurance.md):
   a read-only connection to a scanning tool (Aikido first, in
   `Services/Integrations/Aikido`) that records per-repository gate
   configuration and finding **metadata** — never file paths, lines, titles
   or code, and Bronze is allow-listed rather than verbatim for that reason.
   Contract Ops reads it through the tool-neutral
   `ISecurityAssuranceQueryService` snapshot, where an observation
   supersedes an attestation, no gate configured is "not in place
   (observed)", ignored/snoozed findings still count, and PR-check runs
   stay labelled **Unverified** (`CheckRunsVerified` is false) until a live
   gated pull request confirms their fields. Reports, never enforces.
   Gated on `ProductFeature.SecurityAssurance` (Enterprise). **Never synced
   against the live workspace yet.**
10. **Startup / operations** (`Startup/`, `Middleware/`,
   `Services/Integrations/Resilience/`) — `ProductionConfigurationGuard`
   refuses to boot a non-Development environment with an empty/LocalDB
   connection string, `UseHttps=false` or `Hosting:Debug=true` (runs in
   `Program.cs` before Umbraco boots; pure and unit-tested);
   `CorrelationIdMiddleware` stamps `X-Correlation-ID` on every response and
   Serilog event; `/health` (liveness, no dependencies) and `/health/ready`
   (`DatabaseHealthCheck`); per-client-IP rate limiting on login and sync (every `/sync` POST must carry
   `[EnableRateLimiting("sync")]`; `Architecture/SyncRateLimitTests`);
   `TransientHttpRetryHandler` + `SyncRunGuard` behind both Bronze
   integrations. Release posture, risk register and the go/no-go call live in
   [`docs/release-readiness.md`](docs/release-readiness.md); retention,
   lifecycle and DSR rules in
   [`docs/data-governance.md`](docs/data-governance.md).

Organization is **by feature area** (`Erp`, `Staff`, `Programme`/`ProgrammeOps`,
`Integrations/ClickUp`, `Integrations/HubPlanner`, `Integrations/Jira`,
`Integrations/Tempo`, `Integrations/GitHub`, `Integrations/AzureDevOps`,
`Integrations/Freshdesk`, `Integrations/Aikido`, `Branding`/`BrandingOps`, `ContractOps`,
`Commercial`, `ExecutiveReview`, `Tenancy`, `SkillsEvidence`, `ServiceOps`, `SecurityAssurance`,
`Security`), not by technical layer — `Models/`, `Services/`, `Controllers/`,
`Migrations/`, and `Composers/` each have one subfolder or file per feature
area.

Feature areas under `Services/` must not depend on each other in a cycle,
and `Architecture/FeatureAreaDependencyTests` fails the build if they do. A
type that several areas need, such as `CrossTenantReferenceException`, goes
in `Services/Shared`, which depends on no area. A value that one area needs
from another, such as the current tenant, is passed in by the caller rather
than resolved by reaching into that area. `StaffOnboardingService` used to
break this rule by reading `ITenantContext`. MFA (TOTP, challenges,
`IMfaRepository`) lives entirely in `Services/Security`.

Every repository SQL statement that filters or changes rows carries
`tenantId`, or its method is listed in `Architecture/TenantPredicateTests`
with a reason: platform table, one-person erasure, retention purge, or a key
the caller has already proven. Add the predicate; only add an exemption when
one of those four genuinely applies.

### Persistence: NPoco migrations, not EF Core

Deliberate choice, to avoid a second data-access stack alongside Umbraco's
own. New tables use Umbraco's own migration framework
(`Umbraco.Cms.Infrastructure.Migrations.AsyncMigrationBase` / `MigrationPlan`
/ `Upgrade.Upgrader`) against the existing `umbracoDbDSN` connection, and run
on startup via `INotificationAsyncHandler<UmbracoApplicationStartingNotification>`.
Each feature area with its own tables gets its **own** migration plan:

- `Migrations/StaffOps/` → plan `"StaffOps"` → `StaffOps_Staff`,
  `StaffOps_StaffRate`, `StaffOps_Availability`, `StaffOps_LeaveRequest`,
  `StaffOps_MemberMfa`, `StaffOps_AuditLog`, `StaffOps_WorkHoursHistory`
  (append-only, mirrors `StaffOps_StaffRate` — see the PMP/APM utilisation
  review), plus `StaffOps_Staff.TenantId` (nullable, backfilled twice —
  `AddStaffTenantColumn` and `BackfillStaffTenantColumn` — see
  `docs/tenancy.md`).
- `Migrations/ProgrammeOps/` → plan `"ProgrammeOps"` → `ProgrammeOps_*`
  tables (`Programme`, `Project`, `Workstream`, `WorkItem`,
  `WorkItemAllocation`, `Dependency`, `Risk`, `Issue`, `AuditLog`,
  `RawClickUpPayload`, `RawHubPlannerPayload`, `TimeEntry`, `Customer`,
  `WorkstreamBaseline`, `ChangeRequest`, `ProgrammeStakeholder`,
  `ReportingSnapshot`, `Alert`, `PlannedAllocation`, `ExternalIdentityLink`,
  `UnresolvedIdentity`, `SyncRun`, `SyncLease`, `EvidenceReview`,
  `EvidenceReviewFinding`) plus
  `Programme.BudgetAmount`/`BudgetCurrency` columns. The two `Raw*Payload`
  tables, stale `UnresolvedIdentity` rows and finished `SyncRun` rows are
  purged on retention windows after every successful sync
  (`ProgrammeOps:RawPayloadRetentionDays`, `SyncRunHistoryRetentionDays`).
  `AddPlannedAllocationTable` also deleted the legacy Hub Planner
  booking-as-WorkItem rows (audited as `Migration/LegacyHubPlannerWorkItemsRemoved`)
  — bookings are `PlannedAllocation` rows now, never work items.
  `AddTenantReadIndexes` adds tenant-leading indexes on the hot tables. It
  backfills `workDate`, which is *defined* as `TimeEntry.ReportDate` and is
  what the period seek relies on. `AddOpenAlertUniqueness` adds a filtered
  unique index, so there is one open alert per (tenant, type, entity).
  `AddCodeRepositoryLinkTable` (step 24) adds `CodeRepositoryLink`: an
  Admin's **declared, never inferred** link from a repository
  `(provider, sourceAccountId, repositoryKey)` to a project, ended rather
  than deleted, one live link per repository and project
  (`UX_ProgrammeOps_CodeRepositoryLink_live`). Estimated effort and contract
  assurance hang off it; see `docs/delivery-evidence-and-contract-assurance.md`.
  `AddTimeEntrySourceWorkItem` (step 28) adds `TimeEntry.sourceWorkItemExternalId`:
  the source's id for the item an entry was logged against, kept when that
  item isn't synced, so unlinked hours can name their issue. The Jira and
  Tempo report (`/staffops/reporting/jira-tempo`) reads it, and exists only
  for a tenant with Jira or Tempo data (`GetSourcePresenceAsync`).
  `AddImportStagingTable` (step 29) adds `ImportStaging`: a file import
  that would remove rows, held until the person confirms it. `tenantId` is
  non-nullable; rows go on confirm or cancel and are purged after a day.
  `AddEvidenceReviewScope` (step 30) adds the scope columns to
  `EvidenceReview` (programme, customer, period, extract date, decision).
  They are nullable and not backfilled: a review from before scopes read the
  whole organisation, and says so.
- `Migrations/BrandingOps/` → plan `"BrandingOps"` → `BrandingOps_*` tables,
  each carrying a nullable `TenantId` column (Branding Ops is the
  multi-tenancy pilot — see `docs/tenancy.md`).
- `Migrations/ContractOps/` → plan `"ContractOps"` → `ContractOps_Contract`,
  `ContractOps_ContractDocument`, `ContractOps_AuditLog`,
  `ContractOps_NonLabourCost`, `ContractOps_Invoice`,
  `ContractOps_InvoiceLine`, plus (step 05) `ContractOps_Obligation` and
  `ContractOps_RepositoryControlAttestation`: contract clauses with an
  engineering consequence, and dated, expiring attestations of the repository
  controls they need. Both have a non-nullable `tenantId`, are withdrawn or
  superseded rather than edited, and keep one current attestation per
  repository and control (`UX_ContractOps_RepositoryControlAttestation_current`).
  Assurance reports and never enforces; an expired or missing attestation is
  never shown as met. Step 06 (`AddInvoiceDraftStage`) adds `issuedAtUtc` and
  `needsReviewHours` to `ContractOps_Invoice`: invoices are generated as
  drafts, dated by `TimeEntry.ReportDate`, and numbered only when issued
  (see `docs/contract-ops.md`).
- `Migrations/SkillsEvidence/` → plan `"SkillsEvidence"` →
  `SkillsEvidence_SkillDefinition`, `SkillsEvidence_StaffSkillAssertion`,
  `SkillsEvidence_AuditLog`, plus the Slice 2 evidence tables
  `SkillsEvidence_Connection`, `_EngineeringEvidence`, `_ActorLink`,
  `_UnmappedActor`, `_Coverage` and `_RawPayload` (Bronze, purged on
  `SkillsEvidence:RawPayloadRetentionDays` after each successful run).
  `tenantId` is **non-nullable** on all nine —
  the first area added after tenancy existed, so it has no legacy rows to
  accommodate; do the same for the next new area. Two indexes are
  hand-written T-SQL because the annotation set has no composite or
  filtered form: `UX_SkillsEvidence_SkillDefinition_tenant_skillKey`, and
  `UX_SkillsEvidence_StaffSkillAssertion_current` — unique on
  `(tenantId, staffKey, skillKey) WHERE supersededAtUtc IS NULL`, which is
  the whole mechanism behind "one current assertion, unlimited history".
  Precedent: `AddEstimateBaselineTable` and `AddIdentityLinkUniqueness`.
  Slice 2 adds five more composite unique indexes the same way; the
  load-bearing one is `UX_SkillsEvidence_EngineeringEvidence_identity` on
  `(tenantId, connectionKey, sourceType, externalId, role)`, which is
  what makes a replayed ingestion page idempotent instead of duplicating
  someone's work. Step 03 adds the continuity tables
  (`_ComponentOwnership`, `_ComponentBackup`, `_CoverageAction`,
  `_ProcessingDecision`) plus a second filtered unique index,
  `UX_SkillsEvidence_ProcessingDecision_live`
  (`WHERE withdrawnAtUtc IS NULL`), keeping one live data-processing
  decision per tenant while retaining every superseded one.
- `Migrations/ServiceOps/` → plan `"ServiceOps"` → `ServiceOps_DeskConnection`,
  `_SupportCaseFact`, `_SupportCodeLink`, `_CaseParticipant`, `_AgentLink`,
  `_Coverage`, `_RawPayload`, `_AuditLog`. Its own plan, not an extension of
  `"SkillsEvidence"` — the two areas must upgrade and fail independently so
  a desk-only customer is a real configuration. `tenantId` non-nullable
  throughout; six composite unique indexes, of which
  `UX_ServiceOps_SupportCaseFact_identity` is load-bearing: the Freshdesk
  ingest deliberately re-reads an overlapping window every run, and that
  index is what makes the overlap free instead of duplicating demand.
- `Migrations/SecurityAssurance/` → plan `"SecurityAssurance"` →
  `SecurityAssurance_Connection`, `_Finding`, `_RepositoryObservation`,
  `_CheckRun`, `_RawPayload`, `_AuditLog`. `tenantId` non-nullable
  throughout; identity indexes on `(tenantId, tool, externalId)` make a
  replayed sync idempotent. Observations are replaced only after a
  complete read.
- `Migrations/Tenancy/` → plan `"Tenancy"` → `Tenancy_Tenant` (one seeded
  default row) plus the lifecycle columns `status`, `planName`,
  `trialEndsAtUtc`, `updatedAtUtc` — see `docs/tenancy.md`. Column is
  `planName` because `plan` is a T-SQL reserved word; keep avoiding reserved
  words in column names since migrations here use hand-written `UPDATE`s.
  **Exception on record:** the ExecutiveReview tables
  (`ExecutiveReview_MarketVersion`, `_Pack`, `_DecisionVersion`,
  `_DataEvent`) were added as steps `2026-09-tenancy-03..05` of *this*
  plan, not a plan of their own. Don't move them, because plan state is
  keyed by name and step. Put any future ExecutiveReview schema change in a
  new `"ExecutiveReview"` plan.

Adding tables for a new feature area means: a new migration plan name (don't
reuse an existing one), a startup handler registered in that area's
composer, and a repository per aggregate (or, following the
`ProgrammeRepository` precedent, one repository covering several related
entities rather than one file per entity — that was a deliberate choice to
avoid near-duplicate repo files, not an oversight).

### Reads are bounded by what the page shows (Phase 2)

- **Take `IProgrammeReadRepository` unless you write.** A report or
  `*QueryService` takes the read half. Taking `IProgrammeRepository` means
  being listed, with a reason, in
  `Architecture/ProgrammeRepositoryWriterTests`.
- **Never read a tenant's whole time history to show a period.** Use the
  period read, the SQL aggregates (`GetLoggedHoursByWorkItemAsync`,
  `GetWeeklyProjectCountsAsync`, `GetTimeEntryCoverageAsync`) or a point
  lookup by key. A period that comes from the query string goes through
  `Services/Shared/ReportingPeriod` (at most 731 days), or a crafted
  `?from=0001-01-01` undoes all of that.
- **How a bounded read is added:** as an interface method whose body is its
  definition over the list reads (the fakes use that body), plus a SQL
  override. Add a case to `Integration/BoundedReadIntegrationTests`
  proving the two agree.
- **Per-person data is one batched read, never a loop.** This covers rate
  history, work-hours history and availability (`IReadOnlyCollection<Guid>`
  overloads).
- **Sync writes a batch at a time** through `UpsertWorkItemsAsync` and
  `UpsertTimeEntriesAsync` (set-based, via `Services/Shared/SqlMultiRow`).
  What a row upsert writes is defined once, in `ProgrammeRepository.Apply`,
  and `Integration/BatchedSyncWriteIntegrationTests` compares the two paths
  field by field.
- **A list view is paged.** Use `PageRequest`/`ResultPage<T>`
  (`Services/Shared/Paging.cs`), `SqlPaging.ForPage` on an `ORDER BY` that
  ends on a unique column, and `Views/StaffOps/_Pager.cshtml`. Fetch one row
  past the page instead of counting the list. When a page shows a total, get
  it from a SQL count or summary, never from the page's rows. Prove a new
  paged read in `Integration/PagingIntegrationTests`. (Our type is
  `ResultPage`, not `Page`, because NPoco already has a `Page<T>`.)
- **The report read path carries a `CancellationToken`**, from the page
  action (MVC binds it to `RequestAborted`) to NPoco. NPoco takes a token
  only on its `Sql` and `object[]`-argument overloads, not on the `params`
  forms, so write `FetchAsync<T>(sql, new object[] { … }, cancellationToken)`.
  `Architecture/CancellationTokenTests` lists the covered interfaces; add
  yours there when extending it.
- The scale baseline (`PP_RUN_SCALE_BASELINE=1`) times each page and the
  reads it is built from.

### Auth/identity: Umbraco Members + Member Groups

Not ASP.NET Core Identity, not the backoffice user system. Four tenant roles
= four Member Groups, defined as string constants in
`Models/Staff/StaffRole.cs` (`Admin`, `Holiday Approver`, `Team Lead`,
`Staff` = `StaffRole.All`), plus a fifth operator-only group
`Platform Admin` (`StaffRole.PlatformAdmin`, in `StaffRole.Seeded` but
deliberately *not* in `All`, so a tenant Admin can't create one via the
onboarding form — it gates the tenant console `StaffTenantAdminController`
and is granted only from the backoffice). That file is the *one* place
those literal group names should appear; all five are seeded idempotently
by `Services/Staff/StaffGroupSeeder`. Role checks go through
`Umbraco.Cms.Web.Common.Security.MemberManager.IsMemberAuthorizedAsync(allowGroups: [...])`
— inject the concrete `MemberManager` class, not an `IMemberManager`
interface (there isn't one in Umbraco 18).

**Gating a `/staffops` action is declarative.** Capabilities come from
`Models/Staff/Capability` via `IStaffAuthorizationService`. The
refuse-or-proceed gate is `[RequireCapability(Capability.X)]` on the action
or controller, and a tenant-scoped action takes `[CurrentTenant] Guid tenantId`
as a parameter. That parameter is bound only from `ITenantContext`, never from
the request, and declaring it opts the action into `CurrentTenantFilter`,
which returns 403 when no tenant is resolved. Don't write the old in-body
`if (!await HasAsync(...)) return Forbid();` / `ResolveTenantAsync()`
preamble. `CrossTenantReferenceException` becomes a 404 globally
(`CrossTenantReferenceExceptionFilter`), so don't catch it per action.
Keep `HasAsync` in the body only for decisions that choose *what to show*
(e.g. "an Admin also sees cost"). `Architecture/ControllerGateTests` fails
the build on any `Staff*` action with neither a gate nor a documented
exemption. Controller refusal tests use `Controllers/ActionGate`, which
runs the real filters against the declared attributes.

**Controllers bind, call an application service, and return.** No
controller takes a repository (`Architecture/ControllerLayerTests`).
Validation, timestamps, audit entries and tenant-ownership checks live in
the area's service, e.g. `RaidService`, `ContractAdminService`,
`StaffAdminService` or `DeskAdminService`. The caller comes from
`ICurrentStaff`, which is request-scoped and shared with authorization and
tenant resolution. Never look up the member yourself. A self-service action
uses `tenantContext.ResolveSelfAsync(currentStaff)`. Page data goes in a typed
view model. `ViewData` holds only `Title`, `Message` and `ErrorKey`, and a
test enforces that. Commands a person can get wrong return a
`CommandOutcome`/`CommandResult` (in `Services/Shared`) with a message,
rather than silently doing nothing. Audit detail is always
`JsonSerializer`-built, never string interpolation.

The `/staffops` URL contract (verb, path, capabilities, tenant,
antiforgery) is snapshotted in
`ProgrammePulse.Tests/Architecture/staffops-routes.txt`. After an
*intended* route or gate change, regenerate it with
`PP_UPDATE_ROUTE_CONTRACT=1 dotnet test --filter RouteContractTests` and
review the diff. `ViewLinkTests` fails on any `asp-controller`/`asp-action`
pair or literal redirect that no longer resolves; a stale tag helper
renders an empty link rather than failing the build.

`IStaffRoleAssignmentService.GetForCurrentMemberAsync()` can only answer for
whoever is currently signed in (`MemberManager` has no "check an arbitrary
other member" overload). Looking up another member's roles needs
`IMemberService` group queries instead — not implemented yet; treat it as a
known gap, not something to silently work around. `StaffAccountController`
now has a concrete example of this exact pattern: it needs to know if a
member is in the Admin group *before* they're signed in (to decide whether
MFA applies), so it calls `IMemberService.GetMembersByGroup(groupName)` and
checks membership by `Id` — reuse that shape rather than reaching for
`MemberManager.IsMemberAuthorizedAsync` if you hit the same "not the current
user" problem elsewhere.

**MFA (Admin group only):** password verification (`CheckPasswordSignInAsync`)
and the real sign-in (`SignInAsync`) are now two separate steps for Admins,
with a short-lived second cookie scheme (`MfaChallengeStore`, scheme name
`"MfaPending"`, registered in `Program.cs`) holding the member's identity in
between while they complete a TOTP challenge/enrollment
(`Services/Security/TotpAuthenticator.cs` — hand-rolled RFC 6238, no new
NuGet dependency). Non-Admin members are unaffected. See
`StaffAccountController` for the full flow before changing login behaviour.

### Planned time is not delivery; unmatched people are never dropped

Two Silver rules that came out of the GTM review
(`docs/archive/platform-mapper-gtm-review-2026-09-16.md`): a scheduling-source booking maps to
`Models/Programme/PlannedAllocation`, which carries no lifecycle stage and
must never be counted as Done / % complete by any Gold consumer; and every
external person (ClickUp assignee or time-entry user, Hub Planner resource)
goes through `IStaffIdentityResolver` (an explicit `ExternalIdentityLink`,
otherwise recorded as an `UnresolvedIdentity` on the Admin identity queue at
`/staffops/programme/identities`). An email match is only the row's
suggested staff member, approved in one click: it never attributes on its
own, because whoever can edit an email in the source tool would otherwise
choose whose name the work goes under. Both sync services run under
`SyncRunCoordinator` (in-process latch + `ProgrammeOps_SyncLease` database
lease) and leave a `ProgrammeOps_SyncRun` row that the Programme Overview
header and platform console read for "last published / running / failed".

### Cost/rate data isolation is structural, not a hidden UI column

Admin-only hourly cost rates (`StaffOps_StaffRate`) must never be fetched on
a non-admin code path. The Programme Overview Gold query
(`ProgrammeOverviewQueryService`) and its view model
(`ProgrammeOverviewViewModel`) carry no cost/rate field at all — when adding
a new view that a non-Admin role can reach, follow that pattern: the query
service must not retrieve `StaffRate` rows, rather than retrieving and
hiding them in the view.

### ClickUp integration layering (medallion)

- **Bronze** (`Services/Integrations/ClickUp/*`, `Models/Integrations/ClickUp/Raw/*`)
  is the only layer allowed to know ClickUp's API shape. Raw JSON is
  captured verbatim to `ProgrammeOps_RawClickUpPayload` before anything else
  touches it.
- **Silver** (`Models/Programme/*`, `Services/ProgrammeOps/ProgrammeRepository`)
  is source-agnostic on purpose, so a future second source (e.g. a calendar
  integration) can write into the same tables without Silver/Gold changing.
- **Gold** (`Services/ProgrammeOps/ProgrammeOverviewQueryService`) reads only
  Silver + the Staff domain, never Bronze or the ClickUp client directly.
- `ClickUpMappingService` is the one place Bronze DTOs and Silver models both
  appear — the seam between the two layers. Don't reference `IClickUpApiClient`
  or the `Raw` DTOs from outside `Services/Integrations/ClickUp`.
- `Services/Integrations/Abstractions` is the source-neutral seam above
  Bronze: `ISyncSource` (one source as the rest of the app sees it),
  `ISyncSourceRegistry` (the single source list — there is no hand-maintained
  vendor list anywhere else), `SourceCapabilities`, `IIngestionContext` and
  `RawEntity<T>`. Controllers, views and Gold services resolve sources
  through the registry, never a vendor sync service; adding a source is one
  `AddScoped<ISyncSource, …>()` line in `ProgrammeOperationsComposer` plus
  that source's own folder. This is enforced by
  `ProgrammePulse.Tests/Architecture/SourceIndependenceTests`, which fails
  the build if Silver/Gold reaches a vendor namespace or one vendor
  integration references another — see `docs/programme-ops.md`.
- `Services/Integrations/FileImport` is the credential-free Bronze source:
  CSV work items and time entries uploaded at `/staffops/programme/import`,
  validated all-or-nothing before any write. **Each upload replaces the
  last** of its kind: rows missing from the new file are removed, but only
  after the person confirms a staged preview (`ProgrammeOps_ImportStaging`,
  step 29), and removed work items unlink their time rather than delete it.
  The apply is one transaction. It is
  the entry route for a paid diagnostic, so it is on every plan
  (`ProductFeature.FileImport`) and is deliberately not an `ISyncSource`.
  See the "File import" section of `docs/programme-ops.md`.
- `Services/ProgrammeOps/EvidenceCheck*` is the Gold-layer **Evidence
  Check** (`/staffops/programme/evidence-check`): the automated exception
  register that the product's positioning rests on
  (`docs/archive/competitive-position-2026-09-26.md`). The rules live in
  the pure `EvidenceCheckCalculator`. Readiness is a band with stated
  thresholds, never a score, and every finding carries a numerator,
  denominator and source record ids. It is reachable below Admin, so it
  must never read cost rates, and a test enforces that.
  `EvidenceReview*` makes it a weekly loop: recording freezes the check
  (with source records) so its board pack stays reproducible; findings take
  an owner and decision that carry forward to the next recording; only the
  latest review is editable. The rules live in the pure
  `EvidenceReviewCalculator`. Every check has a declared `EvidenceScope`
  (programme/customer, period, optional extract date and decision). Time
  findings use the period, and estimate accuracy uses all recorded time.
  Comparison, carry-forward and the latest-only rule work within one scope. See "Recorded reviews" in
  `docs/programme-ops.md`. That loop is the current direction
  (`docs/direction.md`): check it before adding anything outside it.

### C# naming gotcha: don't shadow a namespace segment with a type name

A type named the same as its containing namespace's last segment (e.g. a
`Staff` record inside `Models.Staff`) breaks when referenced from another
namespace whose tail segment is also `Staff` (e.g. `Services.Staff`) — C#
resolves the bare identifier to the sibling namespace instead of the type
(`CS0118`). This is why the staff record type is `StaffProfile`, and why
`Programme`-referencing services live under `Services.ProgrammeOps`, not
`Services.Programme`. Keep following this pattern for new feature areas.

### Composers

Each feature area registers its own services/migrations/startup handlers via
its own `IComposer` (`Composers/OperationsHubComposer.cs`,
`StaffOperationsComposer.cs`, `ProgrammeOperationsComposer.cs`,
`BrandingOperationsComposer.cs`, `ContractOperationsComposer.cs`,
`CommercialComposer.cs`, `ExecutiveReviewComposer.cs`, `TenancyComposer.cs`,
`SkillsEvidenceComposer.cs`, `ServiceOperationsComposer.cs`,
`SecurityAssuranceComposer.cs`), discovered
automatically by `AddComposers()` in `Program.cs` — no manual DI wiring
needed there. Add a new composer per feature area rather than growing an
existing one.

### IDE tooling (VS Code)

`.vscode/launch.json` and `tasks.json` wire up F5 debugging and the
`build`/`watch`/`test` tasks — use the `test` task or Test Explorer rather
than shelling out to `dotnet test` manually when iterating in the IDE.

**When the IDE breaks, run `./scripts/dev-doctor.ps1` before changing
anything** — it separates "Dev Kit crashed" from "the project is broken".
This workspace contains six duplicate `.csproj` copies (a live `git worktree`
under `.claude/worktrees/`, plus whole-tree scan copies under `artifacts/`),
which is why C# Dev Kit's project-system server is unusually fragile here.
Containment lives in `dotnet.defaultSolution` + watcher excludes
(`.vscode/settings.json`), `DefaultItemExcludes` (`ProgrammePulse.csproj`)
and `global.json`. Debugging has a Dev Kit-independent fallback config
(`Run ProgrammePulse (no Dev Kit)`), so an outage never blocks iteration.
Full runbook: [`docs/dev-environment.md`](docs/dev-environment.md).

Note that `RazorCompileOnBuild` is `false`, so **`dotnet build` does not
check `.cshtml` files** — a view error appears only as a runtime 500. After
changing any view run `./scripts/check-views.ps1` (the `check views` task,
also a CI step), which type-checks every view; and for a view carrying a
claim that must not regress, add a render integration test alongside
`ContractsOverviewRenderIntegrationTests` /
`PurchasePageRenderIntegrationTests`. The two gates catch different faults —
see [`docs/dev-environment.md`](docs/dev-environment.md).
`.vscode/extensions.json` recommends the
extensions this repo actually benefits from: C# Dev Kit + C#, UmbSense
(Razor/Umbraco HTML intellisense), and Umbraco Log Viewer.

Runtime logs (Serilog) land in `umbraco/Logs/UmbracoTraceLog.*.json` —
open one with the Umbraco Log Viewer extension rather than reading the raw
JSON when diagnosing a failed migration startup handler or a ClickUp sync
error; it's the fastest way to find the failing event without grepping
message templates by hand.

### Entry points and navigation

- **`/`** is `Views/NoContent.cshtml`, wired through
  `Umbraco:CMS:Global:NoNodesViewPath` — Umbraco serves it only while no
  content is published, so a published root node (e.g. the blog template)
  takes `/` back with no code change. Umbraco serves that view for *every*
  unmatched URL meanwhile, so it answers 404 with a "Page not found" body
  for any path but `/` (`PublicNotFoundIntegrationTests`). Its copy is public
  marketing: check it against `docs/commercial/claim-register.md`.
- **Sign-in with no return URL goes to `/staffops/home`**, which redirects by
  capability via `Services/Staff/StaffLandingPage` (reporting roles → the
  Review, i.e. the Evidence Check; Staff → My Work while self-service is
  switched on, otherwise `/staffops/start`; Platform Admin → tenant console). It
  runs on the request *after* sign-in because the new member cookie isn't
  visible inside the login POST. Don't point sign-in at a fixed page again —
  a Platform Admin holds no tenant capability and every fixed page refuses them.
- **`/Account/AccessDenied`** is `AccessDeniedController`. Keep that path:
  Umbraco's member cookie scheme sends every `Forbid()` there, and both
  `SecurityEventMiddleware` and the persona harness recognise a denial by it.
- **The core is five nav items: Review, History, Programmes, Data, Settings**
  (Data and Settings have their own sub-navs, `Programme/_DataNav` and
  `Settings/_SettingsNav`). Each link is gated on the same capability (and plan
  feature) the controller enforces. A search box sits in the top bar
  (`StaffSearchController`, `PortfolioSearchService`).
- **Everything else is a benched module** (`Models/Tenancy/ProductModule`):
  off for every tenant and every role until an Admin switches it on in
  Settings → Modules (`StaffModulesController`, table `Tenancy_ModuleSwitch`).
  A benched controller declares `[RequireModule(ProductModules.X)]` (404 while
  off); a core view that links into a module checks `IModuleGate` first; the
  layout's Modules group lists only what is on. There is deliberately no
  configuration key that switches a module on. `Architecture/ModuleBenchTests`
  makes every new `/staffops` controller declare which side it is on — keep
  the core small, and give a reason tied to the loop before adding to it.
- The layout loads `wwwroot/js/app.js` — without it the mobile
  nav can't open and stacked tables lose their labels.
- **The product name is configuration** (`Product:Name`,
  `Models/Branding/ProductBrandOptions`, injected into every view as
  `ProductBrand` from `Views/_ViewImports.cshtml`). Never write a product
  name — or "Hwb", which is the Welsh Government's schools platform — in a
  view or a file under `wwwroot/css` or `wwwroot/js`: `ProductNameTests`
  fails the build. Code identifiers and the Data Protection application name
  stay `ProgrammePulse`; renaming those would break stored credentials.
- **Capacity views leave out oversight-only roles** (`StaffRole.OversightOnly`:
  Board, Analyst, Platform Admin) when the person recorded no time — see
  `Services/Staff/DeliveryRoleDirectory`. They are named on the page, never
  dropped silently; Admin stays counted.

### Accessibility, devices and report sharing

Target is WCAG 2.2 AA on phone, tablet and desktop. **Read
[`docs/design-system.md`](docs/design-system.md) and
[`docs/accessibility.md`](docs/accessibility.md) before changing layout, colours
or tables**, and run `scripts/accessibility/audit.mjs` (axe-core at three
widths, page overflow from 320px to 1440px, and any table wider than its card
from 1024px up; exits non-zero on failure) after UI changes, against a site
with the Northstar demo loaded ([`docs/demo-data.md`](docs/demo-data.md)).
In short: tables wrap and numbers don't; a link styled as a button is
secondary; related pages are a sub-nav, not a row of buttons; user-facing copy
never cites a doc or an internal type. Give
every table a `<caption>`; check new colours at 4.5:1; keep CSS logical
(`inline-start`/`end`). Report pages opt into print/PDF, copy-link and CSV via
`ViewData["ReportActions"]` / `ViewData["ExportUrl"]`
(`Views/StaffOps/_ReportActions.cshtml`). There is deliberately no
unauthenticated share link — a copied link still needs the recipient's own
sign-in and role.

### Multilingual UI

Offered languages, formats, markets, currencies and time zones are
configuration — the `Localization` section, bound to
`Services/Localization/LocalizationSettings` and validated at startup (a bad
entry refuses to boot; a language with no translation file logs a warning).
**Read [`docs/localization.md`](docs/localization.md) before touching any of
it.** Never hardcode a culture list: `MarketConfiguration`, the language
switcher (`Views/Shared/_LanguageSwitcher.cshtml`) and the executive review
market form all read the settings.

A request's language is the culture cookie (set via `/language`), else the browser's language matched
by language rather than exact tag (`BrowserLanguageRequestCultureProvider`),
else `DefaultCulture`. There is deliberately no query-string provider.
Layouts set `lang`/`dir` from `Services/Localization/CultureMarkup`.

Text goes through `@Localizer[...]` against `Resources/SharedResource.resx`
(English, and the key list translators work from) plus one
`SharedResource.{culture}.resx` per language. Welsh currently covers every
English key; keep it that way when adding keys, and never add a key to a
translation file that English lacks (a test fails the build).
`@inject IStringLocalizer<SharedResource> Localizer` lives in
`Views/StaffOps/_ViewImports.cshtml`. 26 of 79 views are translatable; the
Programme, My Work, Contracts, Skills, Branding and Admin views are still
English-only — extend one area at a time.
