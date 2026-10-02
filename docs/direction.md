# Build direction and status

## Stopped, 2 October 2026

The project stopped here and was published as a lesson. Read
[READ-ME-FIRST.md](../READ-ME-FIRST.md) for what was built and why it was
stopped. In short:

- **No buyer was ever asked.** Thirteen strategy reviews were written; no
  conversation with a potential customer took place.
- **The obvious substitute was missed.** For a team working in one tool, the
  AI built into that tool (Rovo in Jira, Brain in ClickUp) already does most
  of what this product offered, at no extra cost on paid Jira plans.
- **The one possibly real problem was never tested:** checking delivery
  figures that come from several organisations' tools. The Patchwork data set
  shows the capability; nothing shows the demand.
- **The owner chose to stop** rather than spend more time without evidence
  that anyone would pay.

Lessons, for anyone building with AI agents:

1. No code until five people who would pay have described the problem.
2. One screen in front of one real user in the first week.
3. Measure users and money, not tests and documents.
4. Make the agent argue for stopping before every new area.
5. List competitors from the buyer's desk, including the AI they already have.
6. Keep one page of strategy current instead of writing new reviews.

Everything below is the plan as it stood when the project stopped, kept as a
record.

---

**Last updated 2 October 2026.**

This is the single source of truth for what the product is for, where it
stands and what to build next. Any other document that disagrees with it
is out of date. Where a dated review says otherwise, this page wins.

**Keeping it true.** Update this page in the same pull request that changes
a status below. When a new review or assessment is written, fold its
actions into the backlog here, link it as the basis, and move the review it
replaces to [archive/](archive/). Registers stay where they are and are
linked, not copied: risks in [release-readiness.md](release-readiness.md),
security in [security-assurance.md](security-assurance.md), and what may be
said publicly in [the claim register](commercial/claim-register.md).

## Where we stand

**Credible diagnostic workbench; incomplete commercial service; no-go for
an open, recurring SaaS launch.** Basis: the
[GTM readiness review of 28 September](commercial/gtm-readiness-review-2026-09-28.md).

| Commercial motion | Decision |
|---|---|
| Demo on synthetic data (Northstar) | **Ready** for an operator-led rehearsal. No public demo deployment verified |
| Operator-delivered export diagnostic (£3,500, ten working days) | **Conditional go**, once the enquiry handoff, agreed data handling and a scoped, reconciled deliverable are in place |
| Repeat assisted review (£2,000/month) | **Not yet demonstrated.** Needs the P0-5 acceptance test below, plus a customer who accepts the outcome and continues at that price |
| Customers using a hosted instance with real data | **No-go** until the [pilot gates](assayer-pilot-gates-2026-09-27.md) pass on the candidate |
| Open self-service SaaS | **No-go.** Sign-up is off by design; invitations, recovery, provisioning and billing are incomplete |

No customer outcome, paid conversion or repeat purchase is recorded in
this repository. Prospects are tracked outside git, so an empty scorecard is
not evidence of no demand.

## What we are building, and why

**Proposition to test:** for a delivery lead preparing a recurring client or
portfolio review, turn agreed task and time exports into a traceable
exception register, reviewed decisions and a shareable pack, with explicit
gaps and limitations.

**Direction:** make one loop work end to end and tell the truth:
**scope → import → reconcile → check → decide → pack → repeat.** Prove it
with two successive realistic export cycles before selling repetition.

**Not now,** unless a paying customer's agreed engagement needs it: new
feature areas or connectors, self-service sign-up, billing
automation, extra plan tiers, multi-region, live syncs of the evidence
modules. Dependency and security maintenance continues, but is reported
separately from commercial progress.

**The bench (decided 1 October).** Everything outside the loop is a module,
off for every tenant and every role until an Admin switches it on in
Settings → Modules: Reporting hub, Delivery load, Estimate calibration,
Contracts and assurance, Skills and continuity, Service health, Staff
self-service, Executive review. A module is not deleted or abandoned; it is
switched on when a customer's engagement needs it. Adding to the core means
moving a controller into the core list in `Architecture/ModuleBenchTests`,
with a reason that ties it to the loop.

## Backlog

### P0: prove it or stop (2 October)

The owner doubts the product has value over the AI already inside the
tools: Rovo is included on paid Jira plans, and ClickUp sells Brain2. For a
team working in one tool, it probably has none. The one problem those
assistants structurally can't reach is checking figures that come from
*several organisations'* tools. Whether anyone pays to have that solved is
untested. Work data from the owner's employer is never used for this.

| # | Item | Status |
|---|---|---|
| Q-1 | A fictional programme across five organisations, each file in its tool's native export format, with an answer key marking which problems one tool can see | **Done** on `data/patchwork`: [TestScenarios/Patchwork](../TestScenarios/Patchwork/README.md), 13 planted problems, 9 only visible across organisations |
| Q-2 | Rovo benchmark on a personal Jira trial, scored against F01–F03 | Open (owner) |
| Q-3 | Five conversations with people who sign off figures from suppliers they don't control; no demo until they describe the problem | Open (owner) |
| Q-4 | Stop rule: stop if Rovo finds most of what matters inside one tool, or if fewer than two of the five describe a recent, costly incident and agree to a paid diagnostic | Proposed 2 Oct, not yet agreed |
| Q-5 | Readers for each native format. Today the importer accepts 1 of the 8 CSVs (Harvest, with nobody matched) and can't read the monday.com workbook | Not started; only if Q-2 and Q-3 justify it |

### P0: a usable core

Basis: the [usability review of 1 October](usability-review-2026-10-01.md),
after the owner signed in and judged the app unusable.

| # | Item | Status |
|---|---|---|
| U-1 | Bench the periphery behind per-tenant module switches, off by default, toolbox in Settings → Modules | **Done** (merged in #39). 51 of 91 GET routes are benched (`module:` column in the route snapshot); a benched page answers 404 with a short "switched off" page |
| U-2 | Five-item navigation: Review, History, Programmes, Data, Settings, plus a Modules group only for what is switched on | **Done** (merged in #39). Portfolio roles land on the Review; Staff land on My Work only while self-service is on, otherwise on a start page that says so |
| U-3 | Review page answers first: verdict and projects, then the five findings to look at first with their owner, everything else folded; one "how this is worked out" panel | **Done** (merged in #39). KPI tiles removed from the Overview (they repeated its attention list); RAID columns hidden while Reporting is off |
| U-4 | Search | **Done** (merged in #39): programmes, customers and work items by name or source id for anyone who can see the portfolio, people for those who manage staff. Bounded SQL read, tenant-scoped |
| U-5 | User administration: roles, suspend and restore, unlock, password reset link, delete; forgotten-password page | **Done** (merged in #39). No email service exists, so a reset is a one-time link the Admin passes on. Nobody can act on their own account; the last active Admin can't be suspended, deleted or demoted |
| U-6 | Branding reaches the sidebar | **Done** (merged in #39): rail text follows the secondary colour by contrast; the theme stylesheet address carries a version, so no cache can keep old colours. The hard-refresh symptom was not reproduced from the server headers, so this removes caching as a cause rather than proving the cause |
| U-7 | Three delivery leads outside the build team, own exports, timed on "could you present this on Monday?" | **Open.** Needs people; not runnable from the repository |

Ordered by customer consequence. Status is checked against code unless
marked otherwise.

### P0: truthful output (product and data engineering)

| # | Item | Status |
|---|---|---|
| P0-1 | Review scope: record the decision, programme/customer, period, extract date and calculation basis with each review, and compare like with like | **Done by decision, 29 Sep.** Every check and review has a declared scope: programme and/or customer, a period (default 7 days), an optional extract date (which staleness is then judged on) and an optional decision. Time findings use the period; delivery state is as of the check; estimate accuracy uses all recorded time; unlinked time outside a programme scope is stated. Reviews compare and carry decisions only within the same scope. Per-source extract dates are not captured: one date is declared for the check |
| P0-2 | Correction and replacement import | **Done by decision, 29 Sep: each upload replaces the last.** Rows missing from the new file are removed only after a staged preview shows counts, hours and the rows going, and is confirmed; a stale preview is refused. Removed work items unlink their time rather than delete it. One transaction. A corrected entry without `EntryId` now replaces its old version (3 h to 4 h reads 4, not 7). Exporting the whole period each cycle is now an operating rule for the diagnostic |
| P0-3 | Record-aware comparison: persistent/new/cleared records; decisions bound to records | **Done** in `620f9c1`: records matched by `(source, externalId)`; "same count, different records"; decisions carried only while a covered record remains |
| P0-4 | A stale source can't be "Decision ready" | **Done** in `620f9c1`: over 7 days caps it at "Use with caveats". A declared extract date now counts too (P0-1) |
| P0-5 | Two-cycle acceptance test: correction, deletion, stale source, replacement exceptions, replay, all reconciling to agreed counts and hours | **Open, next.** P0-1 and P0-2 are done; this is the acceptance test that proves them together: two realistic cycles with a correction, a deletion, a stale extract and replacement exceptions, reconciling to agreed counts and hours through recorded, scoped reviews |
| P0-6 | One number per fact on the pages a buyer sees | **Done** on `gtm/evidence-display`, not yet merged (found 30 Sep, [design and PMO data review](design-and-pmo-data-review-2026-09-30.md) B3, B4, D3, D12). "Overdue" was 14 on the Programme Overview and 9 on the Evidence Check: now one rule, `WorkItemDueDate.IsOverdueOn` (open, readable status, due before today in whole days), used by the overview, programme health, My Work and the check, with past-due items of unreadable status named separately. Trend snapshots stored today's state under any period: capture is now refused outside the period's last day to 7 days after, the page says which figures cover what, and the demo captures one real snapshot instead of three backdated ones. Project bands now sit beside the portfolio band on the check, the review and the pack. Record detail reads "1 day late, stage: in progress" |

### P0: conversion (commercial owner)

| # | Item | Status |
|---|---|---|
| C-1 | One working route from enquiry to proposal, invoice and scheduled start. `Commercial:EnquiryEmail` ships blank, so the default `/purchase` sends nothing | Open (operator) |
| C-2 | No disabled trial or plan links in the active journey | **Done** in `620f9c1` for login; `/purchase` already hid them |
| C-3 | Home page and offer describe the same buyer, scope and outcome (home still leads on reporting/capacity) | Open (per the 28 Sep review, not re-checked) |
| C-4 | Marketing site (`NewbBuilder9909/Assayer`) released: legal entity, booking URL, email receipt, privacy review, name clearance | Open (branch `claude/awesome-pascal-hyflk2`, not on its `main`) |

### P0: safe delivery (service owner)

| # | Item | Status |
|---|---|---|
| S-1 | One agreed processing and deletion runbook | Text corrected in `620f9c1` ([data handling](commercial/diagnostic-data-handling.md), [data governance §6](data-governance.md)); **not rehearsed** |
| S-2 | Raw import copies disposed of on a schedule, not only when a later import runs | Open (documented as operator-owned meanwhile) |
| S-3 | Hosted real data: deployment, key/restore, monitoring and access-recovery gates | Open, see [pilot gates](assayer-pilot-gates-2026-09-27.md) |

### P1

| # | Item | Status |
|---|---|---|
| P1-1 | Diagnostic workspace: scope, import, mappings, check, decisions and pack on one path with a visible next step and acceptance state. No new plan tier required | Open |
| P1-2 | Customer-configurable status mapping (unknown vocabulary currently needs CSV edits) | Open |
| P1-3 | Clear identity approval then re-import sequence (don't weaken the no-email-attribution rule) | Open |
| P1-4 | First paid diagnostic: baseline, accepted findings, actual delivery cost, explicit repeat/stop decision | Open (founder), gated by the [30-day commercial gate](commercial/30-day-commercial-gate.md) |

### P1: what the buyer sees

Basis: the [design and PMO data review of 30 September](design-and-pmo-data-review-2026-09-30.md).
The data a buyer pays for exists; it is shown almost entirely as tables and
prose. Work is organised around five agreed trigger metrics (readiness by
project, hours without evidence, effort over estimate, slipped commitments
without an owner, and whether last week's decisions held), drawn
server-side as HTML and CSS bars (no chart library, no script) with the
table or figures kept as their twin. No gauges, scores,
earned value or per-person charts.

| # | Item | Status |
|---|---|---|
| V-1 | Display layer of P1-1: chart partials and `--ops-viz-*` tokens; the Evidence Check verdict block (band strip, project matrix, tiles with denominators), findings as proportion bars; page one of the board pack; Evidence Check and Recorded reviews in the navigation. Accepted when three people outside the build team, shown page one of the Northstar pack for five seconds, can say whether it can be used, the biggest exposure and how many problems lack an owner | **Built** on `gtm/evidence-display` (server-rendered HTML bars in `charts.css`, no chart library). **Acceptance not run**: the five-second test needs people outside the build team, and the accessibility audit needs Node |
| V-2 | Recorded-review history: band per review, findings by kind, decisions held versus broken. Also: once a review has a previous one, the pack's movement table pushes page one past one A4 sheet (it fits today, with about 45px to spare, only because Northstar has one review); show only the findings that moved, or put movement on its own page | Open, after P0-5 produces two real cycles |
| V-3 | Programme Overview: *Needs attention* first, tiles with denominators, milestone timeline, overdue by age, person tables alphabetical | **Built** on `gtm/evidence-display`. The duplicate programme table was merged into the health table, reasons folded into each row |
| V-4 | Reporting Hub variance split (never-estimated time is not overrun), contract burn by month, one shared scope bar | Later, demand-led |

### Later, demand-led

Invitations and durable provisioning ([saas-onboarding.md](saas-onboarding.md) P1 onwards),
billing automation, broader connectors, self-service.

## Other plans: parked, not cancelled

Each keeps its own document; none is the next piece of work.

| Plan | State |
|---|---|
| [Engineering roadmap](architecture-review-2026-09-24.md) | Phases 0–2 done. Open: A6 non-null `tenantId`, A2 indexing beyond Programme Ops, connector batching. Phases 3–5 not started. Do Phase 3 (background execution) only when scheduled recording or scale-out is needed |
| [Executive review / multi-market](exco-multimarket-build-script.md) | Increments 1 and 2 built, off by default; remaining phases parked |
| [Delivery evidence and contract assurance](delivery-evidence-and-contract-assurance.md) | Steps 1–3 built; security assurance never synced live; step 4 not started |
| [Skills and evidence](staff-skills-evidence-module.md), Service Ops | All slices built; never run against a live GitHub App or desk |
| [Jira and Tempo](jira-tempo-integration-scope.md) | **Active by decision, 28 Sep.** Sources and deletion reconciliation in `main`. Phase 3 report built: `/staffops/reporting/jira-tempo`, shown only to tenants with Jira or Tempo data. Tempo without Jira issues works through an identity-only Jira connection (no projects selected). Never live-tested. The stale branch `feature/jira-tempo-reconciliation` was retired; its commit is tagged `archive/jira-tempo-reconciliation-wip` |
| Azure DevOps evidence | In `main`, not live-tested. Local branch `feature/azure-devops-live-probe` is already equivalent to `main` |

## Evidence at this update

With the bench and the usable core (`ux/diamond`, 1 Oct): 2,205 tests passed,
none skipped (1,764 unit/architecture and 441 SQL/persona/render, including
`ModuleBenchIntegrationTests`, which proves over HTTP that a new organisation
starts benched, only its Admin can switch a module on, the switch reaches
that organisation alone, a reset link sets a password once, and a suspended
person can't sign in); all Razor views type-check. The demo tests ran against
a scratch database. Screenshots of the Review, Programmes, Search and a
benched page were checked at 1440px and 500px; true phone width was not
(headless Edge won't go below about 500px, and the accessibility audit needs Node).

Before that, with the evidence display (`gtm/evidence-display`): 2,115 tests
passed in one run; all Razor views type-checked.
The accessibility audit was not run (no Node on the build machine). No live connector,
payment, production enquiry, customer usability session or independent
security assessment has been run.

## History

Earlier reviews, briefs and gates are in [archive/](archive/README.md). They
explain why decisions were taken; their open items are not a backlog.
