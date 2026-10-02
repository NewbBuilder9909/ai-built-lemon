# Chief Product Owner readiness review

26 September 2026. A Chief Product Owner and four product owners
(evidence/core, intake/integrations, platform/security, commercial) reviewed
the repository, its commercial documents and its public page, as if they had
just been handed the product. It builds on
[`competitive-position-2026-09-26.md`](competitive-position-2026-09-26.md)
and does not replace it: that document's positioning is right. This one says
what stands between that positioning and a paid customer, and what was built
today to close the product side of the gap.

Everything here is judgement from repository evidence. None of it is
customer evidence, because there isn't any yet.

## The verdict

**The engineering is well ahead of the stage. The commercial side hasn't
started.** In 27 days (first commit 31 August), the repository has grown to
94 commits, about 46,000 lines of application code, 26,000 lines of tests,
28 controllers, 90 views and 12 feature areas, with tenancy, MFA, GDPR, audit
and security controls most seed-stage products don't have. Against that:

| Commercial fact | Evidence |
|---|---|
| Prospects recorded | **0**. `prospect-scorecard.csv` holds only the example row |
| Discovery conversations logged | 0 |
| Paid diagnostics | 0 |
| Deployed environment a prospect could open | None (claim register: "no deployed environment is configured") |
| Product name cleared | No (claim register: possible PPM name collision) |
| One price, stated once | No. Three different price stories (see gap G2) |

The 30-day gate says it plainly: *"Connector count, test count, page views and
complimentary feedback do not pass this gate."* By the team's own rule, the
product has been built past the point where more code changes the outcome.
**The bottleneck is conversations, not features.**

## Readiness by dimension

| Dimension | Rating | Why |
|---|---|---|
| Positioning | **Green** | "Can this week's delivery report be trusted, and what needs an owner?" is specific, defensible and uncrowded. Stop rewriting it: five strategy documents in five days is itself a warning sign. |
| Core product (the wedge) | **Amber, now Green for a diagnostic** | The Evidence Check was a one-off report with no memory: no owner, no decision, no trend, so nothing to subscribe to. Fixed today (see "Built today"). |
| Offer and pricing | **Red** | The public page sells a diagnostic, a self-service trial and three monthly plans at the same time, at prices that disagree with the playbook. |
| Proof | **Red** | No customer, no reference, no measured time saving. Expected at this stage, but it is the only thing that matters now. |
| Sales assets | **Amber** | Outreach templates, proposal, evidence-pack template and ROI check exist. There was no demo dataset, so a first call needed the prospect's data. Fixed today. |
| Trust and procurement | **Amber** | Controls are strong for the stage; the claim register is honest. No hosted environment, no DPA or diagnostic data-handling note, and no way to delete a customer's diagnostic data on request. |
| Operations | **Amber** | Syncs run inside the HTTP request (R18), there are no outbound notifications (R31) and no hosting. None of that blocks an export-based diagnostic, which the founder can run. |
| Focus | **Red** | 12 feature areas for a product whose first sale needs three: import, the Evidence Check and the identity queue. |

## What each product owner would say

**Chief Product Owner.** "You have one product that can win and eleven that
can't yet. The Evidence Check is differentiated. Leave requests, TOTP,
branding, skills evidence, service health, invoicing and the ERP demo are all
competent, but each is another thing to secure, explain and support. Freeze
them. For the next 30 days the only roadmap is the diagnostic journey: export,
import, check, review, board pack, next week's check. Anything not on that
path waits for a paying customer to ask for it."

**Product owner, evidence core.** "Until today the check forgot everything the
moment you closed the tab. The CSV had empty Owner and Decision columns for the
reviewer to fill in by hand in a spreadsheet, which is exactly the manual
reconciliation we claim to remove. The £1,500–£2,500 monthly repeat rests on
week-over-week movement and decisions that stick. That loop now exists. Next,
the reviewer needs to map a customer's statuses themselves: in every real
diagnostic, 'unmapped status' will be the loudest finding, and at present the
only fix is to edit the CSV."

**Product owner, intake.** "Credential-free CSV import is the right entry
route. Don't add another export format until a prospect hands us one. The
identity queue will be the first friction a customer feels: every assignee is
unmatched until someone links them, so the first check always reads
*not decision-ready*. That's honest, but the playbook should say so, so the
first playback doesn't feel like the product is broken."

**Product owner, platform and security.** "For a £3,500 diagnostic, the buyer's
security question is short: where does our export go, who sees it, and when is
it deleted? We can answer the first two. We can't do the third on request:
there's no tenant data purge, and archived tenants age out on retention. Build
that before the second customer, and write a one-page data-handling note
before the first. SOC 2, ISO and perimeter claims are irrelevant at this
price. Don't start them."

**Product owner, commercial.** "The shop window contradicts the strategy. The
hero of `/purchase` says *assisted diagnostic, not self-service*. Scroll down
and the same page offers a self-service trial, a £495/month 'ClickUp-backed
reporting' Starter plan and a heading that reads 'Modular plans you can sell
and gate by tenant', which is our own language, not a buyer's. The Evidence
Check isn't mentioned on the page at all. A delivery director who scrolls will
leave unsure what we sell, and at what price."

## Critical analysis: the hard truths

1. **Building has been substituting for selling.** The playbook's first-week
   target is 15 named prospects. The scorecard has none. Every day spent on
   code now has a lower expected return than one discovery conversation.
2. **The offer is incoherent in public.** See gap G2. A buyer should meet one
   offer, one price and one next step.
3. **Surface area is a liability until it's paid for.** Each extra module adds
   attack surface, GDPR scope (TOTP seeds, leave, skills evidence, GitHub
   activity) and questions in due diligence. A CTO reviewing a "delivery
   evidence diagnostic" who finds a leave-approval system will ask what the
   company actually does.
4. **The wedge had no memory.** Now fixed. Before today, nothing in the
   product gave a customer a reason to come back next week.
5. **The name isn't safe to invest in.** Every outreach message builds recall
   for a name you may have to drop. A clearance check takes an afternoon.
6. **The strategy keeps moving.** There's an EXCO pivot (22 September), a
   stakeholder value review (22 September), a gap analysis (25 September), a
   competitive position (26 September) and a first-sale playbook
   (26 September). The positioning has converged. Now it needs customer
   contact, not another rewrite.

## Market position (judgement, not research)

Where ProgrammePulse can beat the incumbents:

- **Neutrality.** Jira, ClickUp, Monday, Asana, Planview, Kantata and the
  PSA tools all want to be the system of record. None will tell an executive
  that its own numbers can't be trusted, and none sees the other tools.
- **No migration.** Their answer is "move everything into us". Ours works from
  Monday's exports.
- **Uncertainty as the product.** Their dashboards and AI status summaries
  produce confident narrative from the same incomplete data. As
  AI-written status reports become normal, *"AI will write your status
  report; we tell you whether it's true"* becomes a sharper line. Test that
  line in discovery calls; it is a hypothesis.
- **The real incumbent is Power BI plus an analyst**, rebuilding bespoke
  reconciliation rules at every firm. The Evidence Check sells those rules
  ready-made, with stated thresholds.

Where it loses today: no brand, no reference customer, one founder,
no hosted environment, no certification, and no listing in any tool's
marketplace. Only paid diagnostics fix the first two. The rest don't matter at
£3,500.

## Gap analysis

| # | Gap | Current state | What's needed to win the first three | Priority |
|---|---|---|---|---|
| G1 | Demand evidence | 0 prospects, 0 conversations | 15 named prospects, 3 conversations, 1 proposal in week 1–2 | **P0, founder** |
| G2 | Offer and price coherence (**closed by B4, below**) | `/purchase` sells a diagnostic, a self-service trial and plans at £495/£995/£1,995 per month; `Commercial:DiagnosticFee` is £2,000 and `MonthlyRepeatFee` £1,000; the playbook says £3,500 and £1,500–£2,500 per month; `purchasing-and-deployment.md` says the page provisions nothing, but it does | One offer, one price, one call to action on the public page; the self-service trial and plan catalogue switched off until a customer asks | **P0, decision** |
| G3 | Recurring loop | Evidence Check was stateless | Record, decide, carry forward, compare | **Closed today** |
| G4 | Board artefact | CSV plus a hand-filled Markdown template | Printable pack generated from the recorded review | **Closed today** |
| G5 | First-call demo | Needed the prospect's own data | Realistic sample exports that trip every rule | **Closed today** |
| G6 | Name | **Confirmed conflict**: BearingPoint sells "ProgramPulse", a PPM tool for PMOs (see claim register, "Naming"). The public page now uses a descriptive name | Trade mark (UK IPO, EUIPO), Companies House and domain check before outreach at scale | **P0, founder** |
| G7 | Diagnostic data handling | No DPA note; no way to delete a customer's data on request | One-page data-handling note now; tenant data purge with confirmation before customer two | **P0 note, P1 build** |
| G8 | Status mapping by the reviewer | Keyword mapper only; fix means editing the CSV | Per-tenant status-mapping override on the import page | **P1** |
| G9 | Focus of the product surface | 12 areas visible to an Admin | A "Diagnostic" plan whose entitlements show only import, the Evidence Check, identities and reviews | **P1** |
| G10 | Pre-review digest | Not built; no outbound email (R31) | Readiness band and top five exceptions emailed on review morning | **P1, pulled by the first repeat customer** |
| G11 | Hosted environment | None | One small, disposable vendor-hosted pilot environment holding the sample tenant, so a link can be sent | **P1** |
| G12 | Background execution | Syncs run in-request (R18) | Needed for live connectors at scale, not for export-based diagnostics | **P2** |

## Where to focus

**One journey, end to end, and nothing else until it has been sold three
times:**

> Sanitised export → `/staffops/programme/import` → Evidence Check → record
> the review → owners and decisions → board pack → next week's check shows
> what moved.

Every item in the backlog below either shortens that journey, makes its output
more convincing to a delivery director, or reduces the founder's delivery
hours per diagnostic. The 30-day gate's stop rule, *"diagnostic delivery
consumes likely annual contract value"*, is the one most likely to end this,
so delivery efficiency matters as much as features.

## Backlog

### Built today on this branch

| Item | What it does | Where |
|---|---|---|
| **B1 Recorded reviews** | "Record this check for the review" freezes the check, including source records, so the review is reproducible after data changes. Each finding takes a decision (fix at source, accept and state, disputed, resolved), a named owner from the tenant's active staff and a target date. Fixing at source needs an owner and date; accepting or disputing needs a written reason. Decisions carry forward to the next recorded check and are marked as carried until someone revisits them. A finding marked resolved that is still found reopens. Only the latest review can take decisions; earlier ones are frozen as the record. Audited, tenant-scoped, and included in GDPR export and erasure. | `/staffops/programme/evidence-check/reviews`, `Services/ProgrammeOps/EvidenceReview*.cs`, migration `2026-09-programmeops-25` |
| **B1a Week-over-week** | The live check and every recorded review show each finding as new, worse, no change, improved or cleared against the previous review. Movement is judged on the count, not the share, so more clean data can't make a finding look fixed. Decisions that didn't hold (marked resolved but still found, or past their target date) are flagged. | Evidence Check page, review page |
| **B2 Board pack** | A self-contained, printable pack for a recorded review: readiness band and reason, changes since the last review, decisions still needed, both finding registers with owners and decisions, per-project view, limitations, and a source-record appendix. It states that it is not an audit, not independent assurance and reads no cost data. The review's CSV now carries owner, decision, target date and note. | `/reviews/{key}/pack`, `/reviews/{key}/export` |
| **B3 Sample data** | A fictional agency's work-item and time exports, downloadable from the import page, with dates relative to today. A test pins that they import unedited and trip 13 of the 14 Evidence Check rules; the fourteenth, *no recorded time at all*, can't fire alongside time data. | `/staffops/programme/import/sample/{kind}`, `DeliveryExportSample.cs` |

Verification: unit tests for every review rule, service tests using the
repository's fakes, and an HTTP + SQL Server integration test covering the whole
loop: import, record, refused and accepted decisions, a foreign-tenant owner
refused, export, pack, fix at source, re-record, cleared, the old review
frozen (409), a read-only analyst refused, a foreign tenant getting 404 for
the review, pack and export and not seeing it in its history, and GDPR
export and erasure.

### Built on 26 September, second batch

| Item | What it does | Where |
|---|---|---|
| **B4 One offer on `/purchase`** | The public page sells one thing: the fixed-fee diagnostic at £3,500 (founding price, first three organisations), then the monthly evidence review at £2,000 a month, only if the diagnostic earns it. The example finding is now a real Evidence Check finding. The self-service trial, plan catalogue and add-ons sit behind `Commercial:SelfServiceTrialEnabled`, off by default, and `POST /purchase/start-trial` provisions nothing while it's off. Seller language is gone from buyer copy, and the conflicting product name is replaced by the configurable `Commercial:ProductName` ("Delivery Evidence Check"). | `Views/Purchase/Index.cshtml`, `CommercialOptions` |
| **B5 Diagnostic data purge** | A Platform Admin can delete one customer's delivery data from every Programme Ops table, but only once the tenant is suspended or archived and after typing its short code. The page shows counts before, and a printable deletion record after; the counts stay in the platform audit log. A test fails the build if a new tenant-scoped table isn't covered. | `/staffops/platform/tenants/{key}/purge`, `DeliveryDataPurge.cs` |
| **Data-handling note** | The one page a buyer's security contact reads: what we ask for, where it goes, what's kept, how long, and how it's deleted. | [`diagnostic-data-handling.md`](../commercial/diagnostic-data-handling.md) |
| **Name check** | Confirmed conflict with BearingPoint's "ProgramPulse" PPM tool. Recorded in the claim register. | [`claim-register.md`](../commercial/claim-register.md) |
| **Prospect safety** | The repository is public, so named prospects live in a git-ignored `*.local.csv` copy of the scorecard. | `.gitignore`, playbook |

### P0: this week, founder-owned (no code)

1. **Fill the scorecard.** 15 named prospects from your own network first.
   Book three conversations. In each, run the sample data live: import, check,
   record, assign an owner, print the pack. That's a 10-minute demo that needs
   nothing from them.
2. **Decide one offer and one price** (gap G2). Recommendation: the £3,500
   founding diagnostic, with the £1,500–£2,500 monthly evidence review as the
   only follow-on, and nothing else on the public page. Then build B4.
3. **Clear the name** (gap G6) before the first outreach message.
4. **Write the data-handling note** (gap G7): where the export is stored, who
   can see it, retention, and deletion on request. One page.

### P1: build only when a diagnostic is signed, in this order

| Item | Why | Acceptance |
|---|---|---|
| **B4 Align `/purchase` to the one offer** (**built 26 September**, see below) | A buyer must meet one offer. Put a synthetic Evidence Check finding in the hero; put the self-service trial and plan catalogue behind a `Commercial:SelfServiceTrialEnabled` flag, default off; move seller language out of buyer copy. | The page renders one offer and one price; the trial endpoint returns 404 while the flag is off; render tests updated |
| **B5 Diagnostic data purge** (**built 26 September**, see below) | Answers "when is our data deleted?" | Platform Admin previews, confirms and purges one tenant's Programme Ops data; audited; a certificate of deletion can be printed |
| **B6 Per-tenant status mapping** | Removes the loudest source of rework in every diagnostic | The reviewer maps an unrecognised source status to a stage on the import page; re-import applies it; the mapping is shown as a stated definition in the pack |
| **B7 Diagnostic plan** | Stops a prospect seeing 12 modules | A `Diagnostic` plan whose entitlements expose only import, the Evidence Check, identities and reviews |
| **B8 Pre-review digest** | The subscription's weekly heartbeat | On review morning, the owner receives the readiness band, movement since last review and the top five exceptions; needs an SMTP sink (R31) |
| **B9 Hosted pilot environment** | Lets you send a link | One vendor-hosted environment with the sample tenant, enquiry email configured, `/umbraco` allowlisted (R24) and `/opshwb` removed (R25) |

### P2: only after two paid diagnostics

- Additional export formats (Monday, Asana, Harvest, Smartsheet), each proven
  on a real prospect file first.
- Scheduled weekly recording (needs the background execution from R18).
- A readiness trend chart across recorded reviews.

### Don't build

More live connectors, AI-generated summaries, resource optimisation, further
skills, evidence or service-health slices, branding, contract or invoicing
features, multi-market, self-service billing, SOC 2 or ISO preparation. These
are either the incumbents' ground or answers to questions no buyer has asked
yet. Freeze them as they are: no new work, and no removal either, until a paid
customer's need or objection decides it.

## What would change this view

Apply the 30-day gate literally. One paid diagnostic with an accepted finding
means improving the same journey. Interest without an artefact, owner or budget
means changing the segment, not the product. If a prospect's existing tool
already answers "can this report be trusted", that proposition stops. The next
review of this document should be written after the third discovery
conversation, and should quote those conversations.
