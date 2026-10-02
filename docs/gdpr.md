# GDPR: export, erasure, retention

Admin-only, added under Staff Ops (`Services/Staff/GdprService.cs`,
`StaffAdminController.Export` / `.Erase`) — the same gate as the cost/rate
data these actions can touch, plus the tenant boundary: an Admin can only
export or erase a `StaffKey` in their own tenant (NotFound otherwise). The
wider retention/lifecycle/DSR picture is in
[`data-governance.md`](data-governance.md); this file stays the reference
for the two mechanisms themselves.

## Export (Article 15 — right of access)

`GET /staffops/admin/{staffKey}/export` returns a JSON file of every field
this application holds against that `StaffKey`: profile, availability,
leave requests, `StaffRate` history, `WorkHoursHistory`, and the
**audit trail** of admin actions recorded against that key
(`StaffOps_AuditLog` — creation, rate changes, MFA resets, previous
exports/erasures), since "what has been done with my data" is part of the
right of access. It's an Admin-triggered action (a
subject access request comes in and an Admin runs it), which is why it's
allowed to include `StaffRate` — the codebase's cost-isolation rule is about
which *code paths* may fetch `StaffRate` (never a Team Lead one), not
whether an Admin-gated action may, and `StaffAdminController.Detail`
already fetches it the same way.

## Erasure (Article 17 — right to erasure)

`POST /staffops/admin/{staffKey}/erase` overwrites `FullName`, `Email`,
`JobTitle`, `Department` with placeholders and sets `IsActive = false`.
Deliberately **not** a row delete:

- `Availability`, `LeaveRequest`, `StaffRate`, and `WorkHoursHistory` rows
  stay linked by `StaffKey` — deleting them would corrupt historical
  cost/utilisation figures that other people's reporting depends on. This is the
  pseudonymisation-not-deletion trade-off GDPR itself allows when full
  erasure would conflict with another legal basis for retention (here:
  financial record-keeping).
- The linked **Umbraco Member** account (login credentials) is untouched by
  `/erase`. The Delete action an Admin sees in Settings → People
  (`POST /staffops/admin/{staffKey}/delete`) runs this same erasure and then
  deletes the login, so the person is gone in one step; nobody can delete
  themselves or the organisation's last active Admin.

### Skill assertions are deleted, not pseudonymised

`SkillsEvidence_StaffSkillAssertion` is the one exception to the rule
above, and the reason is the rule's own reason: pseudonymisation is what
you do when a row has a *second* purpose that outlives the person. A cost
record does — payroll and tax. A rate history does. A proficiency
assertion does not: "someone was a Practitioner in Billing" is meaningless
detached from who that someone was, so there is nothing left worth
keeping, and `SkillsEvidenceDataParticipant.EraseAsync` deletes every row
for the subject, superseded history included.

What that changes downstream is stated rather than hidden:

- **The coverage view legitimately drops.** A component that had two
  reviewed maintainers now shows one, and may raise a single-maintainer
  warning it did not raise before. That is the truth for continuity
  planning — the person has gone — and is far better than an anonymous
  "ghost maintainer" row that would make a real staffing gap invisible.
  `SkillsEvidenceGdprTests.After_erasure_the_coverage_aggregate_honestly_drops`
  pins this.
- **`SkillsEvidence_SkillDefinition` is untouched.** A taxonomy entry is
  the organisation's vocabulary, not personal data, and stays even if the
  erased person was its only holder.
- **The audit trail survives**, consistent with every other `*_AuditLog`
  in this codebase (6-year retention, never edited). It keeps the
  now-pseudonymous `StaffKey`, the skill, the status and the reviewer —
  the accountability skeleton — and an `AssertionsErasedForSubject` row
  with a count. It deliberately **never** carries the free-text notes (the
  staff member's own evidence note, the reviewer's rationale): those are
  the revealing part of the record, they live only on the rows that get
  deleted, and
  `SkillAssertionServiceTests.Audit_detail_never_carries_the_free_text_notes`
  fails the build if one leaks into the log that outlives the erasure.

Export is the mirror image: the participant returns **every** version of
the subject's own assertions, including superseded ones and the reviewer's
rationale about them, because all of it is their data under Article 15. It
resolves the subject's own tenant from their `StaffProfile` and filters on
it, so a mis-stamped row could never make the export the place a tenant
boundary is crossed; a subject with no resolvable tenant exports nothing
rather than everything.

### Support participation is detached, not deleted

Service Ops is the one place that does neither of the above, and the
reason is what the data is *about*. A skill assertion and an attributed
commit are claims about a person; detached from them they mean nothing,
so they go. A support case is a record of something that happened to a
**customer** and of how the organisation responded. Deleting the
participation row would not remove a claim about a person — it would
quietly rewrite the organisation's own incident history, changing how
long a case took to resolve and whether it was resolved at all.

So on erasure `ServiceOpsDataParticipant`:

- **deletes** the approved `ServiceOps_AgentLink` rows — the statement
  that a desk account *is* this person, which is the personal claim;
- **detaches** `ServiceOps_CaseParticipant` rows, clearing `staffKey`
  and the agent's display name while keeping the row, the role and the
  timings;
- leaves `ServiceOps_SupportCaseFact` and `_SupportCodeLink` untouched.

The consequence is asserted, not assumed:
`ServiceOpsGdprTests.The_case_figures_are_unchanged_by_an_erasure` pins
that the service report reads identically before and after — unlike the
skills and evidence aggregates, which legitimately drop. Links are
deleted before participation is detached, so a concurrent sync cannot
re-attribute through a link that still existed.

A reviewed root-cause finding also survives the erasure of its reviewer.
It is an assessment about a change, and the reviewer key becomes
pseudonymous like every other audit reference — the alternative is an
unexplained causal claim with no accountability at all.

### The continuity plan is detached too, for the same reason

Component ownership follows Service Ops rather than the skills matrix,
and the rule across all four slices is now stateable in one sentence: a
record is **deleted** when it is a claim about the person and means
nothing without them, and **kept** when it is a record of the
organisation that removing would falsify.

"Billing has one owner and no approved backup" is a fact about the
business's exposure. Deleting the ownership row when its owner is erased
would not remove a claim about that person — it would delete the
component from the risk register, which is exactly the exposure the view
exists to find. So on erasure `ContinuityDataParticipant`:

- leaves the component and clears its owner, so it surfaces as
  **unowned** and sorts to the top of the coverage view;
- **deletes** the subject's approved backup rows, because an approved
  backup who has left is not cover and saying otherwise is a false
  reassurance;
- unassigns any coverage action they owned, keeping the rationale, so it
  surfaces as needing reassignment rather than vanishing from the plan.

`ContinuityTests.Erasure_leaves_the_component_unowned_rather_than_deleting_it_from_the_register`
pins the first of those.

## Executive review

The [executive data lifecycle](executive-data-lifecycle.md) now participates in staff export and erasure, even while executive reporting is disabled. It exports journal history with structured subject references and removes whole affected journals on erasure; it also clears capture/settings/lifecycle actor references. Administrators must review third-party information before releasing an export and manually find unstructured mentions in unrelated journals. Data controls provide preview/confirmation for whole-journal redaction, pack withdrawal, retention and executive-module purge. All registered participant erasures and profile deactivation now share a transaction in the composed `IGdprService`; failure rolls them back together.

## Retention

The canonical retention table (including Bronze captures, audit logs and
archived tenants) and the operator runbooks now live in
[`data-governance.md`](data-governance.md). The staff-record rows below are
repeated here unchanged. No automated purge job exists for them; this is
the documented policy an operator should apply manually (or automate
later) once real employees' data is in scope:

| Table | Recommended retention | Basis |
|---|---|---|
| `StaffOps_Availability`, `StaffOps_LeaveRequest` | 3 years after the staff member is deactivated | UK statutory minimum for employment records (ACAS guidance on holiday/absence records) |
| `StaffOps_StaffRate` | 6 years after the staff member is deactivated | UK payroll/tax record-keeping (Taxes Management Act 1970 s.12B; HMRC guidance on PAYE records) |
| `StaffOps_WorkHoursHistory` | 3 years after the staff member is deactivated | Same basis as availability/leave records — it's a working-pattern record, not a payroll one |
| `SkillsEvidence_StaffSkillAssertion` | Deleted on erasure (above). Otherwise: until the staff member is deactivated, then purge — no legal basis holds it open | Not a payroll or employment record. A validated level also expires on its own review date (12 months), so a stale row is visibly stale before it is purged |
| ClickUp-sourced Programme Ops data (`TimeEntry`, `WorkItem`, etc.) | Follows the retention of the contract/programme it belongs to, not the individual | Not personal-data-driven — see docs/programme-ops.md |

These windows are a starting point, not a legal opinion — confirm against
the organisation's actual data protection policy before relying on them.
