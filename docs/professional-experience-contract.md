# Professional experience contract

Status: working product contract, 19 September 2026. This describes the decisions a delivery director, PMO lead, team lead, administrator and support operator need to make. It is a test map, not evidence that every journey is already implemented. Customer interviews must validate terminology, thresholds and workflow order before they become a sales claim.

The [stakeholder question backlog](stakeholder-questions.md) extends this to project managers, resource planners, finance/commercial leads and executive sponsors, with the evidence, current tests and missing functions for each question.

## The weekly professional workflow

| Step and professional question | Evidence the person needs | Expected behaviour | Build check today | Gap / release consequence |
| --- | --- | --- | --- | --- |
| 1. Can I trust this week's view? | Tenant, reporting period, per-source last complete publication, failure/running state, unresolved identities and source coverage | A failed or running sync says figures may be partial; no publication says coverage is unknown | `SyncStatusQueryServiceTests` and programme overview view model/view | No coherent multi-source publication snapshot. Do not call mixed live tables a reconciled portfolio pack. |
| 2. Which delivery commitment needs attention? | Blocked/overdue work and milestones by project, with source trace and owner | A booking cannot advance delivery completion; project exceptions remain visible | `ProgrammeOverviewQueryServiceTests` and `ReportingQueryServiceTests` | No single prioritised exception queue with owner, rationale and resolution history. |
| 3. Can the team take the work? | Contractual hours, approved leave, available hours, resource bookings, task estimates and actual time for the same dated window | Keep all facts separate; show allocation headroom, plan alignment and actual effort variance independently | `CapacityFactsTests`, `ProfessionalCapacityCasesTests`, `ReportingQueryServiceTests` | Live overview has separate booking/workload sections but no reconciled dated forecast. Do not sell a capacity forecast yet. |
| 4. Why is the number wrong or incomplete? | Missing estimates, unresolved people, unknown booking units, orphan time and source run/mapping reference | Unknown stays unknown; a person can inspect the missing evidence and request a correction | Existing overview coverage and identity tests; proposed dirty-data scenario | No unified quality ledger or reproducible evaluation bundle. |
| 5. What is the impact of a change? | Baseline and proposed project demand, specialist availability, project-level variance | What-if uses a copy and never changes baseline records; offsetting project variance stays visible | Proposed scenario 07 and 11 | No what-if engine or permanent benchmark. |
| 6. What can I share or escalate? | Decision, owner, as-of date, severity, supporting facts, provenance and freshness | A reviewable exception can be explained without raw credentials or hidden rates | Cost-shape tests and source-state tests | No exportable decision record or measured buyer time saving. |

The core experience is **review → understand → decide → assign → verify**. Each warning should say what changed, which entity/window it affects, which facts produced it, how complete the evidence is, and who can resolve it. A dashboard total without these details is a navigation aid, not a professional decision record.

## Semantic terms that must survive future features

| Term shown to a professional | Meaning | Forbidden interpretation |
| --- | --- | --- |
| Contractual hours | Effective working obligation in the window | Bookable hours after leave |
| Available hours | Contractual hours less approved absence/holiday | Resource allocation or actual time |
| Booked/planned hours | Source-confirmed resource reservation | Work completed or task estimate |
| Estimated hours | Stated delivery effort, with missing estimate count | A complete forecast when estimates or dates are absent |
| Logged/actual hours | Recorded effort | Future capacity commitment |
| Actual residual hours | Available less logged for the period | Forward allocation headroom |
| Allocation headroom | Available less planned hours | Actual utilisation |
| Plan alignment | Planned less estimated hours | Overrun or margin |
| Effort variance | Actual less estimated hours | Proof of completion |

The first six fixed cases in `ProgrammePulse.Tests/Experience/professional-capacity-cases.json` exercise these definitions through `CapacityFacts`, which live reporting also calls. Exact decimal results are build gated. The cases are deliberately simple enough for a delivery lead to review during a rule change. They are **not** a substitute for dated, tenant-scoped end-to-end scenario tests.

## Deviation register for feature reviews

For every new field, connector, calculation or screen, add a row here or to a linked decision record answering: which professional question it serves; which source fact and date it relies on; what happens when that fact is absent/stale/ambiguous; which tenant and role can see it; which existing signal it could contradict; which deterministic test and view check catch the contradiction. A change to a golden result needs the business reason in the review, not only a snapshot update.

Current priority deviations to catch next:

1. **Plan versus task demand**: a 200h booking and 90h task estimate must yield a planning-alignment issue; current overview shows separate sections without reconciling them.
2. **Leave before delivery**: approved future leave must reduce future available hours and expose a new deficit, even when current actual utilisation is low.
3. **Hidden project overrun**: two opposite project variances may net to zero at portfolio level; each project's adverse result must remain visible.
4. **Unreliable data**: unknown project/person/unit, missing estimate and stale/partial run must reduce confidence; they must never produce a healthy zero by default.
5. **Equivalent sources**: when business facts match, source A/B/C must produce equal numeric facts and signal codes; provenance and legitimate semantic gaps can differ.
6. **Access and isolation**: a team lead's decision view must not retrieve cost rates; identical external IDs in two tenants must not join.

## Human validation and GTM evidence

Walk two design partners through a fixed Northstar Digital weekly pack and ask them to perform the workflow above without coaching. Observe whether they can identify the most important exception, explain its numbers, find missing evidence, name an action owner and revisit the result after a simulated change. Record task time, wrong decisions, terminology corrections, false alarms and support questions. Repeat with their existing process. Do not claim professional fit or time saved until that comparison is measured.

The [scenario build gate](archive/scenario-harness-build-gate-2026-09-19.md) tracks the wider architectural work. Its RED status remains until one production intelligence operation, file-driven fixtures, cross-source equivalence, tenant isolation and temporal benchmark checks run in the build.
