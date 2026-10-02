> **Superseded, 26 September 2026.** This assessment was overtaken: Phase 2 is committed (`d7b2459`), and paging and cancellation are done. Current allocation and the collision protocol are in [parallel-agent-working.md](../parallel-agent-working.md), which also records what happened to each observation below. Kept unedited below for the record.

# Phase 2 parallel-work scope — 25 September 2026

This is Codex's independent scope assessment of the working tree based on
`02b5a33`. Claude is actively implementing Phase 2. The inventory is a snapshot,
not an agreement with Claude about future file ownership. Claude's test and
performance results below are recorded evidence, not independently rerun here.

## Active scope: leave with Claude

The current diff and the Phase 2 delivery record cover:

| Work | Files and areas affected |
|---|---|
| Period reads, lifetime aggregates, weekly project counts, point lookups | ProgrammeRepository, IProgrammeReadRepository, IProgrammeRepository, TimeEntry |
| Gold query conversion and removal of person-level N+1 reads | Reporting, Delivery Load, Overview, Budget, My Work, calibration; Staff availability, rate and work-hours repositories |
| Commercial consumers of the changed reads | ContractAdminService, ContractCommercialService, InvoiceGenerationService, ExecutivePackRepository |
| Batched writes and raw capture | ClickUp mapping/sync, ProgrammeRepository, ClickUpRawPayloadRepository, SqlMultiRow, SqlInList |
| Indexing and alert concurrency | ProgrammeOps migration plan, AddTenantReadIndexes, AddOpenAlertUniqueness |
| Wiring, evidence and documentation | ProgrammeOperationsComposer, architecture writer guard, bounded-read/batch/alert integration tests, ScaleBaselineTests, CLAUDE.md, architecture review |

Treat these files and their dependent tests as one active change. A file being
clean today does not establish that Claude will not need it before finishing.

## Review observations

1. The reporting changes explicitly retain lifetime effort totals and tenant-wide
   coverage facts while bounding period detail. That addresses the risk of
   accidentally changing report meaning with a blanket date filter.
2. Delivery Load retains observed-week semantics. The record acknowledges that
   it still grows with history (150 to 333 ms at five times history). A fixed
   lookback is a product change, not a mechanical performance fix.
3. **The indexing work is narrower than roadmap item A2.** The new migration
   adds 12 indexes on hot ProgrammeOps tables. The roadmap asks for tenant-leading
   coverage across all tenant tables and retention indexes across areas. This
   broader inventory and implementation remain work to track explicitly; the
   current migration does not establish completion of A2 across the solution.
4. The recorded sync round-trip improvement is code-path analysis. Keep it
   distinguished from the measured LocalDB elapsed times and from a captured
   SQL command count when assessing the exit criteria.

## Work explicitly left open in Claude's record

- Non-null tenant columns, including preflight and restored-database rehearsal.
- List paging and cancellation propagation.
- Hub Planner, Jira and Tempo adoption of the batch write APIs.
- Lifetime budget/contract costing aggregation.
- Delivery Load lookback policy and invoice treatment of work-date-only entries.

These are backlog candidates, not automatically collision-free assignments.
Cancellation affects the interfaces already changing; connector batching depends
on the new APIs; paging can affect the same repositories, routes and tests.

## Safe parallel work

**Recommended now: independent assurance and preparation.** Keep changes in
separate new artifacts rather than editing Claude's implementation or delivery
record.

| Task | Concrete output | Boundary |
|---|---|---|
| Independent source review | Findings with file/line references and missing regression cases | Read active code; do not patch it while Claude is editing |
| Tenant schema readiness inventory | A separate read-only SQL script reporting nullable tenant columns, null counts, and unfiltered tenant-leading index coverage | Prepare the script independently; database execution should be scheduled away from Claude's baseline |
| Migration rehearsal preparation | Restore/rehearsal procedure and acceptance checklist for A6 | No ALTER, backfill, index rebuild, or shared-database mutation |
| Remaining index gap assessment | Per-area table/index inventory for A2 | Leave migration plans untouched until ownership is allocated |

After Claude's change is stable, independent verification should use a separate
checkout, build output and dedicated test database. A separate checkout alone
does not isolate tests: the default factory uses `UmbracoBase_IntegrationTests`.
Use its `PP_TEST_SQL_CONNECTION` override to select a dedicated database.

## Work performed by Codex for this assessment

Read the current diff, delivery record, read interfaces, reporting changes,
batch helpers and migrations. No application files were edited and no new
build, test, migration or database run was started during this assessment.
Only this scope document was added.
