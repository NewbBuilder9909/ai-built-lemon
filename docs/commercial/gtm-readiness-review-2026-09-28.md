# GTM readiness review — 28 September 2026

**Verdict: credible diagnostic workbench; incomplete commercial service; no-go for an open, recurring SaaS launch.** The recent changes improve the product materially. They do not yet establish a dependable route from interest to purchase, first accepted result and a trustworthy second review.

The concern is more specific than “the proposition is unclear.” A reasonably clear proposition now exists in the offer: help a delivery lead identify which figures and commitments in a weekly review have defensible evidence. The application still exposes a much broader operations suite, its purchase-to-setup handoffs depend on the operator, and several core data/comparison rules are weaker than the recurring-review promise.

## Scope and evidence boundary

Reviewed application commit **`947c62ab1ede9b63511a428af81efd9f2cbccab8`**, the current `main` after PRs #21, #25, #26 and #28 merged on 28 September. The first examination used `5e4b075`; conclusions below were rechecked against the later candidate. GitHub PR state was checked directly using its authenticated API on 28 September, after 16:40 UTC. Application code was not changed, and no deployment, PR merge or customer communication was performed.

Evidence consists of source, configuration, tests, current PR diffs/status, commercial documents and the separate `NewbBuilder9909/Assayer` website repository. Source-derived scenarios below are labelled as such; they are not reported as observed customer incidents. Local automated verification is recorded at the end. This is not a completed hosted-browser acceptance test, security assessment or customer validation exercise.

The tracked prospect scorecard is deliberately a template; the playbook instructs real prospects to be recorded outside git. **An empty tracked scorecard does not establish that there are no prospects or sales.** No measured customer outcome, paid conversion or repeat-purchase evidence was established in this review. Such evidence may exist elsewhere and should be supplied to the launch decision.

## What is ready, and for which launch

| Commercial motion | Decision | Reason |
|---|---|---|
| Demonstrate the product using synthetic data | **Ready for an operator-led rehearsal/demo** | Imports, findings, recorded decisions and printable packs exist; Northstar adds realistic scenarios. A usable public demo deployment was not verified. |
| Offer a bounded, manually delivered export diagnostic | **Conditional go** | A real workbench and a clear fixed-scope offer exist. Require a working enquiry/purchase handoff, agreed data handling and a reconciled, explicitly scoped deliverable. Human review must account for the limitations below. |
| Sell a repeat assisted review | **Not yet demonstrated** | The second-cycle data and comparison semantics need acceptance testing and correction or explicit restrictions. Delivery effort, customer value and repeat willingness remain unproved here. |
| Invite customers into a hosted application with real data | **No-go until deployment and customer-journey gates pass** | Source controls and tests are substantial; operational restore, support, deployed access/recovery and processing evidence are separate requirements. |
| Open self-service SaaS | **No-go** | Signup is deliberately disabled; verified invitations, recovery, provisioning lifecycle and billing/entitlement reconciliation remain incomplete. Turning on the existing flag would not close these gaps. |

“Conditional go” is permission to qualify and prepare a tightly bounded engagement, not a claim that customer-data or hosting gates have passed.

## What a buyer and user actually experience

| Stage | Current experience | Gap and commercial effect |
|---|---|---|
| Discover | Product landing page describes weekly delivery reporting; `/purchase` sells an assisted diagnostic. The separate Assayer site has a staged journey design. | The application and marketing site are separate releases. There is no verified deployed front door in this assessment. A brand decision record is not a launched website. |
| Understand the offer | £3,500 founding diagnostic, ten working days, half on signature and half at playback; £2,000/month repeat if justified. Synthetic example and limitations are explicit. | Much clearer than the earlier three-offer page. The home page still emphasises reporting/capacity while the offer emphasises evidence exceptions. Scope, buyer and outcome should be identical across both. |
| Enquire | `/purchase` builds a browser-only brief and can open a configured `mailto:` link. Shipped `Commercial:EnquiryEmail` is blank. | With defaults, the page explicitly says nothing reaches the seller. An environment override may fix the address; delivery, acknowledgement and follow-up still need proving. No lead is saved by the application. |
| Buy | Proposal, signature and invoice occur outside the application. | This is acceptable for the assisted service. The missing evidence is a completed, owned handoff from enquiry to agreed scope/payment and start date—not a need to build Stripe immediately. |
| Get access | Platform Admin creates the tenant and its first Admin. The operator supplies an initial password; no invitation email is sent. | Human scheduling and secure handover are required. Onboarding stages, acceptance and support ownership are kept off-platform. No guided customer activation journey is delivered by this screen. |
| First login | Admin completes MFA and is routed to the Programme Overview. Other roles get capability-appropriate landing pages. | This is a real improvement. It still lands a new diagnostic customer in a portfolio application, rather than in a use-case-specific setup sequence. Login advertises “Start a trial” even though that section is disabled. |
| Prepare first data | Admin creates staff as needed, reshapes exports to the two CSV schemas, imports work items then time. Invalid rows and ignored columns are reported. | There is no customer-configurable status mapping. Unknown vocabulary requires source/CSV changes or a stated caveat. This is delivery effort that must be costed. |
| Match people | Unmatched identities and email suggestions require review. After approval, the operator re-imports to populate attribution. | Security has improved: an email match alone no longer attributes work. The product must make the approval-and-replay sequence easy; weakening the check would be the wrong response to friction. |
| Reach first value | Run Evidence Check, inspect records and caveats, record the review, assign owners/decisions and print/export the pack. | This is the strongest implemented path. A successfully generated report is not automatically an accepted customer result. There is no measured time-to-first-accepted-result in the inspected evidence. |
| Return next cycle | Export again, re-import, record again and compare findings. | Data scope, corrected/deleted rows and category-level carry-forward can undermine the claimed week-to-week account of what changed. See the core findings below. |
| Recover, get help and leave | MFA recovery mechanisms exist; operator involvement remains necessary. Delivery-data purge exists for suspended/archived tenants, with counts and confirmation. | No end-to-end hosted account/support recovery was established. Whole-customer exit also includes people, other enabled modules, downloaded files and backups; the narrow purge is not proof of all of these. |

For a founder-delivered diagnostic, the customer may never need a login: the operator imports and reviews, and the customer receives the playback and pack. That is a coherent service. If customer access is sold, all the access/setup steps become part of the paid experience and must be rehearsed accordingly.

Evidence: [purchase page](../../Views/Purchase/Index.cshtml), [configuration](../../appsettings.json), [login](../../Views/StaffOps/Account/Login.cshtml), [home](../../Views/NoContent.cshtml), [assisted setup screen](../../Views/StaffOps/Platform/Onboarding.cshtml), [landing policy](../../Services/Staff/StaffLandingPage.cs), [onboarding plan](../saas-onboarding.md), [import screen](../../Views/StaffOps/Programme/Import.cshtml), [identity resolver](../../Services/ProgrammeOps/StaffIdentityResolver.cs).

## Core findings that affect the value being sold

### 1. “Weekly” is a review cadence, not an enforced data scope

`EvidenceCheckService.BuildAsync` loads all work items and time entries for the tenant. It has no reporting-period or programme parameter. Recorded reviews preserve an as-of timestamp, but do not establish a selected reporting window or source-extract manifest.

**Consequence:** the buyer may think they are assessing one programme or this week's report while the check incorporates the tenant's accumulated imports. Historical estimate-versus-actual is sometimes appropriate, but it must be distinguished from period-specific hours and a particular weekly pack. A timestamp on the check does not establish the age of the source extract.

**Required outcome:** explicitly declare the customer decision, programme/project selection, period, source extracts and calculation basis. Preserve that scope with the review and compare like with like. Until then, deliver only a manually reconciled, agreed dataset and explain the whole-tenant basis.

Evidence: [service](../../Services/ProgrammeOps/EvidenceCheckService.cs), [recorded review model](../../Services/ProgrammeOps/EvidenceReview.cs).

### 2. Re-import safety is narrower than the repeat-service promise

Time `EntryId` is optional. Without it, the importer derives an identity from fields including hours. Therefore, **changing a row from 3 hours to 4 creates another entry, leaving 7 hours in aggregate**, if no stable ID was supplied. This is a source-derived example, not a customer incident. The schema honestly documents it, but a successful import does not prevent it. Import is upsert-based; omission from a later CSV does not itself remove the older row.

**Consequence:** identical-file replay is safe, but corrected or replacement exports are not equivalent to replay. A tool being sold to expose unreliable totals can retain stale or duplicated effort through an ordinary correction workflow.

**Required outcome:** define incremental versus replacement import, require stable IDs where correction is promised, provide a controlled reconciliation/removal path and show before/after counts and hours. Accept a two-cycle test containing changed hours, a deleted row, identical entries and replay—not just the same file twice.

Evidence: [time schema](../../Services/Integrations/FileImport/DeliveryExportSchema.cs), [importer](../../Services/Integrations/FileImport/DeliveryExportImportService.cs), [upsert repository](../../Services/ProgrammeOps/ProgrammeRepository.cs).

### 3. “What changed” and “the decision did not hold” operate at exception-category level

The review calculator matches findings by keys such as `overdue`, compares their numerator and carries decisions/owners by that key. It does not establish whether the same underlying records remain.

**Source-derived example:** last week's ten overdue items are fixed; ten different items become overdue. The comparison can report **no change**, reopen a prior resolved category and carry its previous owner to the new group. Category-level stewardship is a valid feature, but it is weaker than verifying whether particular commitments or decisions held.

**Required outcome:** distinguish category trend from record movement. Show persistent, new and cleared records; bind item/project decisions to stable evidence identities or explicitly restrict the promise to a category-level review. Do not treat unchanged counts as unchanged problems.

Evidence: [record/carry-forward and comparison policies](../../Services/ProgrammeOps/EvidenceReview.cs), [public recurring-review promise](../../Views/Purchase/Index.cshtml).

### 4. Stale sources can still produce “Decision ready”

The calculator records a note for sources older than seven days but does not lower readiness solely for staleness. An existing test explicitly expects `DecisionReady` for a ten-day-old source. Failed sources do lower readiness.

**Consequence:** the code is doing what its test requires, but the headline can overstate suitability for this week's decision. This is a product-policy issue, not a flaky test. A successful upload of an old export also does not prove source freshness.

**Required outcome:** make readiness conditional on the agreed cadence and source-extraction/coverage evidence; present any override and reviewer acceptance in the headline and pack. A note alone should not carry the entire qualification of the main promise.

Evidence: [readiness calculation](../../Services/ProgrammeOps/EvidenceCheck.cs), test `A_failed_source_blocks_readiness_while_a_stale_one_is_only_noted` in [calculator tests](../../ProgrammePulse.Tests/ProgrammeOps/EvidenceCheckCalculatorTests.cs).

### 5. The sold diagnostic has no corresponding narrow product plan

The shipped known plans are Starter, Professional and Enterprise. There is no `Diagnostic` plan in `TenantPlan.All` or the default entitlement matrix. Capability-aware navigation is improved, but the diagnostic is still delivered inside a wider staff/programme application. The old remote branch title mentioning a Diagnostic plan must not be mistaken for a shipped capability.

**Required outcome:** a diagnostic workspace that puts scope, import, mappings, check, decisions and pack together, with one clear next step and a visible acceptance state. This need not introduce another pricing tier; it needs a coherent experience for the offer already being sold.

Evidence: [allowed plans](../../Models/Tenancy/TenantPlan.cs), [entitlements](../../Services/Tenancy/PlanEntitlements.cs), [navigation](../../Views/StaffOps/_Layout.cshtml).

## Proposition and economics

A defensible proposition to test is:

> For a delivery lead preparing a recurring client or portfolio review, turn agreed task and time exports into a traceable exception register, reviewed decisions and a shareable pack, with explicit gaps and limitations.

This identifies a buyer, input, job and output. The customer should buy a better-supported decision and a repeatable review process. Feature breadth and connector count do not establish why they should switch from their current report and analyst.

The pricing case remains unproved. **The purchase page's own default calculator produces a £20,900 negative year-one net position:**

| Calculation from shipped inputs | Amount |
|---|---:|
| Current preparation: 3 people × 4 hours × 48 cycles × £60 | £34,560/year |
| Released hours: (12 − 7) × 48 | 240/year |
| Realised capacity: 240 × £60 × 50% | £7,200/year |
| Diagnostic + 12 monthly fees + adoption: £3,500 + £24,000 + 10 × £60 | £28,100/year |
| Modelled net position | **−£20,900/year** |

The calculator is commendably honest; changing its defaults to manufacture a positive result would not solve the problem. At those assumptions the recurring fee needs a larger reporting burden, a different service scope/price, or evidence of a valuable decision outcome beyond preparation time. At £60/hour, 50% realisation and 48 cycles, the model requires about **19.5 hours released per cycle** to cover first-year cost—more than the default 12 hours currently spent.

Before selling repeat service, specify how many review cycles, which sources/records, who corrects mappings, what human analysis is included, turnaround and support. Measure operator delivery time as well as customer effort. Obtain the buyer's accepted outcome and willingness to continue at the stated price. No claim of unique competitive advantage, quantified savings or avoided loss is established by this review.

Evidence: [calculator](../../wwwroot/js/purchase.js), [default inputs](../../Views/Purchase/Index.cshtml), [offer](paid-diagnostic-offer.md), [commercial gate](30-day-commercial-gate.md).

## Are the recent and open commits doing enough?

**They improve engineering quality and demonstration quality. They do not complete commercial readiness.** Several changes initially described as open were merged while this review was interrupted; the table reflects the refreshed state.

| Change | State checked 28 September | Contribution | What remains |
|---|---|---|---|
| [#26 security/attribution fixes](https://github.com/NewbBuilder9909/OpsHwb/pull/26) | Merged | Material: MFA failure controls, safer identity attribution, evidence-authorship qualification, scoped export/document handling and request bounds. | A deployed final-candidate assessment and operational evidence. Identity approval adds a necessary setup step that the journey must accommodate. |
| [#28 UI and Northstar demo](https://github.com/NewbBuilder9909/OpsHwb/pull/28) | Merged | Material: clearer layouts/navigation/import/review forms and realistic synthetic scenarios. Makes the product easier to inspect and demonstrate. | Does not supply enquiry-to-provisioning orchestration or correct weekly scope/import/comparison semantics. The rich demo is seeded through the test project; it is not proof of customer self-activation. |
| [#25 delivery/build tooling](https://github.com/NewbBuilder9909/OpsHwb/pull/25) | Merged | Useful operational maintenance: toolchain/bootstrap, nested-output exclusions, CI and dependency hygiene. | Does not itself demonstrate a customer outcome or a running supported deployment. |
| [#21 brand/buyer-journey document](https://github.com/NewbBuilder9909/OpsHwb/pull/21) | Merged | Clarifies the staged service-to-product approach. | Documentation cannot be counted as delivered acquisition, payment or setup. |
| [#30 Azure package group](https://github.com/NewbBuilder9909/OpsHwb/pull/30) | **Open**, head `0c23b4e`; build/test/audit and secret scan successful | Updates three dependency versions in `Directory.Packages.props`. Relevant maintenance. | No direct change to the buyer path or the core review limitations. |
| [#31 test-tooling group](https://github.com/NewbBuilder9909/OpsHwb/pull/31) | **Open**, head `5f8784a`; build/test/audit and secret scan successful | Updates coverage collector and test runner in `Directory.Packages.props`. | No direct product or commercial-journey improvement. |

These were the only open application PRs returned by the live API. The local Tempo-reconciliation and skills-evidence branches had no commits ahead of the reviewed `main`; treating their historical worktree descriptions as an outstanding product roadmap would be misleading. Passing checks were also present on the inspected heads of the four merged PRs, but PR-head checks are not deployed-service evidence.

### Separate website: built on a branch, not established as launched

`NewbBuilder9909/Assayer` default `main` at `8cedbb1` contained only `README.md`. The marketing implementation was on `claude/awesome-pascal-hyflk2`, commit `fe76be2`; no open PR was returned for that repository. Its configuration had a blank legal-entity identity and booking URL. Only domain registration was marked complete; email receipt, name-search clearance, legal-entity display, demo regeneration and privacy review were still marked false.

The website has a coherent planned route: sample → call → proposal → invoice. It also has sensible launch checks. These are evidence of unfinished release work, not evidence that the route operates. An attempt to open `https://assayerhq.com` with the web tool failed; that alone does **not** establish a site outage. No production enquiry or purchase was exercised.

Evidence: [website main](https://github.com/NewbBuilder9909/Assayer/tree/8cedbb1a5e9bfbfe2be591272eb5609f5ad412b4), [branch launch configuration](https://github.com/NewbBuilder9909/Assayer/blob/fe76be210c51da1b68a2895a9e0da9a3ce233b04/src/site.config.ts), [branch journey plan](https://github.com/NewbBuilder9909/Assayer/blob/fe76be210c51da1b68a2895a9e0da9a3ce233b04/docs/launch-plan.md).

## Commercial-grade operating gaps

Credit the controls already implemented: scoped repositories and authorization, SQL-backed MFA protections, explicit identity review, production-configuration guards, protected credentials, audit/health endpoints, durable sync state, recorded reviews and a tenant-scoped delivery purge. Earlier reports of missing controls should not be copied forward indiscriminately.

The remaining evidence is about operating the service: named deployment and region, protected persistent keys, restore of database plus matching keys/documents, working monitoring and support escalation, deployed access/recovery/exit rehearsal, and acceptance of each connector actually sold. Enable only the sources and modules needed for the agreed engagement. The [pilot gates](../assayer-pilot-gates-2026-09-27.md) and [security assurance register](../security-assurance.md) distinguish implementation from that evidence.

Two concrete documentation/operation mismatches need correction before attaching these promises to a customer agreement:

- The [data-handling note](diagnostic-data-handling.md) says raw row copies are removed automatically after 90 days. The importer calls retention during a successful import; no independent retention schedule was established. If imports stop, elapsed time alone does not trigger that code. Establish scheduled/manual monitored disposal, or describe the actual mechanism and owner accurately.
- The [archived-tenant runbook](../data-governance.md) still describes Programme/Contract data as unscoped in an older section. The current [delivery purge](../../Services/ProgrammeOps/DeliveryDataPurge.cs) is tenant-scoped and useful. Replace the stale runbook and rehearse complete exit, including member records, other modules if enabled, exports and backup retention. Do not promise “everything deleted” on the strength of the delivery-table purge alone.

Local sanitised-file delivery reduces hosting scope; it does not eliminate the need to agree transfer, storage, access, disposal and responsibility for the actual data.

## Recommended next work and release gates

Priorities follow customer consequence, rather than module count. These are proposed acceptance conditions, not claims that delivery dates or named owners have been agreed.

| Priority | Accountable role | Deliverable | Evidence that closes it |
|---|---|---|---|
| P0 — conversion | Commercial owner | One current offer and working enquiry → proposal/invoice → scheduled start handoff. Remove disabled trial/plan links from the active journey. | A test enquiry from the actual published page reaches the owner and receives acknowledgement; a person outside the build team can explain what they buy, at what price, and what happens next. |
| P0 — truthful output | Product + data engineering | Explicit review scope/freshness, correction/replacement import rules, and record-aware comparison or narrower claims. | Two successive realistic exports, with a correction, deletion, stale source and replacement exceptions, reconcile to agreed counts/hours and correctly show what persisted/cleared/newly appeared. |
| P0 — safe delivery | Service owner | One agreed processing/deletion runbook and a controlled delivery environment appropriate to the engagement. | Rehearsed access, delivery-data purge and wider exit obligations; for hosted real data, the deployment/security/restore gates pass on the candidate. |
| P1 — activation | Product + implementation owner | Diagnostic-specific setup and visible next action; clear identity approval/replay and status-mapping workflow. | A fresh tenant reaches a customer-accepted finding and pack without SQL edits or undocumented developer help. Record elapsed time, operator hands-on time and all support touches. |
| P1 — commercial proof | Founder + customer sponsor | A paid diagnostic with baseline, accepted findings, actual delivery cost and an explicit repeat/stop decision. | Payment/procurement evidence, accepted artefact and viable cost to deliver; a second comparable cycle for any recurring-value claim. Keep customer details outside tracked git. |
| Later, demand-led | Platform/commercial owner | Invitations, billing automation, broader connectors and self-service. | Qualified demand and measured support burden justify the work; the corresponding lifecycle and release tests pass. |

The next product change should be a vertical improvement to **scope → import → reconcile → check → decide → pack → repeat**, with two-cycle acceptance evidence. Another broad module or visual redesign would not address the largest remaining risks. Dependency/security maintenance should continue, but should be reported separately from commercial progress.

## Verification record

- Isolated checkout: `<local>/AssayerHQ-gtm-review-20260928`, detached at `947c62a`.
- Current unit/architecture suite: **1,617 passed, zero failed, zero skipped**. Build emitted one nullable-argument warning in `StaffAuditLogTenantIsolationIntegrationTests.cs`.
- Razor validation: **104 views compiled, zero warnings/errors**; the runtime-generated `GardnerPMBlog` view is excluded by the repository's check script.
- SQL/persona/render verification: **423 passed, zero failed, zero skipped**, in 2 minutes 45 seconds, with SQL required and skipping disabled. The shared integration connection used the dedicated `AssayerGtm20260928_IntegrationTests` database; the Northstar fixture uses its separate demo database by design.
- Test records: `<local>/AssayerHQ-gtm-review-20260928/artifacts/gtm-review-20260928/gtm-unit-current.trx` and `gtm-sql-current.trx`. Total across the two selections: **2,040 passing tests**. The earlier 1,546-test result on `da89eb8` was superseded and is not the current-candidate evidence.
- No live connector acceptance, payment, production enquiry, customer usability session, independent security assessment or full hosted-browser journey was performed. Synthetic HTTP and SQL tests are useful evidence of implementation, not substitutes for these.

**Launch recommendation:** sell the smallest honest, operator-delivered diagnostic once its handoffs and data obligations are ready; treat the recurring application as unfinished until the second-cycle semantics and customer value are demonstrated. The codebase is becoming more professional. The commercial service still needs a completed journey and proof of why a buyer should pay repeatedly.

## Progress since this review

This review stays as written. What has been done about it since, and what is next, is tracked in [the build direction](../direction.md).
