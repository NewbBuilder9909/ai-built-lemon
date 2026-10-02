# Usability review and the bench, 1 October 2026

**Basis for:** the "P0: a usable core" section of [direction.md](direction.md).
**Evidence:** the owner signed in and judged the app unusable. Their complaints
were then checked against the code and against screenshots of the Northstar
demo, signed in as the Team Lead.

## Verdict

Not usable as a product. Under it is sound engineering and one sellable idea:
the Evidence Check's question, "can this period's delivery report be
trusted?", with exceptions that carry an owner from week to week. Everything
around that idea competed with it.

## What was found

| Complaint | What the code showed |
|---|---|
| Too many menus, no search | Up to 19 sidebar links for an Admin, over 85 GET pages and about ten feature areas. No search anywhere. The Overview linked to the Reporting Hub's tabs under different names |
| Harder to use than the source tool | The same four tiles (open, blocked, overdue, overdue milestones) on the Overview and the Reporting Hub, and the overdue count again on the Evidence Check, which ran to about 2,400 words and showed each finding three times. A Forecast column that read "needs a closed item" on every row |
| Copy | Cards opened by saying what the number was *not* ("Done is workflow status, not acceptance, earned value…"). Internal rules written as interface text |
| User management | No password reset, no lock or disable, no delete except GDPR erasure (which left the login behind), and no way to change a role after creating someone. The confirm prompts used inline handlers that the site's own Content-Security-Policy blocks |
| Branding | The rail's text colours were hardcoded for a dark rail; only its background followed the brand. A light secondary colour made the menu unreadable |

## Decisions (owner, 1 October)

1. **Bench the periphery.** Everything outside the loop (scope, import,
   check, decide, pack) becomes a module that is **off for every tenant and
   every role, Admins included**. The only way on is the toolbox in
   Settings → Modules, which an Admin uses per organisation. There is no
   configuration key that switches a module on.
2. Follow every recommendation of the review: five-item navigation, the
   verdict and top findings as the home page, one "how this is worked out"
   panel per page, search, user administration, and the sidebar fix. Then
   test with delivery leads.

## The bench

| Module | What it holds |
|---|---|
| Reporting hub | Effort variance, capacity, RAID, governance, trend, alerts, the Jira and Tempo report, programme budgets |
| Delivery load | The load page, and the workload and booking tables on the Overview |
| Estimate calibration | Estimate against actual for finished work |
| Contracts and assurance | Contracts, invoices, margin, repository links, security assurance |
| Skills and continuity | Skills matrix, coverage plan, repository evidence |
| Service health | Support-desk demand by component |
| Staff self-service | My Work, My Profile, leave and approvals |
| Executive review | Multi-market packs (also needs the deployment setting) |

Customers moved out of the Reporting hub into Data, because a review can be
scoped to a customer.

## Still to prove

The decisive test has not been run: three delivery leads outside the build
team, each with their own export, timed on "could you present this on
Monday?". Until it is, "usable" is a claim about the code, not a finding.
