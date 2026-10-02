# Delivery load and concurrency

22 September 2026 · Built as a Gold view over existing Silver. The Jira/Tempo
and GitHub sources discussed here are **not** connected — see "What is
actually built" below.

## What this answers, and what it refuses to answer

The question is capacity, not quality: **is anyone carrying too many separate
pieces of work at once?** Context switching between projects has a real,
evidenced cost, and a delivery manager who can see it early can move a project,
add cover, or agree the load is temporary.

The question it refuses is the one it would be trivially easy to bend it into:
*who is doing the least?* This document exists because that bend is one sort
order away, and because the refusal has to live in code, not in a paragraph.

This view is a sibling of the skills module's key-person coverage
([`staff-skills-evidence-module.md`](staff-skills-evidence-module.md)): both
describe organisational exposure, neither judges a person. The same prohibition
applies here in full — no developer value, no quality measure, no rank, and
nothing that feeds an employment decision.

## Why a load lens is defensible where an output lens was not

An earlier framing of this work asked the data to identify low performers from
commit and ticket counts. That was refused: commit volume has no mechanism
behind it as a proxy for contribution, the skills module already forbids it,
and in the UK a capability process resting on a proxy metric is a tribunal
risk.

Recasting the same data as load changes three things:

1. **The person benefits from the finding.** A finding leads to relief, not
   sanction. That materially strengthens the proportionality and
   legitimate-interests analysis — it is monitoring work, not workers.
2. **It is not an employment decision**, so the automated-decision concerns
   under Article 22 recede.
3. **It has a mechanism.** Fragmentation degrading throughput is a real effect.
   "More commits means better" is not.

What does *not* change: this is still person-level processing of worker data.
Purpose statement, worker notice and a DPIA decision are still required before
a customer turns person-level collection on, exactly as the skills module
gates it. The improvement is in proportionality, not in the requirement.

## The four rules that stop this becoming a ranking

A load measure is trivially invertible. High concurrency means overloaded —
but any list sorted descending can be read ascending, and the bottom of a list
of people gets read as a judgement whatever the column header says. Intent does
not survive an export to a spreadsheet, so the inversion is blocked
structurally.

| Rule | What it means | What enforces it |
|---|---|---|
| **1. No row below baseline** | A person at or under their own normal is absent from the result, not present with a low number. There is no bottom of the list because the list does not contain everyone. | `DeliveryLoadFacts.Calculate` returns no finding under threshold; three tests in `DeliveryLoadFactsTests` pin below/at/just-under. |
| **2. Self-referential only** | Every comparison is a person against their own trailing median. Nothing compares two people. | No peer denominator exists in the contract; `DeliveryLoadShapeTests` fails the build on a rank/percentile/position member. |
| **3. No score** | `LoadSignal` is a three-value enum of surfaceable states. "Below normal" is not expressible. | `LoadSignal_cannot_express_that_someone_is_below_normal` asserts the exact member set, and a second test bans fractional measures — a `decimal` here would be a composite score in disguise. |
| **4. Ordered by key, never magnitude** | Findings come out ordered by staff key, and the view renders them alphabetically. | `Rule_4_findings_are_ordered_by_key_never_by_how_elevated_anyone_is`, using one barely-elevated and one far-elevated person so a magnitude sort would fail it. |

Two further refusals live in `StaffDeliveryLoadController`: **no export action**
and **no sort parameter**. An export becomes a spreadsheet that outlives its
caveats; a sort parameter is how an exception list becomes an ordered one with
a tail.

## The traps, and how each is handled

**Under-logging inverts the measure.** Time entries only exist where people log
time, and logging discipline fails first when someone is swamped. The most
overloaded person can therefore look lightest. Reading that silence as a
quieter week would get the whole thing backwards, so a person who logged
consistently and then stopped is reported as `CoverageFellAway` — a data
exception with **no context count at all** (`CurrentContexts` is null), never a
load reading.

**The week under assessment is the last complete one.** Timesheets fill up
during a week, so judging the week in progress reported every person as
`CoverageFellAway` each Monday morning (found 28 September against the
Northstar demo). `DeliveryLoadQueryService` passes the previous ISO week to
`DeliveryLoadFacts`, and the page says so.

**Role makes raw breadth meaningless.** A platform engineer is across many
projects because that is the job; a specialist is across one. Comparing raw
counts compares job descriptions. Rule 2 sidesteps this entirely: nobody is
ever compared to anybody else, so no role-relative banding is needed.

**A thin baseline invites guessing.** Below `MinimumBaselineWeeks` of a
person's own history, no finding is produced at any concurrency — an absent
baseline is unknown load, and unknown load is not low load. Those people are
counted in `PeopleNotAssessed` so the gap is visible.

**A findings list without a denominator reads as the whole team.** The page
puts coverage *before* findings: how many people are in scope, how many have
enough history, how many have no time data at all.

**A single bad week should not redefine someone's normal.** The baseline is a
median, not a mean, so one 20-project week among fives leaves the baseline at
five and a genuinely elevated week after it is still caught.

## What counts as a context

**A context is a Project, not a work item.** Moving between items inside one
project is cheap; moving between projects is the switch that costs. Counting
work items would make a focused person on a large project look badly
fragmented — pinned by
`A_context_is_a_project_so_many_items_in_one_project_are_not_fragmentation`.

**Only time entries carry the weekly series.** `TimeEntry` is the one Silver
row with a work date, so it is the only honest source of history. Entries with
no work date stay out of period reporting rather than being dated to now,
following `TimeEntry`'s own contract.

**Allocations describe the present and are never trended.** `WorkItemAllocation`
has no date. Projecting today's allocations backwards would fabricate a past,
so open allocated projects appear as standing concurrency beside a finding and
nowhere else.

## Thresholds

Agreed counts, not tuned parameters of a model. A percentage would be
fabricated precision over what is a small integer.

| Threshold | Default | Meaning |
|---|---|---|
| `BaselineWeeks` | 8 | How many of the person's own prior weeks form the baseline |
| `MinimumBaselineWeeks` | 4 | Below this, the person is not assessed at all |
| `ElevationContexts` | 2 | Extra projects over their own median that count as elevated |
| `SustainedWeeks` | 3 | Consecutive elevated weeks before the signal becomes `Sustained` |

These are a starting point for a design partner to change, not a calibrated
recommendation.

## Access

Two capabilities, split for the same reason estimate calibration splits
aggregate from per-estimator history: the second identifies individuals.

| Capability | Holders | Sees |
|---|---|---|
| `ViewTeamLoad` | Analyst, Board, Holiday Approver, Team Lead, Admin | Counts and coverage. No names. |
| `ViewPersonLoad` | Team Lead, Admin | Named findings. |

`ViewPersonLoad` is never implied by `ViewTeamCapacity`. Only roles who can
actually reassign work hold it, because a name is useful for relieving load and
not much else. The split is enforced at the service boundary, not in the view:
`BuildAsync(tenantId, includePeople: false)` **does not fetch names**, following
the rule in `CLAUDE.md` that a role-gated view must not retrieve the data and
hide it.

No cost or rate data is read on this path, and none is reachable from the view
model.

## What is actually built

Built and tested:

- `DeliveryLoadFacts` (pure arithmetic and the four rules),
  `DeliveryLoadQueryService` (Gold projection), `StaffDeliveryLoadController`,
  `Views/StaffOps/Programme/Load.cshtml`.
- 19 tests across `DeliveryLoadFactsTests`, `DeliveryLoadShapeTests` and
  `DeliveryLoadQueryServiceTests`, including tenant isolation and
  name-withholding.
- Capability, role-map and persona-contract entries.

**Not built.** Everything here reads Silver rows that today come from ClickUp
alone. Jira, Tempo, GitHub and the support desks are scoped but not connected
([`jira-tempo-integration-scope.md`](jira-tempo-integration-scope.md),
[`staff-skills-evidence-module.md`](staff-skills-evidence-module.md)). The view
is source-agnostic by construction, so those sources write the same
`TimeEntry` rows and this view works unchanged — but until they are connected,
the concurrency signal is only as complete as ClickUp time tracking is.

Also not built: a Platinum action layer. There is no equivalent of the skills
module's `CoverageActionType` for recording "rebalanced", with an owner and an
outcome. If that is added, it should keep the same discipline — only actions
that relieve, never actions that judge.

## What we may claim

> Programme Pulse shows where someone's concurrent project load has risen
> against their own recent history, so delivery managers can rebalance before
> it becomes a problem.

We may not claim that it measures performance, productivity, contribution or
quality; that it ranks anyone; that it identifies under-utilised people; or
that a low context count means anything at all — the product does not compute
one, and cannot display one.
