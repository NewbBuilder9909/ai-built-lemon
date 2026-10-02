# Exception and evidence pack — delivery template

The deliverable of a paid Weekly Delivery Evidence Diagnostic. Copy this
file per engagement and fill it in. Preparing it by hand is expected and
acceptable; automate a step only once the same step has been measured as the
slowest one across at least two comparable cycles (see
[`paid-diagnostic-offer.md`](paid-diagnostic-offer.md) and
[`30-day-commercial-gate.md`](30-day-commercial-gate.md)).

**Acceptance for this pack:** the reporting owner reproduces every finding
from the agreed export without asking us a question, and every finding has a
disposition with a named owner. A pack that is fast but not reproducible
fails. A pack whose honest conclusion is "no material issue found" passes.

---

## 1. Scope and provenance

| Field | Value |
|---|---|
| Customer / tenant | |
| Engagement reference | |
| Reporting period covered | `YYYY-MM-DD` to `YYYY-MM-DD` |
| Report being reviewed | name, frequency, owner, approver |
| Decision this report supports | |
| Sources agreed (max 2) | |
| Projects in scope (max 5) | |
| Export taken at (extraction timestamp, UTC) | |
| Latest source record timestamp seen | |
| Definition / calculation version | `v1` — see §5 |
| Prepared by | |
| Reviewed with | name, role, date |

**Sources NOT in scope, and what that leaves unanswerable:**

- …

---

## 2. Coverage before findings

Coverage comes first because a precise number over incomplete data is worse
than no number. State the denominator every time.

| Measure | Numerator | Denominator | % | Note |
|---|---|---|---|---|
| Logged hours with an approved project/contract mapping | | | | |
| Logged hours with an effective cost rate (priced hours) | | | | |
| Work items with an estimate captured before work started | | | | |
| People resolved to a known identity | | | | |
| Bookings matched to a delivery demand for the same period | | | | |

**Records excluded from every figure in this pack, and why:**

| Excluded | Count / hours | Reason | Where they went |
|---|---|---|---|
| | | | |

---

## 3. Findings

One block per finding. Every field is mandatory — an empty field is itself a
finding about the data, so write "not available" rather than deleting the
row.

### F-01 — <one-line statement of the discrepancy>

| Field | Value |
|---|---|
| Scope | tenant / customer, project(s) |
| Period | |
| Source record IDs | list, or the exact filter that returns them |
| Source timestamp | when the source last changed these records |
| Extraction timestamp | when we took the export |
| Definition / calculation version | |
| Numerator | |
| Denominator | |
| Observed or inferred | **Observed** (read directly from the export) / **Inferred** (derived, and how) |
| Omissions affecting this finding | |
| Materiality | agreed with sponsor: threshold and whether this clears it |
| Consequence if unresolved | |
| Reviewer | who checked it on the customer side |
| Action owner | named person, not a team |
| Proposed decision | investigate / correct / accept / no action |
| Disposition | **accepted** / **corrected** / **rejected — reason** / **open, due `YYYY-MM-DD`** |
| Outcome verified on | date, by whom |

**How to reproduce:** the exact steps from the agreed export to this figure.
If a reader with the export cannot follow this to the same number, the
finding is not finished.

---

## 4. Effort measured

The PMO acceptance test. Measure the whole cycle, not report generation —
extraction, correction, reconciliation, review and rework, for every
participant. Do not claim an effort reduction from a single cycle.

| Step | Participants | Baseline person-minutes | Assisted person-minutes | Note |
|---|---|---|---|---|
| Extract from sources | | | | |
| Correct mappings / identities | | | | |
| Reconcile disagreements | | | | |
| Assemble the pack | | | | |
| Review and challenge | | | | |
| Rework after review | | | | |
| **Total** | | | | |

| | Value |
|---|---|
| Net person-minutes released this cycle | |
| Comparable cycles measured so far | |
| Change in volume or scope between cycles | |

Released time is **capacity, not cash**. It becomes money only if the
customer's finance function confirms it was redeployed or a cost avoided.
This pack does not claim that and must not imply it.

---

## 5. Definitions used

Every term that could be read two ways, with the reading applied here and
who agreed it.

| Term | Definition applied | Agreed with | Differs from customer's usual definition? |
|---|---|---|---|
| Reporting period | | | |
| Logged / actual hours | | | |
| Estimate | | | |
| Billable | | | |
| Complete / Done | | | |
| Planned allocation | | | |

**Planned time is not delivery.** A booking or allocation carries no
lifecycle stage and is never counted toward completion or % complete in this
pack.

---

## 6. Limitations

State these before the recommendation, not after it.

- Attribution limits (e.g. overlapping contracts sharing cost — a margin was
  not quoted for these; see `docs/contract-ops.md`).
- Cost figures that are value-to-date, not realised margin or cash.
- Remaining work: original estimate minus time logged is **not**
  automatically remaining effort.
- Availability: a booking is not demonstrated capacity.
- Anything counted that a fuller source would change.

---

## 7. Recommendation

One of exactly three, with the reason:

- **Stop.** The existing process already answers the decision reliably and
  more cheaply.
- **Repeat by export.** Named next period, named scope, quoted separately.
- **Scope a controlled connected pilot.** Only after the source acceptance
  and deployment/security gates in
  [`../release-readiness.md`](../release-readiness.md) are satisfied.

| | Value |
|---|---|
| Recommendation | |
| Sponsor decision | accepted / declined / deferred to `YYYY-MM-DD` |
| Decided by | |
| Next paid step, if any | |

---

## 8. Data handling closeout

| Field | Value |
|---|---|
| Files received (name, sender, date) | |
| Storage location | |
| Access list | |
| Retention date | |
| Deletion owner | |
| Deletion confirmed on | |

Unexpected fields received are rejected; keep the rejection record without
keeping the content. See
[`diagnostic-data-checklist.md`](diagnostic-data-checklist.md) and
[`../data-governance.md`](../data-governance.md).
