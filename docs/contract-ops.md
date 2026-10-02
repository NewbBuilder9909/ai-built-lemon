# Contract Ops: commercial control

A new feature area — own migration plan, own composer, own audit log, same
convention as StaffOps/ProgrammeOps/BrandingOps. Admin-only throughout:
commercial terms and contract documents are more sensitive than the cost/rate
data StaffOps already gates the same way, so nothing here is reachable by
Team Lead, unlike the Reporting Hub.

## What it's for

Two things: a repository for contract documents (MSAs, SOWs, amendments,
POs), and a burn-down of contract value vs cost incurred — for a contract
like £350,000/year over 5 years, how much has been booked against it and
whether it's profitable.

## Data model

- **`Contract`** (`Models/ContractOps/Contract.cs`) — scopes to one
  `Customer` (the existing admin-authored entity from Reporting). Commercial
  terms (`CommercialModel`, value, dates, currency) are set once at creation
  and never silently edited afterward — only `Status`/`Notes` change. A real
  term change is recorded as an `Amendment` document, not a data mutation,
  so the burn-down stays historically honest.
- **`ContractDocument`** — metadata only. The file itself lives outside
  `wwwroot`, under `{ContentRootPath}/App_Data/contracts/{contractKey}/`, so
  there is no static-file route to it at all — the only way to read a byte
  of it is `StaffContractController.DownloadDocument`, which re-checks
  `IsAdminAsync()` on every request. This is the opposite of
  `BrandingAssetStorageService`'s pattern (branding assets are stored under
  `wwwroot` because logos/favicons are meant to be public).
  A download never opens `StoragePath` as given: the location is rebuilt
  from the document's own contract key, document key and extension, under
  this deployment's root or configured Azure share/container, and a stored
  path naming anything else is a 404 (`ContractDocumentUnavailableException`).

## Cost attribution: selection, not allocation

A contract's cost is **selected** — by customer and by the contract's own
term dates — never **allocated**. There is no contract-to-work-item link
anywhere in the schema. Two consequences follow, and both are now enforced
in code rather than left as prose:

1. **Overlapping contracts share cost.** A customer holding two contracts
   whose terms overlap in time has the same logged hours selected by both.
   Summing their margins double-counts. `ContractAttribution.FindOverlapping`
   detects this; the position is marked
   `Attribution.Contested`, `Margin`/`MarginPercent` are suppressed
   (`MarginBasis.NotEstablished`), and `Attribution.SharedCost` reports how
   much of the cost falls inside an overlapping term. Cost itself is still
   shown — those hours were genuinely incurred for that customer; what is
   not established is that they belong to *this* contract alone.
   `PortfolioMarginService` holds contested contracts out of every total and
   counts them in `AttributionContestedContractCount`, with the held-out
   cost per currency in `ContestedCost` — deduplicated by
   `ContestedCostAggregator`, since summing those contracts' own cost
   figures would repeat the very double-count that number measures. A
   portfolio margin that admits a gap beats one that double-counts.

   Resolving it for real means either non-overlapping terms or an explicit
   allocation table. Until one of those exists, no contested contract should
   reach a finance conversation as a profit figure.

2. **An undated entry belongs to no term.** A `TimeEntry` with neither
   `StartedAtUtc` nor `WorkDate` can't be placed in any contract period, so
   it is excluded from cost and reported as `UndatedHours` rather than
   drifting into every contract for that customer. Entries dated only by
   `WorkDate` are term-filtered on that date — the same date
   `TimeEntryCostCalculator` prices them on, so the filter and the costing
   can never disagree about when an entry happened.

### What "Margin" means (`MarginBasis`)

The three commercial models produce three different numbers and only one is
near a realised margin, so the basis travels with the figure instead of
being implied by a column header:

| Model | `MarginBasis` | What the number is |
|---|---|---|
| FixedPrice | `CostToDateHeadroom` | Contract value less cost **to date**. Headroom remaining, not profit — the cost of completing the remaining scope is unknown here. |
| Ongoing | `ContractedValueLessCostToDate` | Cumulative contracted value less cost to date. Contracted value is not recognised revenue and not cash. |
| TimeAndMaterials | `ChargeableValueLessCostToDate` | Billable hours × bill rate, less cost to date. Chargeable value is not invoiced and not cash. |
| any, contested/unset | `NotEstablished` | Suppressed. |

`MarginBasisText.Describe` renders the sentence; the contract detail and
index views both show it next to the figure.

## The burn-down (`ContractCommercialService`)

Walks Customer → Programme → Project → Workstream → WorkItem → TimeEntry,
the same chain `ReportingQueryService`'s Customer rollup already walks, then
reuses `TimeEntryCostCalculator` (extracted from
`ReportingQueryService.BuildCostSummaryAsync` for exactly this reuse) for
the cost side. Splits by `CommercialModel`:

- **Ongoing** — one row per contract-year, `AnnualValue` vs cost for that
  year, with running cumulative totals. This is the "£350k/year × 5 years"
  view.
- **FixedPrice** — one overall figure, `TotalContractValue` vs cumulative
  cost to date. No artificial per-year split — a fixed-price engagement
  isn't necessarily an annually recurring amount.
- **TimeAndMaterials** — cumulative billable hours (`TimeEntry.IsBillable`)
  × an optional blended `BillRate`. If `BillRate` is unset, revenue is
  reported as "not set", never assumed zero.

A cost entry whose effective `StaffRate.RateCurrency` doesn't match the
contract's currency is excluded from the total and counted separately as
`MismatchedCurrencyHours`, rather than being silently summed across
currencies.

## Portfolio-wide position (`PortfolioMarginService`)

`GET /staffops/contracts/overview` (`StaffContractController.Overview`) is a
cross-contract rollup on top of `ContractCommercialService.BuildSummaryAsync`
— it doesn't re-walk Customer → Programme → ... → TimeEntry itself, only
aggregates the per-contract rows that call already produces.

**There is deliberately no single portfolio margin.** Every total is grouped
by currency *and* by `MarginBasis`, because the three commercial models do
not produce the same measure (see the table above): adding fixed-price
headroom to a chargeable-less-cost figure produces a number that reads as
portfolio profit and isn't one. So `TotalsByCurrency` is a list keyed on
`(Currency, MarginBasis)`, `ByCustomer` on `(Customer, Currency,
MarginBasis)` and `ByCommercialModel` on `(CommercialModel, Currency,
MarginBasis)`; the page prints the basis next to every figure and glosses
each one below the table. The value column is called **Value**, not Revenue
— none of the three is recognised revenue and none of it is cash.

A contract whose revenue is unset (Time & Materials with no `BillRate`) is
excluded from every total and counted in `MismatchedRevenueContractCount`
instead of being assumed zero — same honesty pattern as everywhere else in
this file. A contract whose attribution is contested is held out on the same
principle and counted separately (see "Cost attribution" above); the two
reasons stay distinct so the page can say which one applies.

### The held-out cost is deduplicated (`ContestedCostAggregator`)

The size of the held-out gap is itself a figure a reviewer will quote, so it
has to be right. Summing the contested contracts' own `CumulativeCost`
repeats the double-count it exists to measure: two overlapping contracts
each report the same shared hours as their own cost, so a naive sum reports
them twice.

`ContestedCostAggregator` (pure and static, same shape as
`ContractAttribution`) therefore takes the contested contracts' **time
entries**, not their totals, unions them by `TimeEntry.TimeEntryKey` per
currency, and re-costs the union once through `TimeEntryCostCalculator`.
Non-labour costs are summed plainly — a `ContractOps_NonLabourCost` row is
attached to one contract by key, so it is *allocated*, not selected, and two
contracts can never share one.

That is only computable where the entries are still in hand, which is why
`BuildSummaryAsync` returns a `ContractSummarySetViewModel` (rows **plus**
`ContestedCost`) rather than a bare row list — one walk of the book, both
answers.

The row then carries **two figures that are easy to conflate and must not
be**:

| Field | What it is | Three contracts sharing £100 |
|---|---|---|
| `DisputedCost` | The hours more than one held-out contract claims, costed **once**. The exposure actually in dispute. | £100 |
| `DuplicationIfSummed` | How much adding the held-out contracts' own cost figures would have overstated `TotalCost` by. Evidence that the deduplication did something — not money. | £200 |

They coincide when exactly two contracts overlap, which is why the
distinction is invisible in the simple case and why
`ContestedCostAggregator_separates_disputed_cost_from_the_overstatement_a_naive_sum_would_produce`
pins the three-contract case. The page states each in its own sentence and
never labels the overstatement as disputed cost; doing so would overstate
the exposure by exactly the amount the deduplication removed.

What this page reports is therefore a rollup of the contracts whose cost is
attributable to them alone — **not the whole book**. The banner says so, and
names the size of the held-out cost.

Two things this still doesn't do, and shouldn't be demonstrated as if it
did: it does not *allocate* contested cost (nothing can, without an explicit
contract-to-work-item link), and a held-out contract contributes nothing to
any total, so the portfolio view understates the book by design.

Trend-over-time for this rollup (a `ContractOps_MarginSnapshot` table,
mirroring `ReportingSnapshot`'s on-demand-capture pattern) is a natural next
step once the dashboard itself is proven — not built yet.

## Routes

`StaffContractController`, `[Route("staffops/contracts")]`, every action
`IsAdminAsync()`-gated:

| Route | Purpose |
|---|---|
| `GET ""` | Commercial position — every contract with value/cost/margin at a glance, plus the create form |
| `POST "create"` | Creates a contract (`Status = Draft`) |
| `GET "{contractKey}"` | Terms, the burn-down, document list/upload |
| `POST "{contractKey}/status"` | Status/Notes update only |
| `POST "{contractKey}/documents"` | Upload (PDF/DOCX/XLSX/DOC/XLS, 25MB limit, extension + magic-byte signature checked) |
| `GET "{contractKey}/documents/{documentKey}"` | Streamed download, `Content-Disposition: attachment` |

Every create/status-change/upload/download writes to `ContractOps_AuditLog`
— download included, since "who accessed this contract document" is itself
part of commercial control.

## Non-labour costs and invoicing

Two additions on top of the original burn-down, both folded into
`ContractCommercialService`'s cost/margin figures the same way labour cost
already was:

- **`NonLabourCost`** (`ContractOps_NonLabourCost`) — a minimal expense/PO/
  vendor-invoice entry (description, amount, currency, date incurred)
  against a contract, added from the contract Detail page. Matching-currency
  entries are summed into `CommercialPositionViewModel.NonLabourCostTotal`
  and (for Ongoing contracts) bucketed into the correct contract-year row by
  `IncurredOn`; a different-currency entry is excluded and reported
  separately via `MismatchedCurrencyNonLabourCost` — the same
  exclude-and-surface pattern `TimeEntryCostCalculator` already applies to
  labour cost, just re-implemented for this narrower case
  (`ContractCommercialService.SplitNonLabourCosts`) rather than forced
  through the calculator, which is keyed on `TimeEntry` specifically.
- **Invoicing** (`Invoice`/`InvoiceLine`, `ContractOps_Invoice`/
  `ContractOps_InvoiceLine`, `IInvoiceGenerationService`) turns a period's
  revenue into an actual document: `POST /staffops/contracts/{contractKey}/invoices`
  generates a **draft** with one line for Time & Materials billable hours in
  the period (hours × `BillRate`) plus one line per non-labour cost incurred
  in the period. Throws `InvoiceGenerationException` (caught by the
  controller, shown as a banner) if a period has nothing to invoice — never
  creates an empty invoice.
- **Draft → review → issue** (decision 4 of
  [delivery-evidence-and-contract-assurance.md](delivery-evidence-and-contract-assurance.md),
  ContractOps step 06, `AddInvoiceDraftStage`):
  - *Date rule:* an hour belongs to the period of its `TimeEntry.ReportDate`
    (the provider's work date, else the UTC start date), the same rule period
    reports and contract costing use. Before this, invoices used the UTC start
    date only, so Tempo time (work date only) was never invoiced.
  - *Billability:* only hours with known billability that are billable are
    billed. Hours with unknown billability are recorded on the draft as
    `needsReviewHours`, never billed and never dropped; issuing a draft that
    has them requires the reviewer to confirm, and that confirmation is
    audited. Billing no longer depends on cost rates: previously a person with
    no `StaffRate` on file was never invoiced.
  - *Numbering:* a draft has no number (a `DRAFT-…` placeholder that is never
    shown as an invoice number). `IssueInvoiceAsync` assigns
    `{Reference}-NNN`, counting only issued and paid invoices, under
    `UPDLOCK, HOLDLOCK`, so a discarded draft leaves no gap and two concurrent
    issues can't share a number (`InvoiceLifecycleIntegrationTests`). The
    issue date (`issuedAtUtc`) is the invoice date; existing invoices were
    backfilled from `createdAtUtc`.
  - *Transitions* (`InvoiceLifecycle`): Draft → Issued, Draft → Discarded,
    Issued → Paid. Nothing moves back, and a draft can't be marked paid. The
    status route is unchanged; the service refuses any other move with a
    message. `GET /staffops/contracts/{contractKey}/invoices/{invoiceKey}` is
  a standalone printable document (`Layout = null`, its own inline
  stylesheet, a "Print / Save as PDF" button) — no PDF library was added;
  the browser's print-to-PDF is the actual PDF path, consistent with this
  codebase's preference for not adding a dependency for one narrow job.
  Fixed Price and Ongoing contracts can still invoice non-labour costs the
  same way; they just never get the billable-hours line since their value
  isn't hours-based.
- The Customer → Programme → Project → Workstream → WorkItem → TimeEntry
  walk in `InvoiceGenerationService` duplicates
  `ContractCommercialService`'s, deliberately: one is bound to the *contract
  term*, the other to the narrower *invoice period*, so they weren't sharing
  cleanly yet — see `InvoiceGenerationService`'s doc comment.

## Document validation

No safe re-encode step exists for PDFs/Office documents the way ImageSharp
gives Branding for images, so validation stops at extension allowlist, a
25MB size limit, and a magic-byte signature check (`%PDF-` for PDF,
`PK\x03\x04` for the Open XML formats, the OLE header for legacy
`.doc`/`.xls`). Stated as the boundary, not a full content scan — the ZIP-
based Open XML formats can't be distinguished from each other by signature
alone (a renamed `.xlsx` would pass a `.docx` check), which is an accepted
simplification, not a gap being hidden.
