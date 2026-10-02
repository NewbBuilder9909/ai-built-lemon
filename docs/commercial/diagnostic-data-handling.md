# How we handle your data during a diagnostic

For the delivery director, PMO lead or security contact approving a weekly
delivery evidence diagnostic. One page, in plain terms. Where it says "we",
it means the person delivering your diagnostic. Attach it to the proposal.
Agree the specifics (fields, transfer route, deletion date) with
[`diagnostic-data-checklist.md`](diagnostic-data-checklist.md) before any file
is sent.

## What we ask for

Two CSV exports from whatever tool you use: **work items** (id, project,
title, status, and optionally assignee, due date and estimate) and **recorded
time** (date, hours, and optionally the work item, person and billability).
No API keys, passwords or system access. No ticket bodies, attachments,
messages, customer contact details, or health, equality or disciplinary data.

People's names and emails are optional. Send them only if you want work and
time matched to named people. Otherwise send a pseudonymous key (e.g.
`P-014`) and the findings will still be complete.

## Where it goes and who sees it

- **One isolated workspace per customer.** Your files are imported into a
  workspace (tenant) created only for your organisation. Every query in the
  product is scoped to that workspace, and attempts to cross between
  workspaces are tested against a real database before any change ships.
- **Only what's needed is kept.** The importer checks the whole file before
  writing anything. Columns it doesn't recognise are ignored and **never
  stored**. A file with any invalid row is rejected entirely, with the
  problems listed, so nothing half-imported lingers.
- **Who can see it:** the named person delivering your diagnostic, and anyone
  from your organisation you ask us to give access to. Nobody else.
- **Where it runs:** on equipment and hosting we control, in the UK, unless
  agreed otherwise in the proposal. The storage location is recorded before
  transfer.
- **No cost or rate data** is needed or read by the Evidence Check.

## What the product keeps

| Data | Why | How long |
|---|---|---|
| Imported work items and time | The Evidence Check's input | Until deletion (below) |
| A copy of each imported row (recognised columns only) | Lets a finding be traced back to exactly what you sent | Copies older than 90 days are removed each time a later import completes. There is no separate timer, so if no further import runs, the copies stay until the deletion below removes them with everything else. The operator owns that deletion, on the agreed date |
| A file waiting for you to confirm that it replaces an earlier one | Nothing is removed until you have seen what would go | Until you confirm or cancel; unconfirmed files are removed after a day, and by the deletion below |
| Recorded reviews: findings, the source record ids behind them, owners and decisions | The board pack and next cycle's comparison | Until deletion |
| People seen in the files but not yet matched | So nobody's work is silently dropped | Until deletion |
| An audit trail of imports, reviews and decisions | Who did what and when | Until deletion; a single record of the deletion itself (counts only) is kept as evidence that it happened |

## Deletion

On the agreed date, or earlier on request:

1. Your workspace is **archived**, which immediately blocks all access to it.
2. All delivery data in the workspace is **permanently deleted** in one
   operation (work items, time, reviews, identity matches, raw copies and
   audit rows). The count of records removed from each table is shown before
   deletion and recorded afterwards.
3. Any people records created to match your staff are erased through the
   product's per-person erasure route.
4. We confirm deletion in writing, with the record counts, within five
   working days.

Downloaded outputs (the board pack and CSV registers) belong to you. Any
copies we hold are deleted on the same date.

## What we don't claim

This is an assisted diagnostic, not a certified service. We don't hold
ISO 27001 or SOC 2 certification, and we don't claim your data never leaves
your perimeter: you send us exports. If you need a data processing agreement,
we'll sign yours or provide ours before any file is transferred.

---

*Internal note (remove before sending):* step 2 is the Platform Admin
"Delete delivery data" action on `/staffops/platform/tenants`, which refuses
to run unless the tenant is Suspended or Archived. Step 3 uses the GDPR
erasure route in `docs/gdpr.md`. Backups: if the environment is backed up,
state the backup retention in the proposal; deletion from backups happens
when they expire.
