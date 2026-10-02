# Delivery intelligence scenario build gate

Assessment: 19 September 2026. Scope: repository inspection only; no live source account or production data was used. The existing working tree contains unrelated in-progress changes, so this document does not assert that those changes have been released.

## Decision

**Gate status: RED for a vendor-neutral, deterministic scenario harness.** The application has source-neutral programme records and useful fake-repository tests. It does not yet have one pure intelligence entry point that accepts all canonical facts and is called by both production and file-driven tests. Building fixture-only calculations now would create a second product definition and would not prove customer value.

The next engineering change should extract one production calculation at a time into a pure service, then run the same service with canonical fixture data. Start with weekly capacity and variance because those four numbers underpin the buyer's resource decision. Keep existing reporting behaviour explicit until the new output is verified.

## 1. Coupling audit

| Area | State | Evidence and consequence |
| --- | --- | --- |
| People and roles | AMBER | `StaffProfile` is a plain record, but requires `MemberId` and is retrieved through `IStaffRepository`. A fictional person can be constructed in a test; a complete roster cannot be processed by a standalone intelligence service. Job title is free text, not a canonical skill/role taxonomy. |
| Projects, work items, allocations, time entries | AMBER | `Project`, `WorkItem`, `WorkItemAllocation`, `PlannedAllocation`, and `TimeEntry` are plain records with source fields. Mapping DTOs stay in `Services/Integrations`. Calculation services still fetch these through `IProgrammeRepository`. |
| Contractual and available capacity | RED | `ReportingQueryService.BuildContributorCapacityAsync` combines work-hours history, availability and time entries after repository calls. No independent capacity input/output contract exists. Public holidays need explicit absence/calendar semantics in the proposed harness. |
| Resource plan versus task plan | RED | `ProgrammeOverviewQueryService` displays bookings and outstanding task estimates in separate sections. It does not reconcile them for a common person/project/window. `PlannedAllocation.AllocatedHours` can be null, and the source's hour unit still needs live validation. |
| Leave and absence | AMBER | `Availability` is a plain record, but leave deduction is private to `ReportingQueryService`; `AlertDetectionService` has a separate forward-window calculation. This can produce different interpretations of the same leave. |
| Delivery variance and risk | RED | Effort variance is private to `ReportingQueryService` and grouped by workstream. `AlertDetectionService` reads repositories and writes alerts in one operation. The negative residual alert uses actual logged time; its forward alert substitutes due task estimates for committed resource allocations. Neither is the requested four-fact comparison. |
| Commercial calculations | AMBER | `TimeEntryCostCalculator` is pure and testable. `ProgrammeBudgetService` and `ContractCommercialService` orchestrate repository reads and calculations together. Commercial fixtures must stay out of non-admin reporting paths. |
| Time and tenancy | AMBER | Reporting and sync orchestration inject `TimeProvider` and pass `tenantId` explicitly, which is a useful base. Historical queries still need an explicit `AsOfDate` and event visibility rule. Tenant IDs on several records are nullable for migration compatibility. |
| HTTP, Razor, LLM | GREEN | Core record types and calculation classes do not require a request, view, or LLM to construct. Controllers and views call reporting services, rather than containing the main arithmetic. |
| SQL and external credentials | RED for full scenario execution | Existing service constructors need repositories, although fake repositories make unit tests possible. A file runner cannot currently call one production intelligence operation without supplying the application's persistence-shaped interfaces. |

The current architecture guard (`ProgrammePulse.Tests/Architecture/SourceIndependenceTests`) checks namespace dependencies. It does not verify semantic equivalence, consistent arithmetic, or absence of repository calls in the intelligence core.

## 2. Canonical meaning and provenance

The first slice needs five separately named facts, keyed by tenant, person, project and time window:

| Fact | Definition | Missing-data rule |
| --- | --- | --- |
| ContractualCapacity | Working hours owed under the effective contract/calendar | Missing contract history is reported as fallback use, not silently treated as a known contract. |
| AvailableCapacity | Contractual hours less approved leave and holidays within the window | Overlapping absence must be deduplicated and capped per working day. |
| PlannedAllocation | Hours reserved in the resource planner for person/project/window | Percentage-only or ambiguous booking units remain unknown hours until mapping semantics are confirmed. |
| WorkEstimate | Estimated effort on work items attributed to person/project/window | Missing estimate is unknown and counted in coverage, never interpreted as zero effort. |
| ActualTimeEntry | Recorded hours worked in the window | Unmatched person/project/item remains visible in a quality ledger, not attached by guesswork. |

For the example 30 available / 35 allocated / 30 estimated / 35 actual hours, preserve all four values. Define `allocationHeadroom = available - allocated = -5`, `planAlignment = allocated - estimated = 5`, and `effortVariance = actual - estimated = 5`. These answer different questions. Existing `ResidualCapacityHours = available - logged` is an actual-time measure and should be labelled that way wherever retained.

Canonical `Person`, `Project`, `WorkItem`, `WorkEstimate`, `ResourceAllocation`, `TimeEntry`, `Absence`, `SkillRole`, `Milestone`, and `SourceReference` can remain small records. Put `TenantId`, `SourceSystem`, `ExternalId`, `ImportedAt`, and `SyncRunId` on imported facts or their source reference. Include a connection/account key in identity uniqueness: source name and external ID alone are insufficient when a tenant has two accounts with the same vendor. Keep provenance out of business-result equality. Make source DTOs terminate in adapters.

## 3. Harness and acceptance contract

Proposed versioned fixture layout: `TestScenarios/<name>/scenario.json` (`tenantId`, `asOfDate`, `windowStart`, `windowEnd`, schema version), `people.json`, `projects.json`, `work-items.json`, `allocations.json`, `timesheets.json`, `absences.json`, and `expected-results.json`. Files use fixed identifiers, decimal hours, explicit UTC dates, stable ordering, and no randomness or current clock reads.

One production `Evaluate(snapshot, asOfDate)` operation must return numeric facts, quality issues, and risk signals. The production reporting service assembles a tenant-scoped snapshot from repositories and calls `Evaluate`; the test runner deserializes files and calls the same operation. Golden files compare exact decimal values, stable signal codes/severity/entity IDs, coverage counts, and project-level rows. Human-readable message wording and source metadata are separate from business equality. An update to a golden file requires a documented reason and review of the changed business rule.

Implement scenarios in this order, with a failing test before claiming each capability: healthy team; 37.5h available/50h allocated; leave-induced deficit; missing estimates; 100h estimate/140h actual; 200h resource plan/90h task plan; offsetting project overruns; mixed Jira/resource/time sources; equivalent ClickUp source; dirty records; hypothetical project without baseline mutation; same external ID in two tenants. The dirty-data case must produce typed issues for duplicate source keys, missing assignee/estimate, deleted user, unknown project, closed-work time, orphan allocation and orphan time. A duplicate or unresolved mapping must not be resolved by arbitrary first-match selection.

Adapter contract tests take vendor JSON through each real mapper and compare canonical facts. ClickUp and Hub Planner can be tested now; Jira and a second resource planner remain proposed because their adapters do not exist. Cross-stack equivalence should compare normalized business results for (A) ClickUp/Hub Planner/other time, (B) Jira/resource planner/Jira worklogs, and (C) canonical files. Legitimate differences include story points versus hours, percentage bookings without an agreed calendar, absent worklog permissions, differing status semantics, and source-specific project hierarchy. Such differences produce a coverage/semantic issue rather than forced equality.

## 4. Northstar Digital benchmark

Build a permanent, hand-authored 12-week fixture with 25 people, the PM/BA/UX/frontend/backend/QA/DevOps roles, 8 concurrent projects, fixed-price/T&M/internal work, one part-time contract, leave, holidays, shared specialists, overdue and unestimated work, under-utilisation, an over-allocated specialist, an overrun and a future pipeline project. Record every event's effective date and import visibility date. Snapshot weeks 1, 4, 8 and 12 with explicit `AsOfDate`; future facts cannot leak into prior snapshots.

The benchmark report must print capacity, utilisation, allocation, estimate coverage, actual-versus-estimate variance by project, planning mismatch, risk signals, quality issues and source freshness. Include run time and fixture schema version. A single portfolio total cannot hide project-level offsetting overruns. Keep the benchmark fictional and deterministic so it is safe for demos and support reproduction.

## 5. Stability, support and logging gates

Before a pilot claim, each evaluation should emit one structured summary with tenant ID, scenario or publication ID, as-of date, window, source run IDs, calculation version, input counts, issue counts by code, signal counts by severity and duration. Do not log credentials, raw personal data or full source payloads in these summaries. Correlate with the existing request correlation ID and durable `SyncRun` key. Persist source freshness and the partial-publication warning already shown by the programme page; distinguish stale/partial data from a healthy zero.

Support needs a reproducible case bundle: sanitized canonical input, mapping version, source references, calculation version, expected/actual output diff and correlation/run IDs. A support engineer should be able to run it offline, identify the mapping or calculation that changed, and explain the result. Record an owner and remediation for each quality issue. Gate release on tenant isolation, deterministic replay, adapter contracts, partial-run visibility and a restore/migration rehearsal; the existing endpoint and fake-repository tests do not establish these together.

## 6. GTM proof

Use a fictional Northstar walkthrough to show one decision: a proposed project causes a specialist's future allocation to exceed available hours; approved leave worsens the deficit; the manager can trace the signal to contract, bookings, estimates and source records. Compare time to prepare and explain that decision against a manually reconciled weekly pack with design partners. Collect baseline minutes, repeated weekly minutes, mapping exceptions, false alerts, decisions taken and support time. Do not claim a reduction, accuracy rate or source parity until measured in a live design-partner workflow.

The first saleable promise is traceable programme assurance across supported source combinations. Current evidence supports a narrower statement: the repository has useful source-neutral records, source boundaries, tenant-aware in-progress work, durable sync diagnostics, and reporting tests. It does **not** yet prove cross-source equivalence, reliable dated capacity forecasting, Jira support or a 12-week benchmark. The scenario gate turns those into reviewable engineering and buyer evidence before expanding the connector or AI story.
