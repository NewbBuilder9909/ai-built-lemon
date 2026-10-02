# Competitive position: winning beside the giants, not against them

26 September 2026. This builds on
[`exco-market-pivot-2026-09-22.md`](exco-market-pivot-2026-09-22.md) (the
competitor table) and
[`marketability-gap-analysis-roadmap-2026-09-25.md`](marketability-gap-analysis-roadmap-2026-09-25.md).
It's a strategy document, not customer evidence, and nothing here is
validated until paid diagnostics say so.

## The one-sentence position

> **ProgrammePulse tells a leadership team whether this week's delivery
> report can be trusted, and what needs an owner, across whatever tools
> the work actually lives in.**

## Why the giants can't take this position

ClickUp, Jira, Monday, Asana, Workday, Planview, Kantata and Productive
compete to be *the system where work happens*. That gives each of them the
same structural blind spot:

1. **They can't be neutral.** Each wants to be the single source of truth.
   None will tell an executive that the numbers coming out of it are wrong,
   and none can see the other tools a services firm actually runs.
2. **Their incentive is migration.** Their answer to fragmented reporting
   is "move everything into us", which is a costly, political, year-long
   programme. Our answer works on Monday, from exports, with no migration.
3. **Their dashboards hide uncertainty.** A clean chart is their product.
   Showing "this 92% complete figure excludes 14 items whose status can't
   be read" works against their own interest. For us it *is* the product.
4. **BI tools (Power BI plus an analyst) are the real incumbent.** They can
   join data, but every definition, exception rule and reconciliation is
   bespoke and rebuilt at every firm. The Evidence Check is those rules,
   productised.

## What makes it defensible over time

Code is easy to copy. These build up with each customer and are hard to
copy:

- **The rulebook.** Exception rules with stated denominators and thresholds,
  refined by what reviewers accept or reject in real diagnostics.
- **Mappings.** Every status vocabulary, header alias and identity match
  learned from real exports makes the next onboarding cheaper.
- **Trust.** Findings trace to source records, readiness bands can't be
  gamed, and customer data stays portable. Leaders renew what they have
  stopped double-checking.

## What exists now

| Capability | Status |
|---|---|
| Credential-free intake: work items and time from any tool's CSV export | Built (`/staffops/programme/import`) |
| Live connectors: ClickUp, Hub Planner, Jira, Tempo | Built; pilot maturity (see claim register) |
| Automated exception register with readiness band and CSV evidence pack | **Built today** (`/staffops/programme/evidence-check`) |
| Tenant isolation, audit and security posture that pass procurement review | Built; deployment gates open (`docs/security-assurance.md`) |

## What to build next, and only when a paying customer pulls it

In order. Each step converts a one-off diagnostic into recurring revenue:

1. **Owner and decision capture on each finding.** The review happens in
   the product, not in a spreadsheet copy, and next week's check shows
   which exceptions were actually closed. This is the step from report to
   workflow.
2. **Week-over-week trend.** Snapshot each check, so leadership can see
   whether evidence quality and delivery exceptions are improving. This is
   the monthly subscription's reason to exist.
3. **A pre-review digest.** The morning of the weekly review, the owner
   gets the readiness band and top five exceptions.
4. **More intake shapes.** Add export formats only as real prospects bring
   them (Monday, Asana, Harvest, Smartsheet), each proven on a real file
   before it's claimed.

Don't build before a customer pays: more connectors, AI summaries, resource
optimisation, or a general PPM feature set. Those are the giants' ground,
and breadth there dilutes the one message that wins.

## The sales line, by buyer

- **COO / delivery director:** "Before your next portfolio review, find
  out which figures you can defend, and which commitments need an owner,
  without changing any tool."
- **PMO lead:** "Stop reconciling by hand. Upload the exports and get the
  exception register in minutes."
- **CFO:** "See how much recorded effort isn't attached to any
  deliverable, and which fixed-price work has already used its estimate."
- **CTO / security:** "No credentials needed to start. Tenant-isolated,
  audited, and your data stays portable."
