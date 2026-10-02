# Estimate calibration and scheduling visibility

Introduced 19 September 2026. This feature is **advisory effort calibration**, not an automatic schedule or an employee performance score.

## Workflow and access

| Audience | Route and visibility | Control |
| --- | --- | --- |
| Tenant Admin | `/staffops/reporting/estimate-calibration`: aggregate results, named estimator history, capture and review controls | Can freeze the current positive hour estimate on a Backlog/Ready work item before time is logged, attributing it to the person who made the estimate. Can approve a completed item as comparable only when actual time exists, or exclude it with a reason. Reviews are one-time. |
| Board | Same route: tenant aggregate sample count, pending/excluded counts, median and 80th-percentile actual/estimate ratios only | Read-only. No estimator names, baseline rows, capture options, cost rates or review actions are put into its view model. The Board member group is tenant-assignable and grants no general Admin rights. |
| Team Lead and Staff | No calibration route | Existing workload and reporting routes retain their existing permissions. |
| Platform Admin | No implicit access to tenant calibration | A platform operator needs the relevant tenant membership and role; operator status alone does not grant it. |

Records are tenant scoped. `ProgrammeOps_EstimateBaseline` has a unique `(tenantId, workItemKey)` index, preserving one original estimate per work item. Original estimate fields are immutable after capture; the review fields can be set once. The review stores who decided, when, whether the case is comparable and an optional exclusion reason. Controller actions also write `ProgrammeOps_AuditLog` events. A new source sync may update the current work item estimate but cannot silently rewrite the captured original.

## Calculation and decision use

Only Done items with an Admin-approved comparable original estimate and positive linked actual effort enter history. Missing actuals, unfinished work, unreviewed items, excluded scope changes and cross-tenant matches do not enter the ratio distribution. The ratio is **actual effort hours / original effort hours**. One person who estimated the work is attributed; assignee and time-entry author can be different people.

The Board receives a median and an 80th-percentile ratio only after 10 comparable items. The Admin sees each estimator's observed median. An advisory personal planning factor appears only after at least 5 comparable personal items **and** 10 portfolio items: `(n × personal median + 10 × portfolio median) / (n + 10)`, rounded to two decimals. This blends a small personal sample toward the tenant's wider history. These thresholds and the blend are initial product policy, not PMI-prescribed constants; validate them against real design-partner data before using them for commitments.

For an unchanged scope with a 20-hour original effort estimate and an advisory factor of 1.5, a planner may stress-test 30 hours of demand. Keep 20 hours as the original estimate. The factor is not automatically written to a task, booking, invoice or baseline. Do not apply a portfolio P80 to every task and then add an overlapping contingency again. Do not compare 2–3 elapsed days with 7–10 *effort* days; the current feature accepts hours only.

[PMI's Practice Standard for Scheduling](https://www.pmi.org/-/media/pmi/documents/public/pdf/certifications/practice-standard-scheduling.pdf) calls for resource calendars, availability, assignments, constraints and prioritisation when resolving conflicts. It also calls for calculated leveling results to be reviewed against expectations and history. This implementation supplies one historical effort input and displays it as such. **It does not move work between dates or claim resource smoothing.** A scheduling feature must first join adjusted effort to dated assignments, approved leave/holidays, project priorities, dependencies, work calendars and fixed milestones, then show baseline and proposed dates for human review. A planning factor alone cannot determine elapsed duration.

## Support and limits

An Admin must review scope comparability and time completeness. The application cannot currently detect scope changes or missing timesheet entries automatically. Capture is prospective: old completed items without a frozen, attributed estimate remain outside the sample. Results pool unlike work types today; task category and reference-class matching are future work. There is no automated personal ranking, no forecast back-testing, and no review correction workflow. The Board aggregate may still be identifiable in a very small tenant, so deployment access to the Board group should be tightly controlled.

The tests cover reviewed/completed eligibility, exclusion, tenant mismatch, sample thresholds, the blended factor, board projection, prospective capture and missing actuals. Follow-up acceptance tests should cover database migration and concurrent capture, role/tenant route access, review audit events, source re-sync leaving the original intact, category-specific calibration and a dated resource-conflict example.
