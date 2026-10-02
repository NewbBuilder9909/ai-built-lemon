# First-sale playbook

26 September 2026. This is the operating plan for getting the first three
paid diagnostics. It sits under
[`30-day-commercial-gate.md`](30-day-commercial-gate.md): that gate decides
whether to continue, and this playbook covers what to do in the meantime.
The offer is [`paid-diagnostic-offer.md`](paid-diagnostic-offer.md); the
message templates are in
[`outreach-and-proposal-template.md`](outreach-and-proposal-template.md).

## What changed that makes this sellable now

The offer promises a diagnostic built on **sanitised exports, with no
credentials**. Until today the product could not ingest an export: every way
in required a live API connector. `/staffops/programme/import` closes
that gap (see [`programme-ops.md`](../programme-ops.md), "File import"). A
prospect can now:

1. export work items and time from whatever tool they use (ClickUp, Jira,
   Asana, Monday, Harvest, Toggl, a spreadsheet);
2. rename a few headers to match the template, or often none, because
   common export spellings are accepted;
3. hand over two CSV files.

You then run the diagnostic in the product rather than in a spreadsheet.

## The offer, in one line

> A fixed-fee review of one recurring delivery report: which conclusions in
> it have current evidence behind them, which don't, and who owns fixing
> each gap. From your exports, with no system access, in ten working days.

## Price (hypothesis to test, not a published price)

| Customer | Fee | Why |
|---|---|---|
| First three ("founding") | **£3,500 fixed** | Low enough for a delivery director to approve without procurement at a 50–500 person firm; high enough that "yes" means something. |
| After three measured deliveries | **£6,000–£8,000** | Reprice from measured effort: fee ≥ 3× your delivery hours × your day-rate floor. |
| Repeat ("monthly evidence review") | **£2,000 / month** listed on `/purchase`; negotiate within £1,500–£2,500 | This is the conversion to recurring revenue: the same import, re-run on each cycle's export. |

Invoice 50% on signature and 50% at playback. A free diagnostic is not a
lower price; it is a different, weaker test (the gate says that complimentary
feedback proves nothing). If someone won't pay £3,500 to fix a report they
spend hours on every week, the pain isn't big enough, and it's better to
learn that in week 2 than in month 6.

## Who to target

A **named person who personally assembles or signs off** a weekly or monthly
delivery report built from two or more systems, at:

- UK digital, creative and technology agencies (20–300 staff);
- management and IT consultancies (50–500 staff);
- software services / systems integrators running client projects.

Titles: Head of PMO, PMO Lead, Delivery Director, Head of Delivery, Operations
Director, COO at firms under ~150 staff.

Where to find them:

- **LinkedIn search:** `("Head of PMO" OR "Delivery Director" OR "Head of Delivery") AND (agency OR consultancy)`, filtered to UK, 51–500 employees. Engage with posts about reporting, resourcing or utilisation before messaging.
- **Communities:** APM (Association for Project Management) branch events and the PMO SIG; PMI UK chapter events; the PMO Flashmob community; Agency-focused communities such as BIMA and the Digital Agency Network.
- **Your own network:** previous colleagues and clients who've complained about "the Monday report". These convert fastest; start here.

**Keep named prospects out of git.** This repository is public. Copy
`prospect-scorecard.csv` to `prospect-scorecard.local.csv` (git-ignored by
`*.local.csv`) and record real people and firms only there. The tracked file
stays the empty template.

## Weekly cadence (four weeks)

| Activity | Weekly target | Counts as done when |
|---|---|---|
| New named prospects added to `prospect-scorecard.local.csv` | 15 | A real person, firm and report they own |
| Personalised messages or introduction requests sent | 15 | Sent, using the templates |
| Discovery conversations held | 3 | You saw their actual report (sanitised) |
| Proposals issued | 1 (from week 2) | Fixed fee, date, scope, sent to the budget holder |

Four weeks at this rate gives roughly 60 contacts → ~10 conversations →
~3–4 proposals → 1–2 paid diagnostics, *if* the problem is real. Those
conversion rates are assumptions; replace them with your own after week 2.

## Before a prospect has shared anything

Run the demo on the sample data: download both sample files from
`/staffops/programme/import`, import them into a demonstration tenant, open
the Evidence Check, record it, assign an owner to one finding and open the
board pack. It takes about ten minutes, and the prospect sees their own
problem (unmapped statuses, unowned work, time booked to nothing) in someone
else's data. The first check on any real data will read *not decision-ready*
until people are linked on the identity queue. Say so before the playback, so
it reads as a finding, not a fault.

## Delivering a diagnostic with the product

1. **Tenant:** create one per customer at `/staffops/platform/tenants`
   (Platform Admin). Isolation is enforced on Programme Ops, so customers
   never share data.
2. **Data agreement:** before receiving files, agree in the proposal which
   fields, the transfer method, where the data is stored (your laptop running
   the app locally is simplest and cheapest to secure), and a deletion date.
   Ask for emails to be kept only if they want people matched to named
   staff; otherwise names alone are fine.
3. **Import work items**, then **time entries**, at
   `/staffops/programme/import`. Download the templates there if their
   export needs reshaping. Every rejected row is listed with the reason;
   fix and re-upload. Always upload the **whole period**: each upload
   replaces the last, and anything it would remove is shown for you to
   confirm first. That is how a correction or deletion at source reaches the
   figures, and why a filtered or cut-short export must not be confirmed.
4. **Read the import result as findings, not noise.** Items with an
   *Unmapped* status, time with *unknown billability*, time not linked to a
   work item, and people on the identity queue are the first entries in the
   exception register: they're the gaps their weekly report currently
   papers over.
5. **Run the Evidence Check** (`/staffops/programme/evidence-check`) and
   download its exception register. That's the draft deliverable: readiness
   band, every gap and exception with its numerator, denominator and source
   record ids. Then **review** the Programme Overview, Reporting Hub and delivery load views
   with those gaps in mind, and fill in
   [`evidence-pack-template.md`](evidence-pack-template.md) finding by
   finding.
6. **Record the check** before the playback ("Record this check for the
   review" on the Evidence Check page). That freezes it, so the pack still
   says the same thing after you or the customer change the data.
7. **Playback** (60 minutes, budget holder present): top five exceptions,
   who owns each, and a decision on stop, repeat monthly, or connected pilot.
   Record each owner and decision on the review page as you go, then send the
   printable board pack (`Board pack` on the review page) as the written
   deliverable. Ask for the repeat engagement in that meeting: the repeat is
   next cycle's export, re-imported and recorded, and the next pack opens with
   what moved and which decisions didn't hold.
8. **Delete** the customer's data on the agreed date and confirm it in
   writing.
9. **Measure:** record your hours by stage in the scorecard. That number sets
   the next price.

## What not to do in these four weeks

- Build anything that isn't blocking a signed or proposed diagnostic.
- Lead with the platform's breadth (skills evidence, branding, contracts).
  Open with their report and its gaps.
- Claim savings, ROI or customer results you don't have (see
  [`claim-register.md`](claim-register.md)).
- Stand up public hosting for customer data. Local processing on sanitised
  files avoids most of the open deployment gates in
  [`../security-assurance.md`](../security-assurance.md) until a customer
  pays for a connected pilot.
