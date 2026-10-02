# Programme Ops: ClickUp integration & Programme Overview

Phase 2 of the Staff Operations domain. Adds a medallion-architecture
pipeline (Bronze → Silver → Gold) that turns ClickUp task data into the
Programme Overview page at `/staffops/programme`, without ever exposing
staff cost/rate data on that page.

## Layers

- **Bronze** — `Services/Integrations/ClickUp/`. `ClickUpApiClient` calls the
  ClickUp REST API and captures every response verbatim into
  `ProgrammeOps_RawClickUpPayload` (one row per space/folder/list/task/
  member/time-entry) before anything else touches it. Nothing outside this
  folder is allowed to reference `IClickUpApiClient` or the
  `Models/Integrations/ClickUp/Raw/*` DTOs.
- **Silver** — `Models/Programme/*` (Programme, Project, Workstream,
  WorkItem, Dependency, Risk, Issue, AuditLog) plus
  `Services/ProgrammeOps/ProgrammeRepository`. Source-agnostic on purpose: a
  future calendar integration would add its own Bronze capture and mapper
  and write into these same tables — Silver and everything above it would
  not change.
- **Gold** — `Services/ProgrammeOps/ProgrammeOverviewQueryService`. Reads
  only Silver (plus the existing Staff domain for capacity), builds
  `ProgrammeOverviewViewModel`, and is the only thing
  `StaffProgrammeOverviewController` renders. It never depends on Bronze or
  the ClickUp client.

`ClickUpMappingService` (`Services/Integrations/ClickUp/`) is the one place
Bronze DTOs and Silver models both appear — it's the seam between the two
layers.

## How the ClickUp sync works

1. An Admin clicks "Run ClickUp sync now" on `/staffops/programme`, which
   POSTs to `StaffProgrammeOverviewController.Sync`.
2. `ClickUpSyncService.RunAsync` fetches spaces → folders → lists (+
   folderless lists, grouped under a synthetic "Unsorted" project) → tasks
   for the configured workspace, and separately fetches members and time
   entries.
3. Every entity's raw JSON is saved to `ProgrammeOps_RawClickUpPayload`
   (Bronze) before `ClickUpMappingService` upserts the matching Silver row,
   keyed by (ExternalSource, ExternalId) so re-running a sync updates
   existing rows instead of duplicating them.
4. A summary (`ProgrammeOps_AuditLog`, action `SyncCompleted`) records who
   triggered the sync and the counts produced.

Member payloads are captured to Bronze only — there's no Silver "member"
entity, since staff identity already lives in the Staff domain and is joined
by email (see `ClickUpMappingService`). Time-entry payloads are captured to
Bronze *and* mapped into Silver (`ProgrammeOps_TimeEntry`) — see the
Reporting Hub section below.

## How to run a local sync

1. `dotnet user-secrets set "ClickUp:ApiToken" "<your-clickup-personal-api-token>"`
   (from this project's directory — `UserSecretsId` is already configured in
   the `.csproj`). In any deployed environment, set the `CLICKUP__APITOKEN`
   environment variable instead. **Never** put the token in
   `appsettings*.json` — those files are committed to source control.
2. Set the non-secret `ClickUp:WorkspaceId` in `appsettings.Development.json`
   (find it in ClickUp under Settings → find your Workspace/Team ID).
3. Run the app, log in as a Member in the `Admin` group, go to
   `/staffops/programme`, and click "Run ClickUp sync now".

## Token rotation

`ClickUp:ApiToken` is a single workspace-scoped **personal** API token
(ClickUp has no first-class "service account" concept — a personal token is
the only kind that exists), generated from a real ClickUp user's own
Settings → Apps page. That has two consequences worth being explicit about
rather than silently living with:

- **It's tied to a person, not a role.** If whoever generated it leaves the
  organisation or is deprovisioned, the sync breaks — there's no
  independent "integration user" to fall back to.
- **It's long-lived by default.** ClickUp personal tokens don't expire on
  their own, so "rotate it" has to be a deliberate, scheduled action, not
  something that happens automatically.

Until a second ClickUp write path exists (today the integration is
read-only — see the medallion layering below), a full service-account
migration is more machinery than the risk justifies. The interim policy:

1. **Rotate every 90 days**, or immediately if whoever generated the
   current token leaves the organisation or the token is suspected exposed
   (checked into a branch, pasted somewhere it shouldn't be, etc.).
2. **Rotation steps**: generate a new personal token from ClickUp Settings →
   Apps, update it via `dotnet user-secrets set "ClickUp:ApiToken" "<new>"`
   for local dev or the `CLICKUP__APITOKEN` environment variable in any
   deployed environment (see below — never in a committed file), confirm a
   sync still runs, then revoke the old token from the ClickUp side.
3. **Move to a scoped service account** the moment the integration grows a
   second write path (e.g. writing status changes back to ClickUp, not just
   reading) — a shared personal token writing on someone's behalf is a
   materially different risk than one only ever used to read.

## Role model

Reuses the four Umbraco Member Groups from Phase 1 (`Models/Staff/StaffRole.cs`) —
no new roles were added:

| Group            | Can see Programme Overview | Can run a ClickUp sync | Can see cost/rate data |
|------------------|:---------------------------:|:-----------------------:|:------------------------:|
| Admin            | ✅                           | ✅                       | ✅ (via `/staffops/admin`) |
| Holiday Approver | ❌                           | ❌                       | ❌ |
| Team Lead        | ✅                           | ❌                       | ❌ |
| Staff            | ❌                           | ❌                       | ❌ |

The Programme Overview page and its view model never carry a cost/rate
field at all — the gate isn't "hide the column in the view," it's "the Gold
query never fetches `StaffRate` in the first place." Cost data stays
entirely inside the existing `/staffops/admin` pages from Phase 1.

`IStaffRoleAssignmentService.GetForCurrentMemberAsync()` projects the
signed-in member's group membership into a `StaffRoleAssignment` — it's the
canonical model the spec calls for, but it can only answer for whoever is
currently logged in (`MemberManager` has no "check this other member's
groups" API). Looking up an arbitrary staff member's roles would need
`IMemberService` group queries instead; that's a Phase 3 extension point,
not implemented here.

## Which settings are admin-only

- `ClickUp:ApiToken` (user secret / environment variable) — never in a
  committed file, and only read by `ClickUpApiClient`.
- The "Run ClickUp sync now" action — gated `IsAdminAsync()` in
  `StaffProgrammeOverviewController.Sync`.
- Staff cost/rate rows (`StaffOps_StaffRate`) — unchanged from Phase 1,
  gated in `StaffAdminController`.

`ClickUp:BaseUrl` and `ClickUp:WorkspaceId` are not secrets and live in
`appsettings.json`.

## Reporting Hub (Phase 3)

`/staffops/reporting` (`StaffReportingController`) is a second Gold-layer
consumer of Silver, alongside `StaffProgrammeOverviewController`. It adds:

- **`TimeEntry`** (`ProgrammeOps_TimeEntry`) — the Silver entity the
  extension point below used to describe as missing. Mapped from ClickUp
  time entries the same way `WorkItem` is mapped from tasks: correlated to a
  `WorkItem` via the entry's task id (`IProgrammeRepository.GetWorkItemByExternalIdAsync`)
  and to a `StaffProfile` via the same best-effort email join
  `ClickUpMappingService` already used for task assignees. `StartedAtUtc` can
  be null (a manually-created ClickUp entry can omit a start time) — entries
  without it are excluded from period-based capacity/cost reporting but still
  count toward total effort-variance hours.
- **`Customer`** (`ProgrammeOps_Customer`) — deliberately **not**
  ClickUp-sourced. Nothing in ClickUp's data model represents a customer
  (Space maps 1:1 to Programme, nothing above that), so this is
  admin-authored: create one and optionally assign a Programme to it from the
  Reporting Hub page. Unassigned programmes roll up under "(no customer)".
- `ReportingQueryService` builds: effort variance (estimated vs logged hours
  per workstream), contributor capacity/residual/utilisation (baseline hours
  from `StaffProfile.DefaultWorkHoursPerWeek`, minus non-`Available`
  `Availability` rows in the period, minus logged `TimeEntry` hours), and the
  customer rollup. It reuses `IProgrammeOverviewQueryService.BuildOverviewAsync()`
  for the blocked/overdue KPI cards rather than duplicating that logic.
- **Overdue has one definition**, `WorkItemDueDate.IsOverdueOn`
  (`Models/Programme`): open, a status that can be read, and a due date
  before today in whole UTC days. The overview, programme health, My Work
  and the Evidence Check all call it; before 30 September they counted
  three ways and the Northstar demo showed 14 on one page and 9 on the next.
  An item past its due date whose status can't be read is counted
  separately (`IsPastDueWithUnreadableStatus`) and named on the overview.
  Contributor, workload and booking lists are sorted by name, never by load.
- **Cost is a separate action and route** (`GET /staffops/reporting/cost`),
  gated `IsAdminAsync()` independently of the main hub page. `BuildCostSummaryAsync`
  is the one method on `ReportingQueryService` that reads `IStaffRateRepository`,
  joining each `TimeEntry` to the `StaffRate` effective on its `StartedAtUtc`
  date (from rate *history*, not just the current rate, so a later rate
  change doesn't retroactively re-cost old entries). It's never called from
  `BuildHubAsync` — the main hub's view model carries no cost field, same
  principle as `ProgrammeOverviewViewModel`.

Role gating for the hub matches `StaffProgrammeOverviewController`: Team Lead
or above. A Team Lead's contributor-capacity rows are scoped to their own
`StaffProfile.Team` (a self-lookup via `IStaffRepository.GetByMemberIdAsync`
on the signed-in member — **not** a general "look up an arbitrary member's
team" capability, which still doesn't exist; see below). Every Staff role
additionally gets `/staffops/my-work` (`StaffPortalController.MyWork`) — own
assigned work items and own logged hours, no cost data, no visibility into
anyone else's work.

`/staffops/reporting/raid` (`StaffReportingController.Raid` and the
`raid/*` POST actions) is the RAID register — Team Lead or above can log a
`Risk` or `Issue` against a project and move its status forward
(`RiskStatus`: Open → Mitigating → Closed; `IssueStatus`: Open → InProgress
→ Resolved). This activates the `Risk`/`Issue` Silver models and
`IProgrammeRepository` methods that existed since Programme Ops Phase 2 but
had nothing populating or rendering them. Built directly against the
repository (no query service) — the same precedent as
`StaffPortalController.MyWork` — since it's thin CRUD/list with no
aggregation logic worth a service layer.

## PMO governance: baseline, change control, stakeholders

Three more `StaffReportingController` surfaces, all Team Lead or above,
added alongside RAID:

- **Baseline** — `POST /staffops/reporting/baseline/lock`
  (`IProgrammeRepository.LockBaselineAsync`) snapshots a workstream's
  current `EstimatedHours` sum into `ProgrammeOps_WorkstreamBaseline` once,
  from a "Lock baseline" button on the Reporting Hub's effort-variance
  table. Deliberately **not** captured automatically on every ClickUp sync —
  a re-locking baseline would erase the exact variance it exists to catch.
  Once a `WorkstreamKey` has a row, later lock attempts are a no-op that
  returns the existing row unchanged (see `ReportingQueryServiceTests`'s
  `LockBaselineAsync_locks_once_...` test). `EffortVarianceRowViewModel`
  carries `BaselineHours`/`BaselineVarianceHours` as nullable — null until a
  baseline exists, alongside the always-populated current-estimate-based
  `EstimatedHours`/`VarianceHours`.
- **Change control** — `/staffops/reporting/governance`
  (`GovernanceViewModel`, `ChangeRequest` Silver model,
  `ProgrammeOps_ChangeRequest`) mirrors the `LeaveRequest` submit → approve
  shape: a request against a `Project` moves `Proposed` → `Approved`/
  `Rejected` → `Implemented` via `DecideChangeRequestAsync`, recording who
  decided and when.
- **Stakeholders (RACI)** — same page, `ProgrammeStakeholder` Silver model
  (`ProgrammeOps_ProgrammeStakeholder`), one row per programme with a
  `StakeholderRole` (Sponsor/Accountable/Responsible/Consulted/Informed).
  `StaffKey` for an internal stakeholder or `ExternalName` for a client-side
  one — exactly one is expected to be set, enforced by the controller (the
  "add stakeholder" form only exposes `ExternalName`; wiring an internal
  `StaffKey` picker is still open, see below).

## Forecast, trend, budget, and dependencies (Phase 2)

Four more additions, all on `StaffReportingController`:

- **Forecast-to-complete** — `EffortVarianceRowViewModel.ForecastAtCompletionHours`/
  `ForecastVarianceHours`, computed in `ReportingQueryService.BuildEffortVariance`
  once a workstream has both a locked baseline and at least one closed work
  item. Simple linear EAC (`EAC = AC + (BAC - EV)`, PMBOK's "remaining work
  finishes at the originally planned rate" variant) — `%complete` is a
  proxy: closed work items ÷ total work items in the workstream, the
  closest thing to a progress measure this data model has without a real
  progress field per item.
- **Trend snapshots** — `IReportingSnapshotService`/`ReportingSnapshot`
  (`ProgrammeOps_ReportingSnapshot`), captured on demand from
  `/staffops/reporting/trend` rather than by a scheduled job — this
  codebase has no recurring-task infrastructure yet, so a real background
  job is a bigger, separately-verifiable piece of architecture than "give
  trend reporting something to show." See `ReportingSnapshotService`'s doc
  comment; wiring it to `RecurringHostedServiceBase` is the natural next
  step once this shape is proven. Carries the same portfolio-wide totals
  `BuildHubAsync` already computes (never touches `StaffRate`). **What a
  snapshot holds is fixed by when it is taken:** effort figures are
  all-time totals and open and blocked counts are as at capture; only
  utilisation covers the period. So a period can be captured only from its
  last day to `CaptureWindowDays` (7) days after; anything else is refused
  with a reason. Before that rule a snapshot taken in September for July
  filed September's figures under July, and the Northstar demo's three
  backdated snapshots were identical (design and PMO data review,
  30 September, B4). Older late captures are flagged on the Trend page.
- **Programme-level budget vs actual** — `Programme.BudgetAmount`/
  `BudgetCurrency` (nullable, admin-set from the Cost Summary page) plus
  `IProgrammeBudgetService`, extending Contract Ops' contract-scoped
  burn-down to a programme that has no formal `Contract` record. Walks
  Programme → Project → Workstream → WorkItem → TimeEntry directly (a
  narrower version of the walk `ContractCommercialService`/
  `InvoiceGenerationService` do from a `Customer`) and reuses
  `TimeEntryCostCalculator`, same currency-mismatch-excluded-and-surfaced
  pattern. Admin-only, lives on the Cost Summary page, not the main hub.
- **Dependency view** — `GovernanceViewModel.Dependencies`
  (`DependencyRowViewModel`), a read-only table on the Governance page of
  every `Dependency` edge with both work items' current stage and whether
  the edge is still "Unresolved" (its `DependsOnWorkItem` isn't
  Done/Cancelled yet) — not a Gantt chart or a graph rendering, just
  presentation over data that already existed.
- The Reporting Hub and Cost Summary now take optional `programmeKey`/
  `customerKey` filters (`BuildHubAsync`'s new parameters), scoping effort
  variance only — contributor capacity keeps its own team-based scoping,
  and customer rollup stays portfolio-wide by design (see
  `ReportingQueryService`'s doc comment).

## Threshold alerts (Phase 4)

`/staffops/reporting/alerts` (`IAlertDetectionService`/`AlertDetectionService`,
`Alert` model, `ProgrammeOps_Alert`) is the in-app notification channel the
build plan needed before threshold alerts could exist at all — see
`AlertDetectionService`'s doc comment for why in-app rather than email or a
chat webhook (this codebase has no outbound-email or webhook
infrastructure yet; building one untested alongside the alerts that would
use it was a bigger and riskier scope than "a minimal channel" implies).

- **Detection runs on every Reporting Hub page load**
  (`StaffReportingController.Index`), not a scheduled job — same
  on-demand-not-scheduled reasoning as the Trend snapshots above. It's
  idempotent: `DetectAndRaiseAsync` checks `GetOpenAlertsAsync` first and
  only raises a new `Alert` if one isn't already open for that
  `(Type, EntityKey)` pair, so visiting the page repeatedly doesn't flood
  the table with duplicates of a still-true condition.
- **Four thresholds today**: a workstream with at least one `Blocked` work
  item (`AlertType.WorkstreamBlocked`, `EntityKey` = `WorkstreamKey`); a
  contributor whose `ResidualCapacityHours` has already gone negative
  (`AlertType.NegativeResidualCapacity`, `EntityKey` = `StaffKey` —
  `ContributorCapacityRowViewModel` gained a `StaffKey` field for this;
  it didn't carry one before); a contributor whose primary-allocated open
  work due in the next 14 days already exceeds their remaining capacity in
  that window (`AlertType.UpcomingOverAllocation`, `EntityKey` = `StaffKey`
  — see `AlertDetectionService.DetectUpcomingOverAllocationsAsync`); and,
  the opposite direction from the negative-residual check, a contributor
  logging under 50% utilisation for the period despite having meaningful
  available hours (`AlertType.LowUtilisation`,
  `AlertDetectionService.LowUtilisationThresholdPercent`) — a resourcing
  signal (bench time) the other three thresholds don't surface. The
  negative-residual and low-utilisation checks are retrospective (something
  has already happened this period); the upcoming-over-allocation check is
  the only forward-looking one, catching an over-commitment before any time
  is logged against it — the PMBOK "resource levelling" question the others
  don't ask. All four thresholds — and the split-allocation change earlier
  in this document — came out of a PMP/APM utilisation review run against
  this codebase; ask for it if you want the full gap analysis, not just
  what got built from it.
- **Acknowledging** an alert sets `AcknowledgedAtUtc`/`AcknowledgedByStaffKey`
  rather than deleting the row — the table is a permanent record of what
  was raised and when it was dealt with, not just a scratch inbox.
- A Team Lead's page load only computes contributor capacity for their own
  team (see the Reporting Hub section above), so negative-residual
  detection for staff outside that team only happens when an Admin (who
  sees the whole portfolio) loads the page. Not a bug — the same team
  scoping the rest of the Reporting Hub already has.
- Wiring an actual outbound notification (email/webhook) on top of a
  raised `Alert` is the natural next step once real SMTP/webhook
  configuration exists — nothing here precludes it; `AlertDetectionService`
  would just gain a second effect alongside `RaiseAlertAsync`.

## Hub Planner integration (second Bronze source)

A second, independent Bronze source alongside ClickUp — this is the
extension point the section above calls out: *"A second Bronze source
(calendar) writing into the same Silver tables — the reason Silver/Gold were
kept ClickUp-agnostic in this phase."* Here it's Hub Planner (Resource Flow)
instead of a calendar, because the target business for this pitch already
runs Hub Planner for resource scheduling — the sell is richer
reporting/governance/margin on top of their existing Hub Planner data, not a
replacement for it.

**Layout** — mirrors the ClickUp layout 1:1:
`Services/Integrations/HubPlanner/` (`HubPlannerApiClient`,
`HubPlannerSyncService`, `HubPlannerMappingService`) +
`Models/Integrations/HubPlanner/Raw/*` DTOs, capturing to
`ProgrammeOps_RawHubPlannerPayload` before any mapping, same as ClickUp
capturing to `ProgrammeOps_RawClickUpPayload`. Nothing outside this folder
should reference `IHubPlannerApiClient` or the `Raw` DTOs.

**Why the mapping isn't 1:1 with Hub Planner's own data model** — Hub
Planner's shape (Project → Booking, no grouping above Project, no
task-level breakdown) doesn't line up with ClickUp's (Space → Folder → List
→ Task), so three deliberate calls were made:

1. **No Programme-equivalent above Project.** A single synthetic root
   `Programme` ("Hub Planner", `ExternalId = "hubplanner-root"`) is upserted
   once per sync and every Hub Planner Project becomes a Silver `Project`
   under it — the same trick ClickUp's synthetic "Unsorted" project already
   uses for folderless lists, one level up.
2. **Hub Planner `Client` is captured to Bronze only, never mapped to
   Silver `Customer`.** `Models/Programme/Customer.cs` is deliberately
   admin-authored, not synced from any source — see its own doc comment.
   Wiring Hub Planner clients into it would reverse that decision, not
   extend it.
3. **Each `Booking` becomes one `WorkItem`**, inside a single synthetic
   "Bookings" `Workstream` per Project (`ExternalId = "{projectId}-bookings"`)
   — a Booking is a resource-allocation block, not a task, so there's no
   real Workstream grouping to reuse. `Stage` is date-derived (`Done` once
   `booking.end` has passed, `InProgress` otherwise) since a Booking has no
   workflow status; `RawStatus` keeps Hub Planner's `type` field alongside,
   same "keep the raw text next to the normalised stage" pattern
   `WorkItem.RawStatus` already follows for ClickUp. `EstimatedHours` is
   only populated when `booking.state == "STATE_HOURS"` — left `null` for
   `STATE_PERCENTAGE`/other states pending verifying the scale/calendar math
   against a real account. `WorkItemAllocation` gets one row (or none) for
   `booking.resource`, resolved to a `StaffProfile` by email — the sync
   service fetches all Resources first and resolves the email itself before
   calling the mapper, since (unlike a ClickUp task's inline assignee
   email) a Booking only carries a bare resource id.

**Deferred, not silently dropped** — `Timesheet` → `TimeEntry` mapping
(needs the Timesheet schema verified field-by-field first) and the
`billingrate` endpoint (cost data — per the structural cost/rate isolation
rule in `CLAUDE.md`, external rate data needs its own deliberate landing-spot
decision, not a drive-by import alongside a Bronze connector).

**Auth & rate limits** — base URL `https://api.hubplanner.com/v1`, header
`Authorization: <key>` (no `Bearer` prefix). Use a **Read Only** key (Hub
Planner supports scoping a key that way in Settings → API) — this
integration only ever reads, same posture as ClickUp today. Rate limits are
6000 calls/day and a 50-calls/5-second burst limit;
the shared `TransientHttpRetryHandler` in the client pipeline honours
`Retry-After` on a 429 and backs off on 5xx (see "Operational resilience"
below).

**How to run a local sync**:

1. `dotnet user-secrets set "HubPlanner:ApiKey" "<your-hubplanner-api-key>"`
   (from this project's directory). In any deployed environment, set the
   `HUBPLANNER__APIKEY` environment variable instead. **Never** put the key
   in `appsettings*.json`.
2. Run the app, log in as a Member in the `Admin` group, go to
   `/staffops/programme`, and click "Run Hub Planner sync now" (next to the
   existing ClickUp sync button).
3. Same 90-day/leaver/exposure token rotation policy as `ClickUp:ApiToken`
   above applies to `HubPlanner:ApiKey`.

**Not yet verified against a live account** — no Hub Planner API key was
available when this integration was built, so `HubPlannerApiClient`'s
assumption that list endpoints return a bare JSON array (rather than a
named-property wrapper, the way ClickUp wraps `"spaces"`/`"folders"`) and
the exact timezone handling of Booking `start`/`end` timestamps are both
unverified against real responses. Confirm both before relying on this for
an actual pitch demo.

## Operational resilience (sync under real conditions)

Both sync services (`ClickUpSyncService`, `HubPlannerSyncService`) share
one operational contract; the machinery lives in
`Services/Integrations/Resilience/`.

- **Idempotent.** Silver rows are upserted by `(ExternalSource, ExternalId)`
  (`ProgrammeRepository.Upsert*Async`), so re-running after any failure
  converges rather than duplicating. Bronze rows are append-only per run.
- **One run per (tenant, source) at a time, across instances — and a
  taken-over worker is fenced, not just outrun.** `SyncRunCoordinator`
  (scoped) layers two guards: `SyncRunGuard` (singleton, in-process latch,
  still keyed by source alone — see its doc comment for the narrow,
  deliberately-accepted gap this leaves for two different tenants
  triggering the same source on one process at the same instant) fails
  fast without a database round-trip when this process already has the
  source in flight, then `ISyncRunRepository.TryAcquireAsync` takes the
  per-`(tenant, source)` row in `ProgrammeOps_SyncLease` with one
  conditional `UPDATE … WHERE ownerInstanceId IS NULL OR leaseExpiresAtUtc < SYSUTCDATETIME()`
  — no read-then-write, so two instances racing cannot both win, and
  expiry is judged on the *database* clock so skewed node clocks can't
  disagree about who is live. A second trigger while either guard is held
  fails fast with "already running (on this or another instance)".
  The lease is extended by heartbeats (every stage change, and
  `SyncRunHandle.TouchAsync` before every Silver write and at least every
  third of `ProgrammeOps:SyncLeaseSeconds`, default 300) and released on
  completion/failure/dispose; a lease that expires without a release is
  taken over by the next run, which marks the abandoned
  `ProgrammeOps_SyncRun` row Failed. The part that used to be missing:
  every heartbeat and the completion write are themselves conditional
  UPDATEs fenced on `(source, ownerInstanceId, runKey)`
  (`SyncRunRepository.RenewIfOwnerAsync`), so once another instance has
  taken the lease over, the old worker's next heartbeat affects zero rows,
  `SyncRunHandle` throws `SyncLeaseLostException`, and the sync stops —
  it cannot keep writing Silver rows, cannot report "Succeeded", and
  cannot overwrite the abandoned marker the new owner wrote (`FailAsync`
  and `CompleteAsync` both require the row to still be `Running`). This
  closes the gap the September 2026 maturity review flagged: a paused
  worker resuming after takeover used to keep processing and could write
  or complete over the new owner's run. Exercised by
  `SyncRunCoordinatorTests` (five lease-loss scenarios: touch-after-
  takeover, complete-after-takeover, fail/dispose-after-takeover never
  overwriting the abandoned marker or the new owner's lease, and a
  merely-slow worker whose heartbeats keep it live) and a two-instance SQL
  smoke against LocalDB covering acquire/refuse/heartbeat/takeover/
  fenced-heartbeat/fenced-complete/status-guarded-fail/release.
  See "Durable run state" below.
- **Retry / backoff / rate limits** happen inside the typed `HttpClient`
  pipeline, not in the sync loop: `TransientHttpRetryHandler` retries up to
  3 times on 429/408/5xx, connection failures and `HttpClient` timeouts
  (30s per call, set in `ProgrammeOperationsComposer`), never on
  401/403/404/400 (`HttpFailureClassifier`) and never when the caller
  cancelled. Backoff is 1s/2s/4s unless the upstream sends `Retry-After`
  (delta or date), which is honoured — that's how ClickUp and Hub Planner
  both express burst limits — capped at 30s. Hand-rolled, no Polly.
- **Every outcome is audited** to `ProgrammeOps_AuditLog`: `SyncCompleted`
  with counts, or `SyncFailed` with the exception type/message, the stage
  reached (e.g. `syncing space 'Drama'`) and the counts so far. The
  controller surfaces a re-runnable message ("completed work is kept and
  re-running is safe"). The platform console shows the latest of each.
- **Partial failure semantics.** Each upsert is its own transaction, so a
  failure mid-run leaves Silver *consistent but partial*: rows synced
  before the failure are current, the rest are stale until the next
  successful run. Nothing is half-written. Wrapping a whole run in one
  transaction was rejected — it would hold locks across minutes of HTTP.
- **Retention.** After `SyncCompleted`, captures older than
  `ProgrammeOps:RawPayloadRetentionDays` (default 90; 0 keeps forever)
  are deleted for that source, unresolved-identity rows not seen since
  that cutoff are deleted, and finished `ProgrammeOps_SyncRun` rows older
  than `ProgrammeOps:SyncRunHistoryRetentionDays` (default 180) are
  deleted. A purge failure is audited as `RetentionPurgeFailed` and does
  not fail the sync.
- **Replay** from Bronze is *not* implemented — captures are diagnostics
  and a future re-mapping input, not a system of record. Silver keeps the
  mapped data regardless of the purge.

Covered by `ClickUpSyncServiceTests`, `HubPlannerSyncServiceTests`,
`SyncRunCoordinatorTests`, `TransientHttpRetryHandlerTests` and
`SyncRunGuardTests`.

### Durable run state and the management header

`ProgrammeOps_SyncRun` (`Models/Programme/SyncRun`) is the operational
record of every run: `Running` with a heartbeat and current stage while in
flight, then `Succeeded` with a human summary (and the counts as JSON) or
`Failed` with the stage reached and the error. `ProgrammeOps_AuditLog`
keeps the who-triggered-what trail alongside; the two are linked by the
`runKey` in the audit detail. `ISyncStatusQueryService` reads the run table
into one `SourcePublicationStateViewModel` per known source (ClickUp, Hub
Planner): last **complete** publication and its summary, freshness ("2 h
ago"), whether a run is in flight and at what stage, and the last failure
— shown as the **Data sources** panel at the top of `/staffops/programme`
and as the support signals on the platform console.

**What the page claims matches what the pipeline actually guarantees.**
Each sync upserts Silver rows as it goes rather than staging them behind
one atomic publish, so a run that fails — or is still running — can leave
the live tables holding a mix of the last complete publication and
whatever that later run wrote before it stopped. The view model's
`CurrentDataMayBePartial`/`PartialDataNote` say exactly that whenever the
latest run is `Running` or `Failed`: naming the last complete publication
timestamp, noting a failed run's stage, and — if nothing has ever
completed — saying the figures are only what the failed run wrote. The
page no longer says "showing the last successful publication" next to
numbers that can include a failed run's partial writes; that was the
first defect the September 2026 maturity review found. `CurrentDataMayBePartial`
is `false` only when the latest run for that source succeeded, so a
reader who sees the ordinary "Published … ago" line is not also being
asked to second-guess it.

### Planned allocation is not delivery (Hub Planner bookings)

The Hub Planner mapping in this document's earlier section originally
turned each booking into a `WorkItem` under a synthetic "Bookings"
workstream and set `Stage = Done` once the booking's end date passed.
Every Gold consumer that counts Done items (workstream % complete, the
forecast-to-complete's earned-value proxy, customer rollups) therefore
read *time passing* as *delivery progress* — the material reporting risk
the GTM review (`docs/archive/platform-mapper-gtm-review-2026-09-16.md`, B04) flagged.

A booking is now a `PlannedAllocation` (`Models/Programme/PlannedAllocation`,
`ProgrammeOps_PlannedAllocation`): project, resolved staff key (or null),
start/end, `AllocatedHours` only when the source's own unit is hours
(`STATE_HOURS`), `AllocationPercent` for `STATE_PERCENTAGE`, the raw
booking type, and the source resource id so an unresolved allocation can
be traced once an identity link exists. It carries no lifecycle stage and
no Gold code may count it toward Done / % complete. The migration that
added the table (`AddPlannedAllocationTable`) also deleted the legacy
booking work items, their allocations/dependencies and the synthetic
"Bookings" workstreams/baselines, recording the counts in
`ProgrammeOps_AuditLog` as `Migration/LegacyHubPlannerWorkItemsRemoved`
(nothing to delete on the dev database — the integration has never run
live). `HubPlannerSyncService` now reports `Projects`,
`PlannedAllocations`, `UnresolvedResources` and `SkippedBookings`.

On the Programme Overview, **Outstanding Workload (estimate-based)** is
the open-work-item view (with an "items without estimate" coverage
column — missing is not zero) and **Planned Allocation (next 28 days)**
is the booking view: bookings per person overlapping the window, hours
summed only where the source recorded them, and a coverage line
(bookings in window, without a matched active person, without hours,
undated). Neither is a dated forecast, and the page says so.

### Identity resolution and the identity queue

`IStaffIdentityResolver` (`Services/ProgrammeOps/StaffIdentityResolver`)
is the one answer to "which StaffProfile is this external person?" for
ClickUp assignees, ClickUp time-entry users and Hub Planner booking
resources. Order: an explicit `ExternalIdentityLink` on (source, external
user id), then on (source, email), otherwise **null and a recorded
sighting** in `ProgrammeOps_UnresolvedIdentity` (source, user id, email,
display name, where it was seen, first/last seen, occurrence count). The
previous behaviour — silently dropping any assignee whose email didn't
match — made capacity look healthier than it was because people were
simply missing. Scoped per request, so one sync run loads staff and links
once instead of once per assignee.

**An email match is a suggestion, never an attribution** (since 27
September 2026, ProgrammeOps step 27). When exactly one active staff profile
has the person's email (case-insensitive), the resolver still returns null
and records that profile as the queue row's `SuggestedStaffKey`
(`IStaffIdentityResolver.SuggestedCount`). Until then, the email match
attributed the work outright, so anyone able to set a person's email in the
customer's ClickUp, Hub Planner, Jira or import file could put their work
under a colleague's name (Aikido: business logic bypass). The evidence area
has refused the same rule from the start (`EvidenceActorResolver`). On
upgrade, people who were previously matched by email appear on the queue
with a suggestion at their next sync, and their work is unattributed until
an Admin approves them.

**Ambiguity is refused, never resolved by taking the first match.** If a
lookup step (explicit links on a user id, explicit links on an email, or
the roster email match) yields more than one candidate StaffProfile —
two active staff sharing an email, a leaver's old profile plus their
re-hired one, or two conflicting links left over from before uniqueness
was enforced — the resolver does not silently pick one. It queues the
sighting as unresolved with the reason (`"… — ambiguous: 2 staff profiles
share this email"`) and counts it separately
(`IStaffIdentityResolver.AmbiguousCount`), so the sync's result message
and the identity queue both surface it for an explicit link to settle.
The one exception that is *not* ambiguous: if exactly one of several
same-email profiles is active, that one is the suggestion — a leaver's
stale profile does not block their replacement. Sightings with neither a
user id nor an email cannot be queued (there is nothing to key a row on)
but are still counted per occurrence
(`IStaffIdentityResolver.UnidentifiableSightings`), so a "0 unmatched"
header is never a false claim that every person was checked. This closes
the second identity defect the September 2026 maturity review found
(duplicate-email candidates silently resolving to the first).

**One external identity now maps to at most one StaffProfile, enforced by
the database, not just by convention.** `AddIdentityLinkUniqueness` adds
filtered unique indexes on `ProgrammeOps_ExternalIdentityLink`
`(externalSource, externalUserId)` and `(externalSource, email)` — NULLs
excluded, so a link that only sets one of the two still leaves the other
column free for a different link. `IIdentityResolutionRepository.CreateLinkAsync`
checks for a conflicting existing link before inserting and throws
`DuplicateIdentityLinkException` with a clear message
(`StaffIdentityController.Link` turns that into a redirect message rather
than a 500); the indexes are the backstop against two admins racing on
the same queue row. Links are now tenant-scoped (the uniqueness indexes are
`(tenantId, externalSource, externalUserId/email)`, and every
`IIdentityResolutionRepository` method takes the caller's tenant) — a given
deployment can no longer create two contradictory answers for the same
ClickUp user id or email, and two tenants' identical ClickUp user ids no
longer collide with each other's links either.

`/staffops/programme/identities` (`StaffIdentityController`, Admin-only,
audited as `IdentityLink/IdentityLinked|IdentityUnlinked`) lists the
unmatched and ambiguous people with a "link to staff" picker, and, for a
row with a suggestion, a one-click **Approve**. Approval re-checks that the
suggested profile is still active in the tenant and still has the reported
email, and audits `method: ApprovedEmailMatch` (a picker link audits
`Chosen`). Either creates the explicit link and marks the queue row resolved;
the next sync or import uses the link. Links are per source (a
ClickUp user id 42 says nothing about Hub Planner resource 42). A link's
email/user id is the subject's personal data, so `IdentityLinkDataParticipant`
contributes it to the GDPR export and deletes it on erasure (see
`docs/gdpr.md` / `docs/data-governance.md`). Unresolved rows that stop
being seen age out on the Bronze retention window.

## The source-neutral boundary (adding a source)

Bronze is allowed to know a vendor's API shape; nothing above it is. That
rule used to be a convention enforced by review. It is now a seam plus a
test.

`Services/Integrations/Abstractions` holds the seam:

- **`ISyncSource`** — one integrated source as everything above the boundary
  sees it: `Name` (the identity key — it must equal what the source writes
  to `ProgrammeOps_SyncRun.Source` and to `ExternalSource` on its Silver
  rows, because the publication header and the provenance trail join on it),
  `DisplayName`, `FeatureKey` (the plan entitlement), `Capabilities`, and
  `RunAsync` returning a source-neutral `SyncOutcome`.
- **`ISyncSourceRegistry`** — the single list of sources, built from the
  DI-registered `ISyncSource` implementations and ordered by `DisplayName`.
  It replaces three hand-maintained vendor lists that could silently
  disagree: the two hard-coded controller actions, the two hard-coded
  buttons in `Views/StaffOps/Programme/Index.cshtml`, and
  `SyncStatusQueryService.KnownSources`. Duplicate `Name`s throw at
  construction rather than corrupting provenance at read time.
- **`SourceCapabilities`** — a flags enum declaring what kinds of canonical
  data a source supplies (`Work`, `Resource`, `Time`, `Absence`,
  `Commercial`). This exists so a Gold-layer figure can say *no absence
  source is connected, so leave is unaccounted for* rather than reporting as
  though it were. Deliberately **not** five fetch interfaces
  (`IWorkSource`/`IResourceSource`/…): ClickUp and Hub Planner have
  genuinely different fetch shapes, only a source's own orchestrator ever
  fetches, and it already knows its own shape — so a shared
  `GetWorkItemsAsync()` would have exactly one caller per implementation.
- **`IIngestionContext`** — which run and which tenant connection is
  currently writing, so mappers can stamp provenance onto canonical rows.
  Scoped per request and therefore per run. Note the deliberate asymmetry
  with `tenantId`: repositories take `tenantId` as an explicit parameter and
  never read it from ambient state, because `tenantId` is a security
  *filter*. These values are payload — written onto rows, never used to
  decide which rows a caller may see — so getting them wrong produces a
  wrong audit trail, not a tenant leak.
- **`RawEntity<T>`** — the Bronze DTO + verbatim JSON pair. It lived in the
  ClickUp namespace until this change, which forced the Hub Planner client
  to `using` a competitor integration's namespace.

The vendor implementations (`ClickUpSyncSource`, `HubPlannerSyncSource`) are
deliberately thin adapters: all the orchestration — lease, run state,
idempotent upserts, audit, retention — stays in the existing
`ClickUpSyncService` / `HubPlannerSyncService`, which are unchanged. Nothing
outside `Services/Integrations/<Vendor>` references `IClickUpSyncService` or
`IHubPlannerSyncService` any more, except the composer that registers them.

**To add a source (say Jira):** write its Bronze client and mapper under
`Services/Integrations/Jira`, implement `ISyncSource`, add its
`ProductFeature` key to the plan entitlements, and add one
`AddScoped<ISyncSource, JiraSyncSource>()` line to
`ProgrammeOperationsComposer`. The sync button, the `POST
/staffops/programme/sync/{source}` route, the publication header row, the
Admin gate and the entitlement check all follow automatically. No
controller, view, status-service or Silver/Gold change.

**What enforces it.** `ProgrammePulse.Tests/Architecture/SourceIndependenceTests`
asserts mechanically, by reflection over type signatures, that (a) nothing in
`Models.Programme` (Silver) or `Services.ProgrammeOps` (Gold) references a
vendor namespace, and (b) no vendor integration references another's. A third
test fails loudly if the namespace names in the guard ever drift, so the first
two can't start passing vacuously. Compiler-generated async state machines and
lambda closures are scanned too, so a vendor type used only inside an async
method body is still caught; a vendor type touched only in a *synchronous*
body and never stored or passed would slip through, which would need IL
parsing and a new dependency to catch.

`ProgrammePulse.Tests/Architecture/SourceIndependenceDemonstrationTests`
proves the same boundary behaviourally rather than statically: it builds a
real `SyncSourceRegistry` from the two real adapters plus a third,
hypothetical source (`FakeSyncSource`, standing in for e.g. Jira —
deliberately not a new real connector) and drives it through the real
`SyncStatusQueryService` exactly as `StaffProgrammeOverviewController` does.
All three sources come back identically, with zero source-specific code in
either the registry construction or the status query — the demonstration
this phase's exit gate asked for.

### Canonical domain

Silver's shape — `Programme → Project → Workstream → WorkItem`, plus
`TimeEntry`, `PlannedAllocation`, `Dependency`, `Risk`, `Issue` — is the one
domain every source maps into, and it means the same thing regardless of
which source populated it:

- **A `WorkItem` is delivery work with a lifecycle stage**
  (`WorkItemLifecycleStage`: Backlog → … → Done). Only a `WorkItem` counts
  toward "% complete" or a Done total anywhere in Gold.
- **A `PlannedAllocation` is scheduled *intent* to spend time, never
  delivery.** It carries no lifecycle stage and is structurally incapable of
  being counted as Done — see "Planned allocation is not delivery" above.
  This is the GTM review's rule enforced by the type system, not by
  convention: a Gold query that wants "is this work done" can only ask a
  `WorkItem`, because `PlannedAllocation` has no field that would answer it.
- **A `TimeEntry` is a recorded actual**, distinct from both — evidence time
  was spent, not evidence work finished (a `WorkItem` can be Done with no
  time logged, or have time logged and still be open).
- **Capabilities describe what a source is honest about supplying**, not
  what it happens to return. `SourceCapabilities` is a closed set (`Work`,
  `Resource`, `Time`, `Absence`, `Commercial`) precisely so a Gold figure can
  say *no absence source is connected, so leave is unaccounted for* instead
  of silently under-reporting. A source claiming a capability it doesn't
  populate is a mapping bug, not a capabilities-enum bug.

A new source maps into this shape or it doesn't integrate with Programme
Ops — there is no per-source variant of `WorkItem`, and Gold never branches
on `ExternalSource` to interpret a row differently.

### Provenance rules

Every canonical row and every identity link carries where it came from, so a
reader (or a GDPR export, or an audit) can always answer "which upstream
system said this, as part of which run":

- **`ExternalSource` + `ExternalId`** on every Silver row is the mapping key
  back to Bronze, and — combined with `TenantId` — the idempotency key every
  Upsert uses (`(TenantId, ExternalSource, ExternalId)`), so re-running a
  sync converges instead of duplicating and two tenants' identical upstream
  ids never collide.
- **`ConnectionKey`** (`ExternalIdentityLink.ConnectionKey`, nullable —
  existing links predate connections) narrows provenance one level further,
  from "which source" to "which of this tenant's connections for that
  source" — meaningful once a tenant can hold more than one credentialed
  connection per source over time (a rotated or replaced credential still
  produces a new `SourceConnection` row via `SourceConnectionRepository`,
  not a mutated old one, and both remain queryable audit history).
- **`IIngestionContext`** is how a mapper learns which run and which
  connection it is currently writing under, without Silver/Gold needing to
  pass that context through every call explicitly — scoped per request and
  therefore per run, populated once by the sync service before mapping
  starts.
- **`ProgrammeOps_RawClickUpPayload`/`RawHubPlannerPayload`** hold the
  verbatim JSON Bronze captured, keyed the same way, so a mapping dispute is
  resolved by re-reading exactly what the source returned rather than
  trusting the Silver row's memory of it — this is why raw capture happens
  before any mapping, not alongside it.
- **`ProgrammeOps_SyncRun`** is provenance for the *run*, not a row: which
  instance ran it, when, its outcome, and (via `ProgrammeOps_AuditLog`) an
  immutable record of every completed/failed run's counts — this is what
  the Programme Overview header's "last published / running / failed"
  reads, and what a GDPR or incident review reads to answer "was this
  data touched on this date."

## File import (sanitised exports, no credentials)

`/staffops/programme/import` (Admin, `ManageIntegrations`; `ProductFeature.FileImport`,
on every plan) takes two canonical CSV files — **work items**, then **time
entries** — so a paid diagnostic can run on a customer's sanitised exports
without API keys (`docs/commercial/paid-diagnostic-offer.md`). Code lives in
`Services/Integrations/FileImport`, which `SourceIndependenceTests` treats as a
vendor namespace like ClickUp and Hub Planner.

It is a Bronze source in every respect except the trigger:

- Rows are captured verbatim to `ProgrammeOps_RawConnectorPayload`
  (source `FileImport`) and purged on `RawPayloadRetentionDays`.
- Silver rows carry `ExternalSource = "FileImport"`; upserts key on the
  file's own ids, so the same file twice changes nothing. Time rows without
  an `EntryId` get a content-derived key plus an occurrence counter, so two
  genuinely identical rows still count twice. Ids that differ only by case
  are refused, because the database would merge them.
- **Each upload replaces the last** of its kind (decided 29 September; the
  GTM review's finding 2). A row that was imported before but isn't in the
  new file is removed, so a correction or deletion at source comes through.
  Without this, an entry corrected from 3 h to 4 h with no `EntryId` got a
  new derived key and sat beside its old version: 7 h. Nothing is removed
  unprompted:
  - A file that removes nothing (a first import, an identical re-upload, a
    file that only adds) is written at once.
  - A file that would remove rows is **staged** in `ProgrammeOps_ImportStaging`
    (step 29, tenant-scoped, covered by the delivery purge) and the page
    shows the counts before and after, hours before and after, and the rows
    being removed. `POST …/import/confirm/{key}` applies it;
    `…/cancel/{key}` discards it. A preview lasts one hour, and staged files
    older than a day are purged.
  - Confirm re-validates and recomputes the plan. If the removals no longer
    match what was shown (the plan's fingerprint), nothing is applied and
    the new figures are shown instead.
  - A removed **work item** takes its allocations, dependencies (either end)
    and estimate baselines with it. **Time recorded against it is unlinked,
    never deleted**, and the preview says how many hours that is. Projects
    and workstreams left empty are kept.
  - The whole apply (rows, removals, staged file, run and audit) is one
    transaction: it commits entirely or not at all. Audit adds
    `ImportStaged` and `ImportCancelled`, and `ImportCompleted` records the
    removals.
- Writes run under `SyncRunCoordinator` (one import per tenant at a time,
  a `ProgrammeOps_SyncRun` row) and are audited `ImportCompleted` /
  `ImportFailed` / `ImportRejected`.
- People go through `IStaffIdentityResolver` (email first, then name, as the
  external id) — unmatched people land on the identity queue.
- Status text maps by keyword in `ImportedStatusMapper`; no match, or two
  contradictory matches, stays **Unmapped** and is counted in the result.

What differs from a connector, deliberately:

- **All-or-nothing validation.** The whole file is parsed and checked
  (required cells, dates `yyyy-MM-dd`/`dd/MM/yyyy`, hours as decimal or
  `h:mm`, emails, lengths, duplicate ids, time rows pointing at a work item
  that was never imported) before the run starts. Any problem means nothing
  is written and every problem is listed by row and column. A mid-write
  failure (database, lease loss) rolls the whole import back.
- **Optional Programme column.** A work-items file may name each row's
  programme (aliases "Program", "Portfolio"), so one export can load a whole
  portfolio. Rows without it share the root "Imported delivery data"
  programme, as before the column existed. Project identity is still the
  project name, so re-importing a project under a different programme moves
  it rather than copying it.
- **Header aliases.** `DeliveryExportSchema` accepts common export spellings
  ("Task ID", "Issue key", "Spent date"), matched ignoring case, spaces and
  punctuation. Two headers meaning the same column are refused rather than
  one silently winning; unrecognised headers are ignored and listed.
  `Aliases_never_collide_within_one_file_kind` guards the alias table.
- **Not an `ISyncSource`.** It is driven by an upload, so it has no "sync
  now" button and does not appear in the publication header. If imported
  data should get a header row, that is the next change here.

Limits: 5 MB and 20,000 rows per file (`StaffFileImportController.MaxUploadBytes`,
`DeliveryExportImportService.MaxDataRows`), UTF-8 only, `.csv` only; the
upload is rate-limited with the `sync` policy.

Related hardening: `Services/Reporting/CsvWriter` now neutralises
spreadsheet formulas (a leading `=`, `+`, `-`, `@`, tab or CR on anything
that isn't a plain number), because uploaded titles reach the Reporting Hub
CSV exports that executives open in Excel.

## Evidence Check (the automated exception register)

`/staffops/programme/evidence-check` answers one question before every
delivery review: **can this report be trusted, and what needs an owner?**
Readable by anyone with `ViewDeliveryReporting`, tenant-scoped, gated on
`ProductFeature.ReportingHub`. `EvidenceCheckCalculator` is pure and holds
every rule; `EvidenceCheckService` only loads Silver data and source run
state. Like `ProgrammeOverviewQueryService`, it never reads `StaffRate` or
any cost figure, and `The_evidence_check_cannot_reach_cost_rates` fails the
build if a rate dependency is ever injected.

Findings fall into two groups:

- **Evidence gaps** decide trust: status that can't be read as a stage;
  committed work with no matched owner, estimate or due date; recorded time
  not linked to an item, with no matched person, or with unknown
  billability; finished work with no time, checked only for sources that
  record time at all; unmatched people; no time evidence at all.
- **Delivery exceptions** are trusted facts that need a decision: overdue,
  blocked, over estimate, and time spent on work still in the backlog.

Every finding carries a numerator and denominator ("3 of 40 committed
items", or hours for time findings) plus the source record ids behind it,
matching `docs/commercial/evidence-pack-template.md`. Items are named, never
people.

Readiness is a band with stated thresholds, never a score:

- **No data:** there are no work items.
- **Not decision-ready:** a connected source failed or never published, or
  a high-severity gap covers at least 10% of its data.
- **Use with caveats:** any other gap exists; or a source (a sync or the
  file import) last published more than 7 days ago; or a declared extract
  date is more than 7 days old or falls before the period ends. Clean but
  stale data is not decision-ready for a weekly review, and the headline
  reason says why rather than leaving it to a note.
- **Decision-ready:** no gaps remain and the data is current.

### Scope: what a check is about

Every check has a declared **scope** (`EvidenceScope`, decided 29 September,
from the GTM review's finding 1): a programme and/or customer (the same
picker Reporting uses, validated against the tenant), a **reporting period**
(the last 7 days unless given, at most `ReportingPeriod.MaxDays`), an optional
**extract date** and an optional **decision** the review supports. What the
period changes, stated on the page and in the pack:

- **Time findings** use the period's hours only (a period read, never the
  whole history).
- **Delivery state** (overdue, blocked, owners, estimates, due dates) is as
  of the check.
- **Estimate accuracy** ("over estimate", and "finished work with no time")
  uses all time ever recorded against the item, from the SQL lifetime
  aggregate: an overrun is cumulative, and work done before the period isn't
  free.
- In a programme or customer scope, **time linked to no work item** can't be
  placed, so it's left out and the amount is stated in a note. The
  whole-organisation check includes it as a gap.

The extract date, when declared, is what the data's age is judged on: an
old export uploaded today is still old, which publication time alone can't
show.

A recorded review keeps its scope (step 30, nullable columns on
`ProgrammeOps_EvidenceReview`). Comparison, carry-forward and "only the latest
review takes decisions" all work **within one scope**
(`EvidenceScope.Key`: programme and customer, not the period, which moves
each cycle). A programme review never inherits the whole organisation's
decisions, and a newer programme review doesn't freeze the whole-organisation
one. Reviews recorded before scopes existed have none; they read the whole
organisation over all time, compare as whole-organisation reviews, and are
labelled that way rather than given an invented period.

Delivery exceptions never lower readiness: a report that shows its bad
news clearly is the trustworthy one. Each project gets its own band too.

`/staffops/programme/evidence-check/export` downloads the whole register,
one row per source record, with empty Owner and Decision columns ready for
the review. It goes through `CsvWriter`, so imported titles can't execute as
spreadsheet formulas.

### Recorded reviews: the weekly loop

The check on its own forgets everything when the page closes. Recording it
(`POST /staffops/programme/evidence-check/record`, `CaptureTrend`, the same
grant as capturing a reporting trend point) freezes the whole check,
including every finding's source records, in `ProgrammeOps_EvidenceReview`
and `ProgrammeOps_EvidenceReviewFinding` (migration
`2026-09-programmeops-25` (shipped as -22 in #18; renumbered when the modernize branch landed, see the plan); `tenantId` non-nullable; unique on
`(tenantId, reviewKey, findingKey)`). The review, its pack and its CSV are
then reproducible however Silver changes afterwards.

The rules live in the pure `EvidenceReviewCalculator`:

- **Decide** (`ManageExecutiveDecisions`, on the latest review only): each
  finding takes a disposition (fix at source, accept and state, disputed,
  resolved), an owner from the tenant's *active* staff, a target date and a
  note. Fix at source needs an owner and a date; accept and dispute need a
  written reason; past target dates are refused.
- **Record identity**: a source record is recognised across reviews by
  `(source, externalId)`, falling back to `(project, title)` when there is
  no id (`EvidenceRecordIdentity`). Both the latest and previous reviews
  are therefore loaded *with* their records.
- **Carry forward**: recording copies each finding's decision from the
  previous review, flagged `carriedForward` until someone revisits it, but
  only while at least one record that decision covered is still found. If
  every one has cleared, the finding now describes different records and
  starts undecided. An owner named for last week's ten overdue items is
  not the owner of this week's ten. A finding marked resolved whose records
  are still found reopens as undecided: the data has contradicted the claim.
- **Compare**: the live page and every review set each finding against the
  previous review as new, worse, *same count, different records*, no
  change, improved or cleared, and show how many records stayed, appeared
  and cleared. Movement is judged on the numerator, not the share, so more
  clean data can't make a finding look fixed, and an unchanged count over
  different records is never "no change". Decisions that didn't hold
  (resolved or past their target date, *with the records they covered
  still found*) are flagged. Findings with no records (e.g. people awaiting
  a match) compare on the count alone, and say so.
- **Freeze**: once a later review exists, an earlier one returns 409 on any
  decision. It is the record of what the review knew at the time.

`/reviews/{key}/pack` is a self-contained, printable board pack (no script,
because the `/staffops` CSP forbids inline script, so printing is the
browser's own). `/reviews/{key}/export` is the register with owner, decision,
target date and note filled in. Recording and deciding are audited
(`EvidenceReview`/`Recorded`, `EvidenceReviewFinding`/`Decided`; free-text
notes are not copied into the audit detail). `EvidenceReviewDataParticipant`
puts owner, decider and recorder references into GDPR export, and erasure
removes the staff key while keeping the decision. Free-text notes are not
rewritten, the same limit the executive decision journal states.

Like the check itself, reviews never read `StaffRate` or any cost figure.
Scheduled recording waits for background execution (R18). Until then a
reviewer records the check before the meeting.

## Extension points left for later phases

- **Run syncs off the request thread.** A sync still executes inside the
  `POST /staffops/programme/sync/{source}` request; a large workspace can outlive a
  reverse proxy's timeout even though the run itself completes. The
  natural next step is a queued background run (Umbraco's
  `RecurringHostedServiceBase` or a hosted queue) with the page polling
  `ProgrammeOps_SyncRun` — `SyncRunCoordinator`/`SyncRunHandle` and the
  run rows were shaped so that move doesn't change the contract (the
  Data sources panel already renders a Running run's stage).
- ~~**Tenant-owned source connections** (GTM review B03)~~ — built, including
  credentials: every `ProgrammeOps_*` table carries `tenantId` (B02), Silver
  upserts key on `(TenantId, ExternalSource, ExternalId)` so two tenants'
  identical ClickUp/Hub Planner ids never collide, and
  `ProgrammeOps_SourceConnection` (`Models/Programme/SourceConnection`,
  `SourceConnectionRepository.GetOrCreateActiveAsync`) gives each tenant its
  own connection row per source, auto-created on that tenant's first sync,
  with `ExternalIdentityLink.ConnectionKey` as provenance. The sync lease
  and run state are keyed on `(tenantId, source)`
  (`ISyncRunRepository`/`SyncRunCoordinator`), so two tenants' syncs never
  serialize against each other. A tenant's own Admin can now also set that
  connection's credential at `/staffops/programme/connections`
  (`StaffSourceConnectionController`) — a ClickUp token + workspace id, or a
  Hub Planner key — encrypted at rest via `ISourceCredentialProtector`
  (ASP.NET Core Data Protection, purpose `ProgrammePulse.SourceConnection.Credential.v1`)
  and resolved once per sync run (`ClickUpSyncService`/`HubPlannerSyncService`
  call `IClickUpApiClient.WithCredential`/`IHubPlannerApiClient.WithCredential`
  before the run starts). Credentials are immutable and attached per request.
  A tenant without a valid connection fails closed outside Development.
  Shared fallback requires Development and explicit
  `ProgrammeOps:AllowSharedSourceCredentials=true`. See `docs/tenancy.md`.
- `IStaffRoleAssignmentService` needs an `IMemberService`-backed
  implementation to answer for an arbitrary staff member, not just the
  current one — the Reporting Hub's Team Lead scoping works around this by
  only ever resolving the *current* signed-in member's own `Team`, not by
  looking up who else belongs to it. `StaffAccountController`'s MFA gate
  (see `CLAUDE.md`) needed exactly this "arbitrary member" lookup for group
  membership and used `IMemberService.GetMembersByGroup` directly rather
  than extending this service — worth folding back in if a second caller
  needs the same thing.
- A second Bronze source (calendar) writing into the same Silver tables —
  the reason Silver/Gold were kept ClickUp-agnostic in this phase.
- ~~ClickUp tasks/time-entries with multiple assignees still resolve to a
  single `AssignedStaffKey`/`StaffKey`~~ — built: `WorkItem` assignment now
  splits across every resolved ClickUp assignee via
  `ProgrammeOps_WorkItemAllocation` (`Models/Programme/WorkItemAllocation`,
  `ClickUpMappingService.ResolveAssigneeStaffKeysAsync`).
  `WorkItem.AssignedStaffKey` is kept as the derived primary/first assignee
  for single-owner views (`StaffPortalController`'s "My Work"); the
  Programme Overview's resource-capacity grouping
  (`ProgrammeOverviewQueryService.BuildResourceCapacity`) reads the
  allocation set but only credits `EstimatedHours` to the primary allocation
  on each work item — crediting every co-assignee in full was tried and
  then deliberately reverted (see the PMP/APM utilisation review this
  followed): ClickUp's assignee list doesn't distinguish who's actually
  doing the work from who's just tagged for visibility, so full-crediting
  every co-assignee inflated workload for everyone but the person actually
  accountable. `TimeEntry.StaffKey` stays single-valued — a ClickUp time
  entry is inherently one person's logged hours, so splitting was never
  needed
  there.
- Cost is computed on the fly from `TimeEntry` × `StaffRate` history rather
  than persisted as a `CostSnapshot` table — revisit only if recomputing on
  every request becomes an actual performance problem.
- The stakeholder "add" form only accepts an external name — there's no
  internal-staff picker yet (`AddStakeholder`'s `staffKey` parameter is
  wired end-to-end but nothing in the UI sets it).
- Reporting snapshots are capture-on-demand, not scheduled — see above.
- No threshold alerts yet (a workstream going Blocked, a negative residual
  capacity) — that needs an actual notification channel first, which
  nothing in this codebase has built yet either.
