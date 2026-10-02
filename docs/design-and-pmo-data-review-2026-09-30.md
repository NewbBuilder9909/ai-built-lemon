# Design and PMO data review — 30 September 2026

**Verdict: the data is better than the display.** The product already holds
the evidence a PMO buyer pays for: a numerator and denominator on every
finding, source record ids, frozen reviews and record-level movement between
them. It shows almost none of it graphically, leads its pages with forms and
caveat prose, and buries its most commercially potent numbers in table rows.
The fix is not a redesign. It is a small set of server-rendered charts on the
four surfaces a buyer sees, organised around five agreed trigger metrics, plus
three truth defects that the charts would otherwise expose in a demo.

Actions are folded into [direction.md](direction.md) (P0-6 and V-1 to V-4).
This document is the evidence behind them, not a backlog.

## How this was reviewed

One review written from two lenses: a senior product designer, and a business
analyst with twenty years in PMO reporting and data. It is not two people, and
it is not a customer usability session. Treat the claims about what triggers a
purchase as hypotheses for the first paid diagnostic (P1-4) to test.

- **Code:** every view under `Views/StaffOps/Programme` and
  `Views/StaffOps/Reporting`, the Contracts views, `_Layout.cshtml`,
  `wwwroot/css/app.css`, `wwwroot/js/app.js`, the Evidence Check and review
  calculators, and the reporting and snapshot services.
- **Rendered pages:** the Northstar demo ([demo-data.md](demo-data.md)) loaded
  fresh into a scratch database on 30 September, with the site run from a
  scratch worktree of `f317b01` and pages captured at 1440px as
  `pm.sarah@northstar.test` (Team Lead). Every figure quoted below is from
  that run. Admin-only pages (Contracts, Cost) were reviewed from code only,
  because the review instance could not decrypt the seeded MFA secret.
- **Commercial basis:** the [paid diagnostic offer](commercial/paid-diagnostic-offer.md),
  the [GTM readiness review](commercial/gtm-readiness-review-2026-09-28.md) and
  the [claim register](commercial/claim-register.md).

## The designer's teardown

**D1. There is no graphical representation of data anywhere a buyer looks.**
Across 72 signed-in pages, the only graphic is a 72px progress bar in the
Programme Overview's programme table (`Programme/Index.cshtml:241`, `:286`).
The components that do visual work (`.ops-pipeline`, `.ops-status-summary`,
`.ops-activity-feed`) live only in the `/demo` operations area. The Trend page,
whose only job is change over time, is a table. The board pack, the one
artefact a client receives, is a text document.

**D2. The Programme Overview breaks the design system's first rule.**
[design-system.md](design-system.md) rule 1 says a page answers one question
first, with what needs acting on at the top. On a 1440×900 laptop the whole
first screen is a scope form, a "Scope:" line, four bare links and the top of a
nine-column "Programme health review" table whose prose cells wrap to eight
lines (`"0 cancelled (included in total)"`). The KPI tiles start about 1,250px
down and *Needs attention* about 1,400px down. `_ProgrammeHealth` is rendered
above everything else (`Programme/Index.cshtml:52-53`). The Evidence Check, the
thing being sold, is a small link in the *Needs attention* header.

**D3. The product's verdict looks like a warning notice.** Readiness renders as
`ops-banner--warning`, the same component as "270.5 recorded hours have
unverified billability" and every other caveat. The most important sentence in
the product has the visual weight of a footnote. Project-level readiness sits
in a plain-text column further down, so the page says *Use with caveats* while
three of Northstar's eight projects underneath are *Not decision-ready*, and
never puts the two side by side.

**D4. Every finding is a fraction, rendered as prose.** "4 of 53 committed
items", "32 of 622 hours". A numerator over a denominator is exactly what a
proportion bar is for, and every finding has one. At present only a pastel
severity chip carries any visual weight, and it shows severity, not size.

**D5. The KPI tiles are inventory, not signal.** *Programmes 5 · Open Work
Items 67 · Blocked 3 · Overdue 14*: no denominator, no period, no change since
the last review, no link to the records. The same four tiles appear on the
Reporting Hub (`ReportingQueryService.cs:80` reuses `overview.KpiCards`). On the
Evidence Check, "Work items checked 103" and "Recorded hours checked 622.0" are
basis statements dressed as KPIs.

**D6. The type scale is too compressed to create hierarchy.** Text runs from
12px to 15px, h2 is 17px, the page title 24px and a KPI value 28px
(`app.css:67-70`, `:523`). Nothing is large enough to be *the* answer. KPI
values also use `tabular-nums` (`app.css:528`), which spaces standalone figures
loosely; tabular figures belong in aligned columns.

**D7. Prose is doing the interface's job.** The Reporting Hub has three
paragraphs of scope rules and a warning banner before its first figure. The
Contracts overview opens with a 70-word banner, and its contested-cost banner
can run to about 150 words. The Evidence Check opens with a 60-word scope
paragraph. The honesty is the product's differentiator and must stay. It needs
one consistent *basis* component instead: a one-line statement of what the
figures cover, footnote markers on the figures a caveat affects, and the full
text in a disclosure.

**D8. There are three different scope bars for one concept.** The Programme
Overview has programme and customer with "Apply scope". The Evidence Check has
programme, customer, from, to and extract date, with a secondary "Check this
scope". The Reporting Hub has from, to, programme and customer with "Update".
Scope is the P0-1 concept, and it doesn't carry between pages.

**D9. The navigation doesn't contain the product.** The rail holds up to 18
links in four groups: Programme Overview, Reporting, Delivery load, Skills
Coverage, Coverage Plan, Service Health and so on. The Evidence Check and
Recorded reviews, the loop [direction.md](direction.md) says to prove, have no
entry of their own; they light up "Programme Overview". A demo viewer sees
a portfolio suite, not the diagnostic they are being sold.

**D10. Colour isn't a system for data.** Status colours are raw hex in the chip
rules (`app.css:937-964`, amber `#d97706` at `:659` and `:860`) with no dark
overrides, so chips stay pastel on a dark tenant theme. There are no series,
sequential or neutral chart tokens, so the first chart anyone adds will invent
its own. The pack uses a third palette (`#171b1e`, `#1f7a3a`, `#a3261a`) and
ignores tenant branding.

**D11. The pack is the least designed surface in the product.**
`EvidencePack.cshtml` has its own inline stylesheet, a 900px column, 11.5px
uppercase headers, a ten-item bullet list before the register, and project
readiness at the bottom. It is what a steering committee or client reads. It
should be the most considered page, with a first page a director can read in a
minute.

**D12. Identifiers and grammar leak into records.** Evidence Check record
detail prints enum names and a fixed plural: "Due 2026-09-29, 1 days late,
stage InProgress" (`EvidenceCheck.cs:313`), which reaches the pack's appendix.
That breaks rule 8 ("Never show an identifier").

**D13. People tables are sorted by magnitude.** Contributor capacity is sorted
by residual hours (`ReportingQueryService.cs:389`), outstanding workload by
estimated hours (`ProgrammeOverviewQueryService.cs:184`) and planned allocation
by booked hours (`:236`). That is the ranked list of people that Delivery
load's rule 4 exists to prevent ([delivery-load.md](delivery-load.md)). It is
a table today. The day someone "adds a bar chart" to it, it becomes a
leaderboard. Sort these alphabetically; the over-threshold rows are already
flagged.

## The analyst's teardown

**B1. What a PMO buyer actually pays for.** Three jobs: walk into the steering
committee with figures they can defend; find money and dates leaking before
the client or the finance director does; and show who owns each problem and
whether last week's promise held. Preparation time is the weakest lever: at the
shipped defaults the product's own calculator gives a year-one position of
−£20,900 (GTM review). The pitch and the pack have to lead on **exposure** and
on **the loop**, and the data for both already exists.

**B2. The strongest numbers in the demo are buried.** From the Northstar run:

| What the data says | Where it appears today |
|---|---|
| 28 of 70 estimated items with time (40%) have already used more than their estimate, across a portfolio that includes a £240k fixed-price programme | The third delivery exception, severity *medium* |
| 3 of 8 projects are *Not decision-ready* while the portfolio headline says *Use with caveats* | A text column in the "By project" table, below the fold |
| 8 of 11 findings have no named owner; 6 have no decision | Pack tiles only, not the Evidence Check headline |
| 4,684 of the 6,897 hours by which workstreams exceed their estimates (68%, all time) sit in three workstreams that never had an estimate | Red-flagged rows in the Reporting Hub's effort-variance table, presented as overrun |
| 27, 32 and 56 of the period's 622 hours are unlinked, unattributed and of unknown billability | Three separate finding rows |

The fourth line matters most to an analyst. The Reporting Hub presents a
missing-estimate problem as an overrun, which is exactly the evidence-gap
versus delivery-exception distinction the product's thesis is built on.

**B3. Two numbers for "overdue" on adjacent pages.** The Programme Overview
says 14 overdue; the Evidence Check says 9 of 67 open items. The overview counts
any open item due before now (`ProgrammeOverviewQueryService.cs:80`). The check
excludes items whose status can't be read as a stage and counts whole days
(`EvidenceCheck.cs:308`). Northstar's "External accessibility audit" milestone
has an unmapped status: it is overdue on one page and absent on the next.
Both rules are defensible; neither page says which it uses. In a product sold
on "which figures can you defend", two unreconciled numbers for one word is the
fastest way to lose a PMO buyer in a demo.

**B4. The Trend page can't show a trend.** A snapshot stores all-time
cumulative effort and today's open and blocked counts under whatever period the
user picks (`ReportingSnapshotService.cs:22-37`); only utilisation is
period-scoped. A snapshot captured late records today's figures under an old
period. In Northstar all three rows show 8,559 actual hours, 67 open and 3
blocked. The page's lede says these are totals "as they stood at the close of
each reporting period". Recorded Evidence Reviews are frozen and scoped: they
are the real time series.

**B5. What not to add.** No RAG composite and no health score: readiness is a
band with stated thresholds, and a score would undo that. No earned value
(SPI/CPI): there is no cost or schedule baseline to support it, and the product
rightly says so. No forecasts from workflow counts. No per-person utilisation
or load charts. No money on any page reachable below Admin: the Evidence Check
must never read cost rates, and a test enforces that.

## What we agreed: the data points that trigger a decision

### Tier 1: trigger metrics

These lead the demo and the first page of the pack. All five exist in the data
today.

| # | Metric | Definition | Why it moves a buyer | Chart form |
|---|---|---|---|---|
| T1 | Readiness, portfolio **and** per project | Existing band and thresholds | "Can I present this?" answered once, with the projects that change the answer | Band strip (three labelled segments, current one marked) and a project band matrix |
| T2 | Hours without evidence | Unlinked, unattributed and unknown-billability hours, each over the period's hours | Hours you pay for that no report can explain. The finance director's question | Proportion bars, one per kind, never summed (they overlap) |
| T3 | Effort over estimate | Items over estimate over estimated items with time; per workstream, overrun split from never-estimated time | Margin being spent on fixed-price work, and whether "variance" is overrun or missing estimates | Proportion bar; per-workstream diverging bars, never-estimated in neutral grey |
| T4 | Slipped commitments without an owner | Open items past due, by days late (1–7, 8–30, 31+), owned or not | Dates leaking, and whether anyone is on them | Stacked bar by age |
| T5 | Did last week's decisions hold | Per finding: records cleared, still present, new; decisions held or broken | The repeat purchase. Proof the loop reduces exceptions | Movement bars (cleared left of zero, still present and new right) |

### Tier 2: credibility

These are shown on one line in the basis panel, each as *n of d*: extract age
in days against the seven-day rule, status mapping coverage, identity match,
estimate coverage, due-date coverage, and findings with an owner.

### Tier 3: operational

These stay in the app and out of the pitch and the pack's first page:
per-person workload, capacity and bookings (tables, alphabetical), RAID, change
requests, RACI, dependencies, invoices, skills and estimate calibration.

### Tier 4: demote or remove from buyer surfaces

The "Programmes" tile; raw open-item counts without a denominator; "items
checked" and "hours checked" as tiles (move them to the basis line); the
snapshot Trend page until B4 is fixed; and the customer rollup's open and
blocked counts, which repeat the programme table.

**Commercial, Admin only:** contract burn, meaning cumulative cost against value
or ceiling by month with the margin basis stated, never blended across currency
or basis. It needs a new bounded monthly read; `YearlyBreakdown` is per contract
year.

## Where the two lenses disagreed

| Designer wanted | Analyst's objection | Agreed |
|---|---|---|
| A readiness gauge | A dial is a score, and the product promises a band with stated thresholds | Three-segment band strip, current band marked, the rule that set it stated beside it. No needle, no percentage |
| Bar charts of per-person utilisation | A sorted person chart is a ranking (delivery load rule 4), and it's person-level processing | Team-level charts only; person tables stay tables, alphabetical, flagged rows kept |
| To delete the caveat prose | The caveats are what makes a report trustworthy, and that is the pitch | Caveats become a basis line, footnote markers on the affected figures and a disclosure. Nothing is removed |
| RAG colours on every row | RAG on small chips is colour-only noise, and red tends to spread | Status colour only on the band and on severity, always with a text label |

The analyst wanted money on "hours without evidence" in the pack. It stays in
hours: the pack is reachable below Admin. Whether a review may carry an
operator-entered blended rate is a product decision for the owner, not taken
here.

## The optimised display, surface by surface

1. **Evidence Check.** One shared scope bar, then a verdict block: the band
   strip, one sentence saying why, and "3 of 8 projects not decision-ready"
   with the project matrix. Then four tiles with denominators and change since
   the last review: findings without an owner, hours without evidence, overdue
   items, and items over estimate. Then the findings as proportion bars,
   grouped into gaps and exceptions, each opening to its records. Then the
   movement chart. The record form comes last. Items and hours checked, the
   period, the extract date and the thresholds move into the basis line.
2. **Board pack.** Page one is the decision question, the band, the project
   matrix, the T2–T4 figures, the findings chart, the movement chart and
   "decisions still needed" as a count with owners. Page two is the register;
   the appendix follows. Tenant branding tokens; one A4 portrait page for the
   summary.
3. **Recorded reviews.** A band per review as a timeline, findings per review
   stacked by gaps and exceptions, and findings with an owner. This is the
   evidence for the £2,000/month repeat review.
4. **Programme Overview.** *Needs attention* first; KPI tiles with
   denominators and links; an Evidence Check verdict card; a milestone
   timeline with a today line above the milestone table; overdue by age. The
   nine-column health table becomes one row per programme, with its reasons
   behind a disclosure.
5. **Reporting Hub.** Effort variance as diverging bars per workstream, with
   never-estimated time in neutral grey and labelled "no estimate, not
   overrun". Team capacity as logged, leave and residual per team, never per
   person.
6. **Contracts (Admin).** A burn chart per contract. The overview keeps its
   no-single-margin rule: small multiples by currency and basis.

Delivery load, Skills and Service Ops are left alone. Their rules constrain
them, and they are not buying surfaces.

## How to build it

- **Server-rendered from Razor partials, no chart library.** The `/staffops`
  CSP forbids inline script, the pack prints without script, and a
  server-drawn chart carries the same numbers as the table beside it. As
  built (V-1 and V-3), the bars are HTML and CSS rather than SVG: they keep
  text at reading size on a phone, and `print-color-adjust` keeps them in
  print. See *Charts* in [design-system.md](design-system.md).
- **No chart carries information alone.** Where its numbers are written
  beside it (every bar, as built) the mark is hidden from assistive
  technology; where they aren't (the milestone timeline) it has a
  `role="img"` summary and a table twin. Status colour always comes with its
  text label (design-system rule 11).
- **Tokens:** add `--ops-viz-*` (series, track, grid, axis, and status good,
  warning, serious and critical) to `app.css` with dark overrides, and check
  them with a palette validator before use. Move the chip hex into tokens at
  the same time (D10).
- **Tests:** render integration tests assert that each chart states the same
  *n of d* as its table and that the band strip marks `report.Readiness`;
  `./scripts/check-views.ps1`; the accessibility audit at three widths. The
  audit needs Node, which the review machine does not have.
- **Data:** V-1 and V-2 need no new reads; everything is in the Evidence Check
  report and the recorded review. Overdue ageing is computed in memory from
  open items. Contract burn by month needs a new bounded read under the
  Phase 2 rules in `CLAUDE.md`.

## Sequencing, and why this isn't a redesign

[direction.md](direction.md) says "not now" to visual redesign. This proposal
does not change the shell, the rail, the typeface, the brand or the layout
system. It splits into:

- **P0-6, truth:** one definition of overdue, or both reconciled on the page
  (B3); trend snapshots labelled as captured, with backdated capture refused or
  flagged (B4); portfolio readiness always shown with its project bands (D3);
  and D12's identifiers. These are truthful-output defects, found by looking
  at the rendered pages.
- **V-1, the display layer of P1-1:** chart partials and tokens; the Evidence
  Check verdict block, findings bars and project matrix; the pack's first page;
  Evidence Check and Recorded reviews in the navigation.
- **V-2:** the recorded-review history chart, once P0-5 has produced two real
  cycles to plot.
- **V-3:** Programme Overview reordered, KPI denominators, milestone timeline,
  overdue ageing, alphabetical person tables.
- **V-4, demand-led:** the Reporting Hub's variance split, contract burn, and
  one shared scope bar.

**Acceptance for V-1:** three people outside the build team, shown page one of
the Northstar pack for five seconds, can each say whether the report can be
used, what the biggest exposure is, and how many problems have no owner. All
views type-check, render tests pass, the audit is clean at three widths, and
page one fits one A4 page.

## The 30-second demo this enables

Open the Evidence Check. The band says *Use with caveats*, and beside it three
of eight projects are not decision-ready. Forty per cent of estimated items
with time are already over estimate, and one of those programmes is fixed
price. Eight of
eleven problems have no owner. Next week the same page shows which decisions
held. Every one of those statements is in the product today; none of them can
be read in 30 seconds yet.
