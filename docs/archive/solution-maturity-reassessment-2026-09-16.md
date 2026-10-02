# Solution maturity, gap analysis and GTM reassessment

16 September 2026 — second review of the working tree after the trust-foundation implementation. This supplements [the original assessment and backlog](platform-mapper-gtm-review-2026-09-16.md); it does not mark its proposed work complete by default.

**Update, later the same day:** the three findings below marked lease
takeover, publication language and identity ambiguity have been fixed and
verified — see "Update: the three priority findings, resolved" after the
findings table for the evidence. The register rows are R17 (lease, now
including takeover fencing), R36 (publication language) and R37 (identity
ambiguity) in [`release-readiness.md`](../release-readiness.md). Everything
else in this document — customer isolation, mapping breadth, onboarding
and commercial validation — is unchanged and still open.

**Overall judgement: an advancing engineering alpha, approaching an assisted single-customer pilot. Shared multi-tenant SaaS and a general multi-platform mapper remain unready.** The update improves reporting semantics and operator visibility substantially. Customer isolation, source ownership, independent onboarding and commercial validation have not advanced to the same degree.

The next investment should complete and prove the foundations already started, followed by a customer journey that works without a particular vendor. More connectors alone will not resolve the current launch risks.

## Evidence and maturity

This review examines the current staged/unstaged/untracked solution, not just committed HEAD. It inspected repositories, mappers, controllers, migrations, views, tests and CI. No customer API credentials were used, no production deployment was tested, and no customer research was performed. The release register records 389 tests and LocalDB migration/SQL smoke checks from the previous implementation session; those checks are useful but do not prove live connector behaviour or stale-worker exclusion.

Ratings below describe evidence stages, not numerical completeness: **implemented** means code exists; **partial** means the end-to-end outcome has material gaps; **unproven** means release/customer evidence is missing.

Fresh verification during this review: `dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false` built the application/test project and passed **389 tests, 0 failed, 0 skipped**. The initial solution-wide invocation produced no output; cancellation was requested but termination was not confirmed. The explicit single-node project invocation completed successfully. This does not exercise live APIs, a real two-worker lease takeover or a production migration.

| Dimension | Current maturity | Change and remaining gate |
|---|---|---|
| Application architecture | Implemented foundation | Bronze/Silver/Gold boundaries remain useful; keep the modular monolith |
| Reporting semantics | Improved, partial | PlannedAllocation separates bookings from delivery; dated capacity and source hour semantics still need validation |
| Identity resolution | Improved, partial | Explicit links, unresolved queue and now ambiguity handling + link uniqueness exist ([resolved below](#update-the-three-priority-findings-resolved)); tenant/account-scoped identity does not |
| Sync operations | Improved, partial | Persistent runs, database leases and now takeover fencing exist ([resolved below](#update-the-three-priority-findings-resolved)); durable (queued/background) execution remains open (R18) |
| Multi-tenant security | Incomplete release blocker | Programme/contract/reporting data and new identity/run data remain global; fail-closed feature gates do not isolate data |
| Multi-platform mapping | Partial architecture, narrow implementation | ClickUp and Hub Planner adapters exist; no implemented Jira/HubSpot path, generic connection registry or versioned mapping/replay workflow found |
| Independent starter experience | Missing | CSV reporting exports exist; a CSV import/manual delivery onboarding journey was not found |
| Deployment and assurance | Partial | Build/test/security CI exists; publish/deploy/restore evidence and production operating envelope remain gaps |
| Customer adoption and economics | Unproven | No buyer validation, willingness-to-pay, onboarding-cost or retention evidence established by this review |

## What has materially improved

- Hub Planner now maps bookings to [PlannedAllocation](../../Services/Integrations/HubPlanner/HubPlannerMappingService.cs), without deriving delivery completion from dates. The migration removes legacy booking WorkItems. This addresses the original semantic defect at code level; upgrade behaviour still needs representative-data testing.
- [StaffIdentityResolver](../../Services/ProgrammeOps/StaffIdentityResolver.cs) supports explicit links and records unmatched identities. The admin queue gives operators a repair action; cost/capacity gaps are more visible. Identity links also participate in staff data export/erasure.
- [SyncRunCoordinator](../../Services/Integrations/Resilience/SyncRunCoordinator.cs) and [SyncRunRepository](../../Services/ProgrammeOps/SyncRunRepository.cs) add persistent lifecycle records and conditional database lease acquisition. These are meaningful operational building blocks.
- [TenantFeatureGate](../../Services/Tenancy/TenantFeatureGate.cs) now refuses an unresolved tenant and distinguishes it from a plan restriction. This closes the previously identified fail-open decision.
- The programme header exposes source/run freshness and workload/planned-allocation coverage. This begins to deliver the common management layer described in the original proposition.

## Findings that change the release decision

| Finding | Evidence and failure scenario | Required acceptance evidence |
|---|---|---|
| **Critical: customer isolation remains incomplete** | ProgrammeRepository still fetches global tables. StaffIdentityController uses global identity/staff lookups and unscoped link keys. New identity and sync repositories have no tenant/connection boundary | Real database/API tests for two tenants, identical upstream IDs, guessed entity keys, exports, files, audits and jobs; no cross-tenant access or mutation |
| **High — FIXED: lease takeover does not fence out the old worker** | HeartbeatAsync silently returns when its conditional update affects zero rows. The handle continues processing. CompleteAsync updates by run key without requiring current ownership. A paused worker can resume after another instance acquires its expired lease and write/complete stale work | Two-worker SQL-backed test: A pauses, B takes over, A resumes and cannot write or publish. Propagate lease loss, cancel processing and validate a lease generation/token at writes/publication; check release against the run identity |
| **High — FIXED: publication language is stronger than the implementation** | Programme/Index says “Showing the last successful publication” after failure, but syncs commit individual Silver upserts and Gold reads current tables. A failed run can leave changed records visible beside the old success timestamp | Either stage/version data and publish atomically, or label the view as current data potentially containing partial updates. Test a failure after one changed item and compare displayed rows and freshness claims |
| **High — FIXED: identity matching still accepts ambiguity silently** | Resolver uses GetAllAsync and dictionary TryAdd for email/source links; duplicate email candidates keep the first. ExternalIdentityLink has uniqueness on LinkKey, not a connection-qualified external identity | Unique tenant/connection/user links, ambiguous-candidate queue, explicit conflict resolution, inactive-person handling and concurrent-link tests |
| **Medium — IMPROVED: zero unresolved rows is not proof of complete matching** | Resolver returns null without a sighting when both ID and email are absent; queue records can age out. The header nevertheless says every person is matched when its count is zero | Run-scoped coverage denominator and separate missing-identifier/unknown counters; never-synced, expired-queue and partial-run cases remain visibly incomplete |
| **Medium: planned window totals need precise labelling** | BuildPlannedAllocations includes overlapping bookings then sums their whole recorded hours. A booking spanning beyond the window contributes its entire value | Label as total hours on bookings overlapping the window, or calculate verified daily allocation. Test boundary-spanning bookings, timezone transitions and missing dates |
| **Medium: abandoned runs can remain visibly Running** | SyncStatusQueryService checks status, not heartbeat expiry. Old running rows are failed on subsequent acquisition, so a crash without a later trigger can remain Running indefinitely | Expired heartbeat displays interrupted/stale; recovery reconciles run status without waiting for a user to trigger a new sync |

These are static findings with concrete code paths, not reproduced production incidents. Heartbeats are also outside the ClickUp inner task loop's task-fetch call, so a slow upstream page fetch can extend the interval between renewals — the fix below moves the touch to before every Silver write, including inside that loop, which narrows but does not eliminate that window. Merely increasing the lease timeout cannot prove exclusive ownership; the fencing fix below is what does.

The legacy-booking migration deletes derived WorkItems, dependencies and baselines. Rehearse it against a restored representative database, verify related time/financial records and document rollback/recovery before deployment. Do not infer migration safety solely from a clean boot against an empty or demo dataset.

## Update: the three priority findings, resolved

Fixed the same day as this review, verified against the live LocalDB
database (not fakes alone), and covered by new unit tests. Full detail in
[`programme-ops.md`](../programme-ops.md) ("One run per source at a time" and
"Identity resolution and the identity queue" sections) and register rows
R17/R36/R37 in [`release-readiness.md`](../release-readiness.md).

1. **Lease takeover now fences out the old worker.**
   [`ISyncRunRepository.HeartbeatAsync`/`CompleteAsync`](../../Services/ProgrammeOps/ISyncRunRepository.cs)
   are conditional `UPDATE`s on `(source, ownerInstanceId, runKey)`,
   judged on the database clock (`SYSUTCDATETIME()`, not each node's own
   clock), and both return whether they actually applied. When a heartbeat
   or completion finds it no longer owns the lease,
   [`SyncRunHandle`](../../Services/Integrations/Resilience/SyncRunCoordinator.cs)
   throws `SyncLeaseLostException` and the sync stops; both
   `ClickUpSyncService` and `HubPlannerSyncService` now call `TouchAsync`
   immediately before every Silver write (including inside the
   task-per-list loop), so the stale-write window is bounded to a single
   pause rather than the whole run. `FailAsync`/`CompleteAsync` in the
   repository only transition a still-`Running` row, so a worker that
   raced past the takeover cannot overwrite the new owner's abandoned
   marker or its live run either.
   **Evidence:** five new scenarios in `SyncRunCoordinatorTests`
   (touch-after-takeover, complete-after-takeover, fail/dispose-after-
   takeover, and a merely-slow-but-still-heartbeating worker that
   completes normally); a new `ClickUpSyncServiceTests` case drives a
   real mid-run takeover through the sync service and checks nothing was
   written after the loss. An 8-step two-instance SQL smoke was run
   against the LocalDB `GardnerDB\UmbracoBase` database (acquire, refuse
   while held, heartbeat while owner, takeover of an expired lease, the
   old owner's heartbeat/complete/fail/release all refused post-takeover,
   the new owner completing normally) — every step matched its expected
   row count; rows removed after.

2. **The management header no longer claims more than the pipeline
   guarantees.**
   [`SourcePublicationStateViewModel`](../../Models/ViewModels/ProgrammeOverview/SourcePublicationStateViewModel.cs)
   gained `CurrentDataMayBePartial` (true whenever the latest run is
   `Running` or `Failed`) and `PartialDataNote`, which names the last
   *complete* publication and the failed run's stage, or — if nothing has
   ever completed — says the figures are only what the failed run wrote.
   `Views/StaffOps/Programme/Index.cshtml` and `Platform/Tenants.cshtml`
   show that note instead of "Showing the last successful publication."
   **Evidence:** four new/updated `SyncStatusQueryServiceTests` cases
   (failed-with-prior-success, failed-with-no-prior-success, running, and
   succeeded-is-not-flagged-partial) plus the live boot below.

3. **Identity ambiguity is refused, not guessed, and one external
   identity now maps to at most one staff profile at the database
   level.** [`StaffIdentityResolver`](../../Services/ProgrammeOps/StaffIdentityResolver.cs)
   collects every candidate at each resolution step instead of taking the
   dictionary's first `TryAdd` winner; more than one candidate is queued
   as ambiguous with the reason (e.g. "2 staff profiles share this
   email") and counted separately (`AmbiguousCount`), except that a
   single *active* profile among otherwise-inactive duplicates still
   resolves cleanly (a re-hired leaver isn't blocked by their old,
   deactivated record). Sightings with neither a user id nor an email —
   previously silently dropped and invisible to the header's zero count —
   are now counted too (`UnidentifiableSightings`), and the Programme
   Overview coverage line no longer claims "every person … is matched"
   when it cannot know that. Separately,
   [`AddIdentityLinkUniqueness`](../../Migrations/ProgrammeOps/AddIdentityLinkUniqueness.cs)
   adds filtered unique indexes on `(externalSource, externalUserId)` and
   `(externalSource, email)`, and `CreateLinkAsync` throws
   `DuplicateIdentityLinkException` on a conflicting link rather than
   silently accepting a second, contradictory one.
   **Evidence:** seven new `StaffIdentityResolverTests` cases (two-active-
   duplicates ambiguous, single-active-wins-over-inactive, only-inactive-
   duplicates still ambiguous, conflicting explicit links ambiguous, two
   links for the same person not ambiguous, a second link on the same
   identity refused, unidentifiable sightings counted) and a new
   `HubPlannerSyncServiceTests` case driving the ambiguous-resource path
   through the real sync service. A SQL smoke against LocalDB confirmed
   both filtered indexes exist, reject a duplicate user-id pair and a
   duplicate email pair, and still allow two rows with different `NULL`
   columns; rows removed after. Tenant/source-account scoping of identity
   (R35) is unaffected by this fix and remains open.

**Regression check for all three:** `dotnet test
ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --no-restore
--verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false`
passed **403 tests, 0 failed, 0 skipped** (up from 389). A full live boot
against the same LocalDB database applied migration
`2026-09-programmeops-13` cleanly on top of the prior `…-12` state, logged
zero error/fatal events, and returned `/health` 200, `/health/ready` 200,
`/staffops/programme` and `/staffops/programme/identities` 302
(anonymous), `/staffops/contracts` 403 (fail-closed tenant gate intact).
Not exercised: a real two-process concurrent sync against a live
ClickUp/Hub Planner account (no API token in this environment) — the
fencing logic itself was proven against real SQL Server concurrency
control, not simulated in-memory locking, but a genuine two-`dotnet run`
process pair racing a live sync was not run.

## Reconciliation with the earlier backlog

| Earlier IDs | Revised status |
|---|---|
| B02/B03: isolation and tenant connections | Open; primary shared-hosting blockers |
| B04: business semantics | Partially delivered; planned/delivery split implemented, broader canonical definitions and commercial model remain |
| B05: identity and field authority | Partially delivered; people queue/linking, ambiguity handling and link uniqueness now exist ([resolved](#update-the-three-priority-findings-resolved)); tenant/connection-scoped identity and field authority remain |
| B06: durable sync/publication | Partially delivered; run state, acquisition, lease fencing and honest publication now exist ([resolved](#update-the-three-priority-findings-resolved)); queue/checkpoints (durable background execution, R18) remain |
| B08: entitlement/auth/onboarding | Partially delivered; fail-closed decision implemented, full auth integration/onboarding outcomes remain |
| B09/B10: mapping replay and standalone onboarding | Open |
| B11/B12: Jira/HubSpot | Open; no adapter implementation found in the reviewed application paths |
| B13/B14: existing adapters and management header | Partially delivered; semantics/header improved, live reconciliation and trustworthy coverage remain |
| B07/B15/B16/B17: release, onboarding, governance and commercial validation | Partial foundations or unproven outcomes; identity export/erasure is an improvement, not completion of governance across all data |

## Recommended next delivery increments

| Order | Work package | Accountable owner | Exit evidence |
|---|---|---|---|
| 1 | ~~Correct publication and coverage claims~~ — done; qualify planned-window totals (still open) | Engineering/Product | ~~Failure-after-write and no-identifier scenarios show accurate user-facing statements~~ — done; overlapping-booking window labelling still open |
| 2 | Tenant isolation and tenant-owned source connections as one schema/security change | Architect/Engineering | Two-tenant database/API suite, migration backfill rehearsal and reviewed authorisation matrix |
| 3 | ~~Lease-loss fencing~~ — done (SQL-backed takeover test proved no stale worker can publish); persistent queued execution and stale-run recovery (R18) still open | Engineering/Ops | ~~Paused-worker takeover test against SQL~~ — done; restart/cancellation of a queued background run still needs that R18 work first |
| 4 | ~~Connection-qualified identity~~ → done at the deployment level (duplicate review, uniqueness); tenant/source-account scoping specifically still needs R35. Versioned mapping and replay remain open | Engineering/Data | ~~Ambiguous matches require decisions~~ — done; mapping changes traceable/reversible still open |
| 5 | CSV/manual starter journey and guided source/capability selection | Product/UX/Engineering | A customer with neither Jira nor HubSpot produces a trusted portfolio view without code/SQL changes |
| 6 | Live source reconciliation and upgrade/restore/deploy rehearsal | QA/Ops | Known records reconcile to source; privacy/retention checks cover new data; supported scale and refresh cadence measured |
| 7 | Two design-partner pilots with baseline measurement | PM/Commercial | Weekly usage, reporting time saved, setup/support effort and willingness-to-pay recorded |
| 8 | Jira then HubSpot, or reverse if validated buyer demand warrants it | Product/Engineering | A customer commitment is explicitly linked to delivery evidence with permissions/capability fallback tested |

Orders 1–4 are the dependency spine for shared SaaS. Customer discovery can run immediately alongside them. A dedicated-customer pilot may precede complete shared-tenancy work only with an isolated deployment/database/storage/credentials and a narrower proven scope. Single-node operation contains the multi-node lease risk but does not resolve publication or semantic defects.

## GTM position and offer

The credible near-term offer is **an assisted programme-assurance pilot for services teams that assemble portfolio reporting from separate delivery and resource tools**. Its promise should be a clearer weekly view of delivery risk, workload and missing evidence, with operator-supported setup. The cross-CRM commitment-to-delivery promise remains the expansion story until its domain relationships and adapters are implemented.

Do not yet market universal mapping, self-service multi-tenant onboarding, live synchronisation, automated capacity forecasting or a supported Jira/HubSpot integration. The solution currently exposes a useful management header over specific integrations; there is still substantial work between that and a configurable integration product.

Jira and HubSpot remain optional sources. Missing CRM should lead to a manual/imported commitment register; missing delivery software should lead to an imported delivery plan. Missing actual time should disable actual-cost/utilisation claims rather than imply zero. A customer's permissions and subscription must be discovered separately from this product's entitlements: Jira documents permissions-based API access, while HubSpot supports optional scopes for features unavailable to some accounts. See [Jira API documentation](https://developer.atlassian.com/cloud/jira/platform/rest/v3/intro) and [HubSpot scope documentation](https://developers.hubspot.com/docs/apps/developer-platform/build-apps/authentication/scopes).

Treat the buyer and commercial model as hypotheses: PMO/delivery director as buyer, COO as sponsor, assisted onboarding fee plus recurring workspace/service fee. Validate pricing against measured value and delivery cost; no price recommendation is supported by the repository. Record engineering/operator hours per onboarding, monthly hosting cost, connector incidents and support time so bespoke integration work does not consume the recurring margin.

Proposed pilot measures, to agree with buyers: first trusted portfolio view within two working days of usable data/access; at least 50% less weekly report preparation; every unexplained reconciliation difference resolved or explicitly excluded; weekly use by the intended decision-maker; a recorded renewal/purchase decision. These are proposed gates, not achieved results.

| Release decision | Current recommendation |
|---|---|
| Internal demo with controlled data | Go, with accurate labels and limitations |
| Dedicated customer pilot | Conditional; fix trust statements, validate the selected live adapter, rehearse deployment/recovery and agree support/data scope |
| Multiple customers sharing business tables | No-go until tenant and connection isolation is proved |
| Broad/self-service launch | No-go until independent onboarding, reliable operations, supported integrations and repeatable paid adoption are demonstrated |

The previous 7/10 release score is not useful as an overall product maturity score. Security isolation and data correctness are gates, not weaknesses that can be averaged away by feature breadth. The strongest next milestone is a demonstrably trustworthy, repeatable pilot journey with a measured operating cost.

## Update, 18 September 2026: tenancy closure verified

Since the update above, commit `8ffb2e8` ("Isolate ProgrammeOps data by
tenant and add a source connection registry") landed the B02+B03 increment
this document's recommended order placed second, after the R36/R37 trust
fixes. This is a fresh, independent verification of that commit, not a
re-reading of its own message.

**Fresh verification performed for this update:** a clean `dotnet build`
(0 errors, 0 warnings), the full test suite (**405 tests, 0 failed**, up
from 389 at the last review), and a real boot against LocalDB —
`ProgrammeOps` reached migration step `…-16`, `Tenancy` `…-02`, zero
error/fatal log lines, `/health` and `/health/ready` 200,
`/staffops/programme` 302 (anonymous), `/staffops/contracts` 403,
`/staffops/platform/tenants` 302.

**Spot-checked against the code, not just the commit message or the
register:**
- `IProgrammeOverviewQueryService.BuildOverviewAsync`, `IProgrammeRepository`
  and `ISyncRunRepository` now all take a `tenantId` parameter — the Gold
  layer and the sync lease/run tables are genuinely threaded, not just
  documented as such.
- `StaffContractController`'s diff shows Contract Ops's *reads of
  ProgrammeOps data* (customers, commercial summary) now resolve and pass
  the caller's real tenant instead of a hardcoded default — closing a real
  cross-tenant leak (tenant A's contracts could previously see tenant B's
  programme data).
- `ContractOps_Contract` itself still carries no `tenantId` column —
  confirmed by reading the DTO — so Contract Ops's *own* data (contract
  terms, documents, invoices, margin) remains global across tenants. This
  is the register's own claim (R12: "Open… for Contract Ops"); the code
  confirms the register is not overstating what closed.
- `StaffApprovalsController` has no tenant-scoping code at all — confirmed
  by inspection — matching the register's "Approvals still global" claim.
- Per-tenant source credentials remain explicitly out of scope: every
  tenant still authenticates ClickUp/Hub Planner through one
  deployment-wide token. This is a real, honestly-flagged blocker for
  shared hosting, independent of the schema work.

**Assessment: this is genuine, well-sequenced progress, not scope creep.**
The engineering did what the previous version of this document recommended
— isolate the widest-read surface first (Reporting Hub/Programme
Overview), keep the register honest about what's still open rather than
declaring victory, and verify with tests and a real boot rather than
inference. Two cross-tenant-leak regression tests were added alongside the
change, consistent with this project's practice of testing the failure
mode, not just the happy path.

| Dimension | Revised maturity | Change |
|---|---|---|
| Multi-tenant security | **Substantially improved, two named gaps** (was: incomplete release blocker) | ProgrammeOps (the widest-read surface) is now tenant-scoped end to end, including the sync lease/run and identity tables. Contract Ops's own tables and Approvals remain global; source credentials remain shared. The most commercially sensitive data (contract terms, invoices, margin) is in the *unclosed* half — this is not yet a shared-tenancy release blocker fully lifted |

This does not change the release recommendation already in
`release-readiness.md` (conditional go for an operator-provisioned pilot,
no-go for self-service multi-tenant SaaS) — that document's own gating
list (R13, R14, Contract Ops scoping, credential isolation) is accurate
and I concur with it. I am deliberately not revising the 7/10 score: this
document already said that score is not a useful single maturity number,
and inflating it for one closed area while the single most sensitive
remaining area (commercial/contract data) is still global would repeat
exactly the kind of overstatement this review exists to catch. **Verdict:
yes, this is the right direction** — real isolation work, correctly
prioritised, honestly reported, independently verifiable.

## Update, 18 September 2026: evaluating a proposed capacity/skills feature

The user proposed a specific new-business scenario as a candidate feature:
*"A new customer wants N days of a given type of work by a date — do we
have the capacity, what are the pinch points, and what are the skill
gaps?"* — delivered via a skills matrix plus an MCP (Model Context
Protocol) integration so it can be asked conversationally. This is a
genuine product judgement call, evaluated here against what the codebase
actually contains, not in the abstract.

**Why this is worth taking seriously.** It is squarely on-thesis for the
GTM position already stated above: "a clearer weekly view of delivery
risk, workload and missing evidence." A trustworthy answer to "can we take
this on" is exactly the kind of decision a PMO/delivery director buyer
currently makes from a spreadsheet and gut feel. It also needs no new
integration — it would run on data the platform already collects
(work-item estimates, bookings, the staff roster), not a fourth source
connector.

**What already exists to build on, and what is genuinely missing.**
`StaffProfile` carries `JobTitle`/`Department`/`Team` — single free-text
fields, not a skills taxonomy, and no proficiency levels or many-to-many
tagging. `StaffOps_StaffRate` is cost-only. `StaffOps_Availability`
currently only reflects approved leave (`Source = LeavePolicy`), not
partial commitments. `WorkItem.EstimatedHours` exists, but
`ProgrammeOverviewQueryService.BuildResourceCapacity` is a **current
snapshot** — total outstanding estimated hours per person right now — not
a date-scoped calculation of hours available between today and a target
date net of existing commitments. `PlannedAllocation` (Hub Planner
bookings) is the closest thing to a forward calendar, but this
document's own earlier findings already flag its hour semantics as
unverified against a live account and its window totals as needing more
precise labelling — both still open. One encouraging detail: ClickUp
custom fields are already captured verbatim in Bronze
(`ClickUpTaskDto.CustomFields`) and the mapper already reads one of them
("Milestone") into Silver — proving the pattern for a cheap,
source-provided "type of work" tag on work items without inventing a
parallel taxonomy, though it says nothing about which *staff* can do that
type of work.

**Judged as three separable pieces:**

1. **A skills matrix (new data).** Real, bounded, architecturally
   unremarkable work: a new `StaffOps_StaffSkill`-style table (tenant-scoped,
   following the existing pattern), an admin UI to maintain it, and a
   decision on how it gets populated — admin-entered is the safest start;
   self-report or inference from historical ClickUp tags are both
   plausible later enrichments but add data-quality risk on day one, so
   don't start there.
2. **A date-scoped feasibility calculation (new logic).** The harder,
   more important half. It has to combine availability, existing open and
   planned commitments, and (once it exists) a skill filter into "hours of
   this type free before this date" — a new Gold-layer query, consistent
   with the Bronze/Silver/Gold discipline the rest of the codebase follows.
   **This must not be built on top of the planned-window and Hub Planner
   hour-semantics gaps this document already flagged as open** — a
   feasibility promise is a stronger claim than a coverage dashboard, and
   shipping one on numbers already known to need better labelling would
   repeat the exact "claims stronger than the implementation" defect the
   R36 fix just corrected elsewhere. Fix the inputs' trustworthiness
   first, or the feature launches already carrying the same defect class.
3. **MCP integration (delivery, not data).** Architecturally low-risk
   *if* it is built as a thin wrapper that calls the same tenant-scoped
   Gold-layer service the web UI would call — `ITenantContext` and the
   already-verified per-tenant repository methods extend to a new caller
   for free. The one hard rule: an MCP tool must not open a shortcut data
   path, and must not touch Contract Ops or Approvals data until those
   close their own tenant scoping (this feature itself doesn't need
   either, since it draws only on the now-scoped Staff Ops and ProgrammeOps
   domains). It should also surface the same honesty signals the dashboard
   now does — unresolved/ambiguous people, stale or partial sync, missing
   estimates or skill tags — as structured output, not just a confident
   "yes" or "no," for the same reason `PartialDataNote` exists on the
   Programme Overview page. This document's GTM section already says not
   to market "automated capacity forecasting" yet; an MCP tool that
   answers "can we hit this date" *is* that claim, so it inherits the same
   caution.

**Recommendation: a good addition, sequenced last, not first.** Build and
prove the plain feasibility view before the conversational layer:
first the skills tags and a date-scoped capacity query as an ordinary
dashboard/API, validated against real data and, ideally, one of the
planned design-partner pilots; only then add MCP as a cheap, low-risk
finishing layer over service calls that are by then trusted. This has no
dependency on the still-open Contract Ops/Approvals tenant work, but it
does depend on fixing the planned-allocation trust gaps already on the
backlog above. Building the fluent conversational answer before the
underlying numbers are proven would ship a confident-sounding wrapper
around data nobody has yet validated — the specific failure mode this
project's trust-foundation work exists to prevent.
