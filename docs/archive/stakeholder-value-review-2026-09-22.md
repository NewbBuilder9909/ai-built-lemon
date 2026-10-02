# Commercial coherence and stakeholder challenge

Review date: 22 September 2026. Source baseline: c139d95, initially clean working tree. This is an evidence-based product assessment and simulated stakeholder challenge, not interviews with customers. Prices, target segment and acceptance thresholds below are hypotheses to test. No customer outcomes or willingness to pay have been established in this review.

## Verdict

There is a coherent engineering core and a plausible paid service. There is not yet evidence of a repeatable software business. The commercial presentation is broader than the proven customer outcome.

The core is: reconcile delivery commitments, recorded effort and planned resources across existing tools; expose missing evidence; help the reporting owner decide what to investigate and act on. Staff identity, rates, leave, source ingestion and reporting support that chain. ERP demos, general HR, branding, invoicing, skills intelligence, service analytics and agent delivery assurance expand the story into different buying problems. Keep useful foundations, but remove those expansions from the initial sales narrative.

The product should initially sell **a reconciled weekly delivery review for services firms whose existing tools disagree**. The deliverable is an accepted exception pack with traceable evidence, an accountable owner and a next decision. The software is the workbench behind an assisted service until repetition proves what can be automated.

## What the code actually supports

| Evidence inspected | Useful capability | Commercial limitation |
|---|---|---|
| `Services/ProgrammeOps/ProgrammeOverviewQueryService.cs` | Open, blocked and overdue work; milestones; planned allocations separated from completed work | Counts do not establish impact or predict delivery dates. A booking is not demonstrated available capacity. |
| `Services/ProgrammeOps/ReportingQueryService.cs` | Effort variance, reporting periods, cost coverage, unknown billability, unpriced hours and currency breakdowns | Completeness and agreed definitions determine whether a precise number is useful. Actual residual hours are not a forward resource forecast. |
| `Services/ProgrammeOps/EstimateCalibrationService.cs` | Original estimates captured before work starts; completed comparable samples reviewed before calibration | Requires prospective baselines and enough comparable observations. No proven schedule forecasting or individual performance inference. |
| `Services/ContractOps/ContractCommercialService.cs` | Contract value and incurred cost calculations; rate history; currency exclusions | Time is selected through customer and contract dates, not explicit contract allocation. Overlapping contracts can share costs. Undated entries enter the selected set and require separate handling. |
| `Services/ContractOps/PortfolioMarginService.cs` | Currency-separated portfolio rollups; contracts without margin excluded and counted | Aggregation inherits contract attribution limitations. Fixed-price value less incurred cost is not final margin, recognized revenue or cash. |
| Sync status, identity workflows and tenancy/security documentation | Foundations for explaining freshness, identity gaps and controlled access | A code control is not deployment assurance. Use the current security register rather than copying resolved findings from older reviews. |
| `docs/agent-delivery-build-gate.md` | Offline contract/replay checks with a deliberately unmet customer measurement gate | Does not establish live agent integrations or commercial ROI. |
| `Views/Purchase/Index.cshtml`, `wwwroot/js/purchase.js` | Public assisted-diagnostic proposition and browser-only effort calculator | “Break-even diagnostic fee” currently equals hypothetical annual time value. It omits realization, recurring fees and adoption effort; it is not a defensible buying threshold. Email opens without a configured sales recipient, so this is not a complete lead capture journey. |

Some source comments and older reviews describe historical tenant limitations. This assessment relies on the inspected implementation for the commercial findings above; it does not reopen historical security findings without verification. GitHub/Freshdesk branch status in the existing claim register was not independently verified here.

## Three rounds of stakeholder challenge

These are adversarial reasoning rounds, not fabricated customer endorsements. A proposition passes the wording stage when its decision, evidence, limitations and purchase test are explicit. Customer acceptance remains open for every persona.

| Persona | Round 1: challenge to the broad suite | Round 2: challenge to “better reporting” | Round 3: bounded proposition and acceptance test |
|---|---|---|---|
| CFO | “I cannot buy margin protection from a number that can double-allocate cost or omit remaining work.” | “Finding a discrepancy is not recovering money. Staff time released is not cash saved.” | Review a selected engagement's cost evidence and unpriced/unattributed effort. Finance reconciles sampled records and accepts or corrects every quantified finding. Count recovery only after an approved correction reaches the relevant financial record. |
| CTO | “Why maintain another integration platform, and can I trust its data boundary?” | “An export still creates mapping, security and support work.” | Begin with one bounded export workflow and a mapping/coverage report. Measure customer technical effort, explain every exclusion and agree retention. Connected access requires the current deployment gates and a demonstrated operational owner. |
| CEO | “Which outcome deserves budget instead of another dashboard?” | “A nicer weekly pack does not prove a better business.” | Receive the material commitments needing an executive decision, each with evidence, consequence, owner and deadline. Sponsor records the decision and verifies the outcome later; no attribution of all subsequent improvement to the product. |
| COO | “Can this really tell me who can take more work?” | “Plans, actuals and availability describe different things. Comparing them casually is dangerous.” | Reconcile a chosen team's bookings and delivery demand for the same period. Label mismatches as investigation prompts. Planner confirms availability and owns any resourcing change; no automatic promise that spare capacity exists. |
| PMO | “Will this create another reporting process for me to maintain?” | “How much time will I spend correcting your mappings and checking your results?” | Replay one existing pack and repeat the same workflow. Measure extraction, correction, reconciliation, review and rework, not just report generation. Every finding is reproducible from the agreed export. |

The resulting story is coherent because all five stakeholders consume the same review, with different decisions and permissions. They are not five separate products or five required sales approvals. PMO is the working champion; COO/delivery director is the likely operational buyer; CFO approves material spend; CTO gates technical risk; CEO sponsors only where the commitment warrants it.

## Marketable value and the data that makes it credible

| Stakeholder | Message to test | Minimum data and useful metric | Boundary |
|---|---|---|---|
| CFO | “Establish which delivery cost figures are defensible before acting on them.” | Approved project/contract mapping, dated actuals, effective rates, currencies and exclusions. Report priced hours / in-scope logged hours; unresolved attribution hours; reconciled incurred cost by currency. | Exclude ambiguous overlapping contracts from quantified contract claims. Do not call contract value minus cost-to-date forecast profit. Future cost needs a separately approved remaining-effort estimate. |
| CTO | “Evaluate one reporting workflow with a visible integration and data burden.” | Source inventory, mapping rules, source IDs, freshness, access boundary, setup/support time. Report customer technical hours and unresolved mapping count. | Current value is bounded evaluation and explainability; lower integration cost is a hypothesis. No universal connector or perimeter guarantee. |
| CEO | “Bring evidence-backed commitment decisions to the weekly review.” | Commitment, due date, source-linked issue, materiality agreed by sponsor, owner, action and outcome date. Report accepted decisions and overdue agreed actions. | A decision count is workflow evidence, not revenue generated or losses avoided. |
| COO | “Expose booking and delivery discrepancies before the resourcing meeting.” | Matching person/project keys, same dates and units, planned hours, remaining demand if available, working calendar and approved leave. Report unresolved mismatches and accepted corrective actions. | Remaining demand and dated availability may need manual verification. Total original estimate minus time logged is not automatically remaining work. |
| PMO | “Produce a traceable weekly pack with less reconciliation work.” | Baseline and assisted person-minutes by step, source capture times, record counts, unresolved mappings, accepted exceptions and correction effort. Report net person-minutes released per comparable cycle. | Include all participants and exceptions. A faster incomplete pack fails acceptance. |

Every finding needs: tenant/customer scope, period, source record IDs, source timestamp, extraction timestamp, definition/calculation version, numerator and denominator, omissions, observed-versus-inferred label, reviewer, action owner and disposition. This is initially a delivered evidence register; it is not a claim that one unified product workflow already implements all fields.

## One example that hangs together

Fictional example, not product output or customer evidence: a weekly pack reports 240 logged hours across an engagement. Of these, 200 have an approved contract mapping and effective GBP cost rate; 40 cannot yet be allocated confidently. The agreed priced cost is GBP 12,000. Pricing coverage is 83.3%; the cost of the remaining 40 hours is unknown.

PMO traces and corrects the 40-hour exception. CFO withholds a complete-margin conclusion. CTO can inspect the mapping and extraction boundary. COO investigates the affected work before reallocating people. CEO receives a decision request only if the unresolved commitment is material. All five use the same fact; none needs a new dashboard metric invented for their job title.

For the same fictional workflow, baseline preparation totals 12 person-hours per cycle and the assisted repeat totals 7, including corrections and review. Five hours at an assumed GBP 60/hour across 48 comparable cycles represents GBP 14,400 annual capacity value. It is neither cash saved nor guaranteed annual performance. A recurring GBP 1,000/month service plus GBP 2,000 setup would leave only GBP 400 of that modeled value before customer adoption costs. That is a weak time-only business case: lower the cost, qualify a larger problem, or decline the sale. Do not manufacture “avoided loss” to rescue it.

## Where the proposition competes

Productive advertises profitability, utilization and revenue reporting; Harvest has project profitability reporting; Tempo has project cost/revenue reporting. These capabilities make generic dashboards and margin visibility weak differentiators. Sources checked during this review: [Productive reporting](https://productive.io/reporting/), [Harvest profitability report](https://support.getharvest.com/hc/en-us/articles/25342727197581-Profitability-report), [Tempo reporting](https://help.tempo.io/financialmanager/latest/reporting).

Inference: the best initial opportunity is an organization retaining multiple tools, with a specific unresolved reconciliation problem and no appetite for migration. That is a positioning hypothesis, not proof that competitors cannot solve the problem. The incumbent spreadsheet, existing analyst, Power BI model or source report is also competition. Replay the same pack against that baseline. If their existing setup already answers the decision reliably and cheaply, disqualify the opportunity.

Initial segment hypothesis: UK digital delivery consultancies and agencies with roughly 30–150 delivery staff, multiple concurrent client engagements, a weekly portfolio review and work/time/resource information in at least two systems. Size is a prospecting filter, not evidence of demand. The stronger qualification signal is a named reporting owner with material measured reconciliation work and a recent consequential discrepancy.

## Revenue offer to test now

Offer name: **Weekly Delivery Evidence Diagnostic**.

Pitch: “We reconcile one weekly delivery pack against agreed source exports, identify which exceptions need action and show the evidence and gaps behind each finding. You receive a reviewed exception pack and a measured decision on whether repeating the service is worthwhile.”

Proposed private quote experiment: **GBP 2,000 fixed fee**, one reporting pack, up to two agreed source exports, up to five projects, one representative period, one correction round and a 60-minute playback. Deliver within ten working days of accepted inputs. Scope invoice/payment terms in the proposal; a suggested starting point is 50% to book and 50% on delivery. This is not published pricing, a market benchmark or observed willingness to pay.

Acceptance is delivery of the agreed reproducible review, including a valid conclusion that no material issue was found. Do not make payment depend on inventing a saving. Separately, commercial success requires the buyer to accept a useful finding or a worthwhile measured effort reduction and choose a next paid step.

Illustrative internal economics: cap initial delivery at 16 hours at an assumed fully loaded GBP 60/hour plus GBP 100 direct tools cost. At GBP 2,000, contribution before acquisition, overhead and tax is GBP 940 (47%). Four additional sales hours reduce this to GBP 700 (35%). Record actual founder time. If the diagnostic cannot fit, reduce scope or quote a transparent bespoke service; do not hide unpaid engineering.

After acceptance, test a **GBP 1,000/month assisted repeat** for the same pack and source scope, with at most four cycles and a defined support allowance. At eight internal hours/month plus GBP 100 direct costs, illustrative contribution is GBP 420 (42%) before sales and overhead. This is a managed-service experiment, not SaaS economics. Quote only where conservative buyer value exceeds the total recurring and adoption cost. Additional source development is separately scoped, never silently included.

Do not set annual SaaS tiers yet. Earn that option through several paid customers repeating substantially the same workflow with falling delivery effort, acceptable support demand and verified deployment readiness. Customer-controlled deployment should be a separate scoped assessment, not a free sales promise.

## Acquisition and the next commercial cycle

1. Founder selects ten relevant firms through existing contacts and targeted research. Request introductions to the person preparing the weekly delivery review. Do not lead with the full module list.
2. Aim for five artifact-led conversations in the first two weeks. Ask to see the disputed figure, how it is reconstructed, the last consequential error and the actual approval route.
3. Qualify using the existing diagnostic scorecard. Produce up to three bounded proposals with this quote hypothesis. Log explicit price/scope objections and reasons for no decision.
4. Deliver one paid diagnostic within the existing 30-day commercial gate. Record invoice/cash status separately from interest, all delivery hours, accepted findings and repeat purchase decision.
5. Repeat the same pack for at least two comparable cycles before using an effort-reduction claim. Record changes in volume and scope. Obtain permission before using a customer result publicly.

These are activity targets, not conversion forecasts. If no qualified buyer provides an artifact or budget route, change the target/acquisition approach. If paid delivery is accepted but not repeated, investigate whether this is a one-off consulting need. If repetition works only through bespoke engineering, price consulting honestly or narrow scope further.

## Product decisions following the critique

| Priority | Concrete next change | Acceptance |
|---|---|---|
| Before a finance demo | Suppress or explicitly qualify contract-level margin where attribution overlaps; label fixed-price cost-to-date headroom accurately | A two-contract/same-customer example cannot be presented as independently attributed profit; remaining cost and exclusions remain explicit |
| Before public commercial launch | Replace the calculator's break-even claim with a clearly scoped capacity-value calculation and include adoption/recurring cost in any buying case; configure a genuine enquiry destination | Buyer cannot mistake hypothetical annual time value for cash savings or a justified diagnostic price; a test enquiry reaches the designated owner |
| First paid delivery | Create one reusable exception/evidence pack with the fields above; manual preparation is acceptable | PMO reproduces findings and each disposition has an owner |
| Only after repeat demand | Automate the slowest measured preparation/reconciliation step | Net customer and internal effort falls without lost coverage |
| Before connected delivery | Satisfy current source acceptance and deployment/security gates | Evidence applies to the actual customer environment and chosen sources |

Freeze new connector breadth, HR expansion, skills/agent scoring, general invoicing expansion and executive dashboard proliferation during this commercial test unless a contracted scope demonstrably depends on them. Maintain necessary security fixes and useful existing foundations.

## Final gate

Verification in this review: 45 targeted tests passed, zero failed or skipped, covering ContractCommercialService, PortfolioMarginService, ReportingQueryService, EstimateCalibration and ProgrammeOverviewQueryService. The app and test project built during that run. These checks establish existing calculation behavior; they do not validate live integrations, deployment, customer outcomes or the proposed pricing. No application behavior was changed in this assessment.

The proposition now has a clear benefit and a falsifiable acceptance test for each stakeholder. The strongest immediate user value is PMO reconciliation; the strongest likely budget route is the COO/delivery leader. CFO value is conditional on attribution and complete cost evidence. CTO is initially an approval stakeholder, not an independent product market. CEO value depends on the materiality of actual decisions.

That is enough clarity to sell a bounded diagnostic. It is not enough evidence to claim product-market fit, recurring revenue, protected margin or readiness for broad self-service SaaS. The next cycle must include a real paid buyer; additional simulated persona rounds cannot supply that evidence.
