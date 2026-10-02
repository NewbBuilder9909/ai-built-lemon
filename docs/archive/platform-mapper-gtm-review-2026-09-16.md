# Multi-platform mapper and management header: GTM review and backlog

Review date: 16 September 2026. Perspective: senior project manager reviewing technical architecture, delivery risk and commercial readiness.

**Decision: proceed with product validation and foundation work; do not launch this as shared multi-tenant SaaS yet.** There is a credible proposition in bringing customer commitments, delivery progress and capacity into one management view. The repository demonstrates parts of that proposition, but not yet reliable reconciliation across tools or safe isolation across all customer data.

“Header” is interpreted here as the management layer above existing systems: common navigation, portfolio reporting, exceptions and links back to source records. Tenant branding supports that experience, but is not the primary value proposition. Jira and HubSpot should be optional Bronze adapters, not prerequisites for buying the product.

This assessment covers the current working tree, including substantial staged, unstaged and untracked development. It is a static review of code and documentation, supplemented by official vendor API documentation. No live customer integrations, deployment, build or tests were run for this review. The 344 passing tests in [release-readiness.md](../release-readiness.md) are previously recorded evidence, not a fresh verification or proof of cross-tenant safety. Proposed capabilities below are backlog, not implemented features.

## Product position and buyer value

Recommended positioning: **a programme assurance workspace that connects customer commitments to delivery evidence across the tools teams already use.**

The initial buyer hypothesis is a delivery director, PMO lead or operations director in a services organisation running several client programmes across different team tools. The commercial sponsor could be the COO; finance is a stakeholder where cost and contract reporting are enabled. Validate this segment with interviews and paid design partners before committing to broad connector coverage.

The practical purchase trigger is repeated manual reconciliation: preparing the weekly portfolio pack, finding which customer commitments are at risk, and understanding whether delivery teams can absorb new work. The desired outcome is a trusted answer with traceable evidence and an accountable owner.

| Existing approach | Why a buyer may keep it | What this solution must prove |
|---|---|---|
| Reports inside the customer's existing tools | Familiar, already purchased, low adoption effort | A useful relationship between commercial commitments and delivery that one source alone cannot explain |
| Spreadsheet portfolio pack | Flexible and inexpensive to start | Less recurring reconciliation work without losing explainability |
| BI/data warehouse implementation | Flexible reporting and established data expertise | Faster assisted setup and maintained PMO definitions, identity links and exception workflows |
| Integration middleware | Moves data between systems | Useful management decisions beyond data movement |
| Full portfolio/operations suite | Broader operating model in one product | A narrower adoption footprint with enough governance for the buyer's problem |

These are substitution hypotheses, not a feature-by-feature competitor benchmark. No defensible market size, price or competitive win rate is established by this repository.

The strongest demonstration would follow one customer through a linked commercial commitment, its delivery projects, a capacity exception and the underlying source evidence. Additional connectors are valuable only where they improve that journey. Staff leave, invoicing and CMS functionality broaden maintenance and support responsibilities; keep their expansion outside the initial sales promise unless design partners identify them as essential.

## Architect findings translated into delivery decisions

| Priority | Evidence in the current solution | Business implication and decision |
|---|---|---|
| Critical | [ProgrammeRepository](../../Services/ProgrammeOps/ProgrammeRepository.cs) retrieves whole domain tables without tenant filters; the existing release register also identifies unscoped reporting, contracts and approvals | Shared customer deployments can expose or act on other customers' data. Isolation is a release gate, including exports, files, audits and background work |
| Critical | [ProgrammeOperationsComposer](../../Composers/ProgrammeOperationsComposer.cs) binds deployment-level source options; repository lookups use ExternalSource + ExternalId | Connecting another customer or another source account is not a safe configuration-only change. Introduce tenant-owned connections and account-qualified identities first |
| High | [HubPlannerMappingService](../../Services/Integrations/HubPlanner/HubPlannerMappingService.cs) turns bookings into WorkItems and marks them Done once their end date passes; [ProgrammeOverviewQueryService](../../Services/ProgrammeOps/ProgrammeOverviewQueryService.cs) counts Done items in completion | Time passing can appear as delivery progress. Separate planned bookings from delivery outcomes before combining sources in a customer-facing KPI |
| High | [ClickUpMappingService](../../Services/Integrations/ClickUp/ClickUpMappingService.cs) resolves people by email against GetAllAsync and drops unresolved assignees | Capacity may look healthier because people are missing; matching is also not tenant-bounded. Use explicit identity links and a visible unresolved queue |
| High | Silver WorkItem carries one source identity; [Customer](../../Models/Programme/Customer.cs) only has an internal key/name and timestamps | Multiple adapters currently create parallel source records, not a reconciled customer/project model. A crosswalk is needed before claiming a unified view |
| High | [ClickUpSyncService](../../Services/Integrations/ClickUp/ClickUpSyncService.cs) persists records progressively; [SyncRunGuard](../../Services/Integrations/Resilience/SyncRunGuard.cs) locks by source within one process | Failed runs can leave mixed-freshness data. Add durable run state, reconciliation and a publication policy; background execution alone does not solve trust |
| High | [TenantFeatureGate](../../Services/Tenancy/TenantFeatureGate.cs) returns true for unresolved tenants; [PlanEntitlements](../../Services/Tenancy/PlanEntitlements.cs) packages named connectors | Entitlements are not a complete security boundary or a source-neutral product package. Fail closed and test route enforcement; sell outcomes/capacity rather than a mandatory vendor |
| Medium | Overview capacity totals outstanding estimated hours, credits primary allocations, and displays weekly staff hours; missing estimates contribute zero | This is a workload indicator, not a dated resource forecast. Label it accurately and show missing-data coverage before selling capacity forecasting |
| Strength | Separate mapping services, raw capture, shared Gold consumers, retry handling, audit records and structurally separate cost views | Preserve these boundaries. Extend the modular monolith instead of starting with a platform rewrite |

The existing release review gives a conditional pilot recommendation for a small number of tenants on one instance. **That condition is insufficient while business tables remain global.** Until isolation is proven, any real-customer pilot needs a dedicated deployment, database, credentials and storage boundary per customer, plus verified access and operational controls. A single instance or a small user count does not itself isolate customer data.

## Bronze inputs and the canonical model

Bronze should retain source facts and provenance. Silver should express business meaning. Gold should calculate only metrics justified by the available facts. Adding HubSpot requires extending Silver; the claim that future sources never change Silver/Gold is too strong for a CRM domain that is not represented today.

| Input | Bronze scope for first release | Silver mapping and guardrails | Management value |
|---|---|---|---|
| Jira Cloud | Selected projects, issues, status/field metadata, parent links; worklogs only when available and authorised | Configurable project/workstream grouping; issues to WorkItems; approved status mapping; stable external user links. Story points stay distinct from hours. Do not assume a Jira project equals a programme | Delivery risk and progress across selected teams |
| HubSpot | Companies, deals, pipeline/stage metadata and associations; contacts only if needed | Company to Customer through a crosswalk; Deal to a new Opportunity/Commitment model. Explicit association from opportunity to one or more delivery projects. Closed-won is not automatically a signed contract, project budget or recognised revenue | Commercial-to-delivery handover and commitments at risk |
| ClickUp | Existing task hierarchy, people and time payloads | Retain adapter; migrate to tenant connections and versioned mappings | Existing delivery reporting route |
| Hub Planner | Existing project/resource/booking payloads | Booking to PlannedAllocation, with dates and allocation units; separate from completed work and actual time | Planned demand/capacity evidence after live validation |
| CSV/manual | Uploaded files or submitted records, import batch, author, capture time and source label | Validated records through the same mapping rules, identities and audit path; explicit manual authority | Accessible starting point and controlled fallback |

Jira's REST API documents authentication, scopes, permissions and pagination; its rate-limit model requires connector-specific handling. Scope the first adapter to **Jira Cloud**, with Data Center a separate later decision. Use least-privilege OAuth for the product integration, consented project selection, paginated retrieval and retry/checkpoint tests. See [Jira REST documentation](https://developer.atlassian.com/cloud/jira/platform/rest/v3/intro) and [rate limiting](https://developer.atlassian.com/cloud/jira/platform/rate-limiting/).

HubSpot represents companies/deals and their relationships through object and association APIs. Access depends on scopes and available account capabilities; detect these during onboarding instead of assuming that a customer's subscription exposes every desired feature. Keep premium/custom objects outside the minimum contract. See [object APIs](https://developers.hubspot.com/docs/api-reference/latest/crm/using-object-apis), [associations](https://developers.hubspot.com/docs/api-reference/latest/crm/associations/overview), [scopes](https://developers.hubspot.com/docs/apps/developer-platform/build-apps/authentication/scopes) and [API limits](https://developers.hubspot.com/docs/developer-tooling/platform/usage-guidelines).

Recommended shared foundations:

- Connection registry: tenant, provider, source account/site, selected scope, secret reference, permissions, capability results and connection health. Secrets belong in managed secret storage.
- External identity: a unique tuple of tenant, connection, entity type and external ID, linked to a canonical entity. Allow many external identities per canonical customer/project/person; require review for ambiguous matches.
- Field authority: designate the owner of each important field and how overrides work. A refresh must not silently overwrite approved local budgets or manually reconciled links.
- Mapping versions: field, hierarchy, status and unit rules with preview, reject report, approval and replay from retained Bronze. Preserve source values and distinguish unknown from zero.
- Sync runs: durable checkpoints, idempotent writes with database uniqueness, bounded retries, deletion/archive handling and permission-loss detection. Never interpret a partial or forbidden response as mass deletion.
- Publication state: expose last successful publication, current failure and per-source coverage. Publish a consistent run or clearly disclose mixed freshness; retained Bronze needs a tested remapping path.

## Customers without Jira, HubSpot or particular features

All routes below are target product behaviour. The current application already has some manual governance/customer entry, but it does not yet demonstrate a complete standalone import/manual onboarding journey.

| Customer situation | Proposed route | Honest limit |
|---|---|---|
| Jira and HubSpot available | Connect both, review customer/deal/project links | Cross-system joins require approval; installing two adapters does not establish the relationships |
| Jira but no HubSpot | Delivery connector plus CSV/manual customer and commitment register | No automatic sales-pipeline freshness |
| HubSpot but no Jira | CRM connector plus ClickUp where used, otherwise delivery CSV/manual updates | CRM status cannot establish delivery completion |
| Neither tool | Starter templates for customers, projects, work items, staff and optional time; simple manual maintenance | Update ownership and cadence must be explicit; no claim of live synchronisation |
| No time tracking or cost rates | Delivery/milestone reporting; optionally import approved actuals/rates | Utilisation, actual cost and margin unavailable until sufficient inputs exist |
| Tool present, API feature unavailable or consent refused | Explain capability restriction; offer export/import or disable dependent metric | Never silently replace unavailable data with zero or a healthy status |
| Integration temporarily unavailable | Retain last published view with visible age and incident state | Manual overrides require provenance and conflict rules before reconnection |

Use a capability matrix to enable reports: delivery status, dated planned capacity, actual time, commercial pipeline and cost are separate capabilities. Distinguish missing source capability from this product's paid entitlement and from the user's access permissions. Each requires a different explanation and remedy.

The common header should show tenant, reporting period, active sources, last successful publication, data coverage and source links. The critical action is “review this exception” with owner and evidence. A logo and a combined total are not sufficient.

## Tools and implementation choices

Keep the current .NET/Umbraco modular application for the next phase. Implement a small common connector contract for capability discovery, scoped extraction, checkpoints and diagnostics; keep provider-specific DTOs in adapters. Avoid a universal low-code mapping designer until several customers expose repeatable mapping needs.

Build first-party adapters for the initial supported sources. Evaluate integration middleware only for paid demand in the long tail, against authentication ownership, retry/replay guarantees, residency, observability, support responsibility and per-customer running cost. Middleware should feed the governed ingestion boundary, not bypass Silver validation.

Use a durable background queue/worker backed by persistent run state; choose its product after a short spike against deployment constraints. A database lease and unique keys are necessary even with a queue. Reuse existing retry and audit components where appropriate. Keep AI-assisted mapping suggestions optional and human-approved; deterministic mappings must remain sufficient to operate the product.

## Prioritised delivery backlog

All items are proposed and unstarted for this assessment; existing work is noted in the evidence above. P0 gates customer safety/trust, P1 gates the supported pilot proposition, P2 follows demonstrated demand. Sizes are relative complexity (S/M/L/XL), not estimates or commitments. Owners are accountable roles pending named assignment. Dependencies reference IDs.

| ID | Priority / size | Outcome and acceptance evidence | Owner | Dependencies |
|---|---|---|---|---|
| B01 | P0 / M | Approve buyer, first decision journey and pilot scope; interview at least five target buyers and recruit two design partners with recorded baseline reporting effort | Product/PM | None |
| B02 | P0 / XL | Isolate all business data, credentials, files, exports, audits and jobs; tenant-qualified authorisation and DB integration tests prove tenant A cannot read or mutate B, including guessed IDs | Architect/Engineering | None |
| B03 | P0 / L | Tenant connection registry and external crosswalk; identical IDs in two accounts/tenants remain distinct, concurrent replay creates no duplicates | Engineering | B02 |
| B04 | P0 / L | Approve semantic model and metric definitions; bookings never increment completed delivery, points never become hours implicitly, unknown data stays visible | Product/Architect | B01 |
| B05 | P0 / L | Identity matching, duplicate review and field authority; ambiguous people/projects quarantine visibly and re-sync preserves approved overrides | Engineering/Data | B03, B04 |
| B06 | P0 / L | Durable sync/publication lifecycle; restart, cancellation, 429, partial run and two-worker tests prove recovery and accurate freshness; archive/permission-loss cases do not delete valid data | Engineering/Ops | B03 |
| B07 | P0 / M | Release controls: reproducible artifact, migration/restore rehearsal, restricted backoffice, gated demo routes, alert routing and named incident owner; attach evidence to release record | Ops | None |
| B08 | P0 / M | Reject unresolved tenant entitlements, test server-side route gates and auth/MFA flows; onboarding, suspension and offboarding work without manual SQL | Engineering | B02 |
| B09 | P1 / L | Versioned mappings and retained-Bronze replay; preview identifies affected rows, rejects are actionable, published results trace to mapping/run versions | Engineering/Data | B03, B04, B05, B06 |
| B10 | P1 / L | CSV/manual minimum product; a customer with neither Jira nor HubSpot completes the portfolio journey, reimports safely and resolves invalid rows without developer help | Product/Engineering | B04, B05, B09 |
| B11 | P1 / L | Jira Cloud read-only adapter; live consented account validates selected projects, pagination, custom statuses, missing people, hierarchy changes and optional worklogs | Engineering/QA | B03, B05, B06, B09 |
| B12 | P1 / L | HubSpot read-only adapter and Opportunity/Commitment extension; live company/deal/association reconciliation, capability denial and currency handling validated; no automatic contract conversion | Engineering/QA | B03, B04, B05, B06, B09 |
| B13 | P1 / M | Migrate ClickUp and validate Hub Planner; reference dataset reconciles task counts, identities, booking units/timezones and planned-versus-actual distinctions | Engineering/QA | B03, B04, B05, B06 |
| B14 | P1 / M | Common management header and exception journey; each KPI shows period, source freshness and coverage with traceable detail; unsupported metrics explain missing inputs | Product/UX | B04, B06, B10; relevant adapter |
| B15 | P1 / M | Customer onboarding capability check; guided connection or fallback selection, tested revocation/rotation and published support matrix by provider/deployment type | Product/Engineering | B08, B10; relevant adapter |
| B16 | P1 / M | Retention, export and deletion cover new CRM/raw/crosswalk data and backups under agreed policy; least-privilege access and data-processing terms reviewed | Data/Ops/Legal | B02, B03; relevant adapter |
| B17 | P1 / M | Pilot measures and offer: price hypothesis, support boundaries, onboarding fee, operating cost and weekly adoption recorded; buyer reviews outcomes against baseline | Commercial/PM | B01, B10, B14, B15 |
| B18 | P2 / L | Dated capacity/forecast improvements validated against approved allocations, leave and actuals; missing estimates disclosed and forecasts back-tested | Product/Data | B04, B13; pilot evidence |
| B19 | P2 / M | Additional adapter selection backed by paying demand, reuse and support-cost case; publish a supported scope before committing | Product/Engineering | B17 |
| B20 | P2 / L | Evaluate enterprise SSO, regional hosting, marketplace distribution and outbound actions individually against signed demand and operational readiness | Product/Ops | Pilot exit |

For B02, extend the existing release register's R12/R13; B06 covers R17/R18; B07 covers R24/R25/R27/R31; B08 covers R14/R26/R29; B13 covers R21; B16 covers R19/R22. This review raises the importance of R20 (Bronze replay): accepting no replay is inconsistent with selling configurable mappings.

## Delivery sequence and release decisions

**Foundation gate:** agree the buyer journey and definitions while engineering closes isolation, connection identity and publication risks. No shared customer data before B02 passes. A dedicated-customer pilot is only a containment option after its access, deployment and semantic checks pass.

**Pilot product gate:** prove CSV/manual plus one delivery adapter first, using existing ClickUp work where suitable. Build Jira and HubSpot against the shared contract, selecting order from design-partner demand. Do not advertise a Jira-to-HubSpot journey until both adapters and the relationship mapping are validated together. B10 prevents the product being dependent on either vendor.

**Paid pilot gate:** require B07/B08, relevant privacy work, verified adapters, support ownership and an agreed scope. Proposed success targets are a first trusted portfolio view within two working days of receiving usable access/data, at least 50% less weekly report preparation, zero unexplained differences in an agreed reference dataset, visible handling of every unmatched record, and no unauthorised cross-tenant access. These are targets for negotiation, not achieved results. Measure effort at the start and end of each pilot and record exclusions.

**Wider release gate:** obtain repeatable onboarding and pilot renewal/purchase evidence, complete tenant isolation, and pass integration, recovery, migration and load tests at the advertised operating envelope. Define supported record volumes, refresh cadence, RPO/RTO and support response targets from measured results before selling them. Package pricing around portfolio scope, refresh/service level and governance depth; keep a usable import route in the entry offer. Validate contribution margin after onboarding labour, hosting, connector maintenance and support.

Plan the next delivery increment against these gates rather than promise a fixed launch date from a static review. The PM should name owners, have the team estimate B02-B06, resolve the first adapter order with design partners and maintain one linked risk/backlog register. Expansion of connector count should follow demonstrated customer value and repeatable support economics.

## Progress log

### 16 September 2026 — "trust foundation" increment

Built the engineering P0 items that make a dedicated-environment pilot's numbers trustworthy, in dependency order, leaving B02/B03 (which share one schema-wide ripple: tenant columns and connection-qualified identity on every `ProgrammeOps_*` table) as the next increment. Evidence: build 0 warnings, 389 tests (from 344), live boot against LocalDB with the ProgrammeOps plan advancing 09 → 12; detail in [release-readiness.md](../release-readiness.md) §3.

| Item | Status | What exists now |
|---|---|---|
| B04 (semantic model) | **Done for Hub Planner** | Bookings map to `PlannedAllocation`, never to a WorkItem; elapsed bookings can no longer increment Done / % complete (test: `Elapsed_planned_allocations_never_count_as_done_work_or_percent_complete`). Overview shows planned allocation and estimate-based workload as separate, labelled indicators with coverage (items without estimate, bookings without hours, unmatched, undated). Story points vs hours is a Jira concern and stays open |
| B05 (identity matching) | **Done — explicit links + visible queue** | `IStaffIdentityResolver` (link by source user id → link by email → recorded `UnresolvedIdentity`, with a roster email match only a suggestion to approve since 27 Sep 2026), Admin identity queue at `/staffops/programme/identities`, sync results and the overview header report unmatched-people counts. Field authority / approved-override preservation for other fields remains open |
| B06 (durable sync lifecycle) | **Done for run state + lease; background execution open** | `ProgrammeOps_SyncRun` + `ProgrammeOps_SyncLease` (conditional-UPDATE lease, heartbeat, abandoned-run takeover), per-source publication state in the Programme Overview header and platform console. Runs still execute inside the HTTP request (R18); restart/cancellation/429/two-worker cases are covered by unit tests over the lease semantics, not by a two-process integration test |
| B08 (entitlements) | **Fail-closed done** | Unresolved tenant → `FeatureGateOutcome.TenantUnresolved`, distinct message from `NotInPlan`. Onboarding/suspension/offboarding without SQL (R14) still open |
| B14 (management header) | **Partial** | Data sources panel: per-source status, last published, freshness, last failure, data coverage. Reporting period and source links per KPI not yet |
| B16 (retention/erasure of new data) | **Done for this increment's tables** | Retention rows and GDPR export/erasure participation for identity links; see `data-governance.md` |
| B02, B03 | **Done** (see 18 September entry below) | — |

### 18 September 2026 — tenant isolation increment (B02/B03)

Closed the two items the 16 September increment deliberately deferred as
"one schema-wide ripple": tenant columns across every `ProgrammeOps_*`
table (B02) and a tenant-owned source connection registry with
connection-qualified identity, structural half only (B03) — see
[tenancy.md](../tenancy.md) and [programme-ops.md](../programme-ops.md)'s
"Tenant-owned source connections" section for the mechanics.

| Item | Status | What exists now |
|---|---|---|
| B02 (isolate all ProgrammeOps data) | **Done** | Every `ProgrammeOps_*` table carries `tenantId` (nullable, backfilled to the default tenant); `IProgrammeRepository`/`IIdentityResolutionRepository`/`ISyncRunRepository`/`IAuditLogRepository`/the two raw-payload repositories filter reads and stamp writes by it; both Gold services (`ProgrammeOverviewQueryService`, `ReportingQueryService`) and every controller in the area (`StaffReportingController`, `StaffIdentityController`, `StaffProgrammeOverviewController`, `StaffPortalController`, `StaffAdminController`'s audit page, `StaffContractController`'s Programme Ops reads) resolve the caller's tenant and forbid if unresolved; per-key mutations (baseline lock, change-request decide, stakeholder remove, alert acknowledge, identity link/unlink) scope by tenant so a foreign key matches nothing. `StaffTenantAdminController` (Platform Admin) keeps one deliberate cross-tenant read for its support-signal panel (`ISyncStatusQueryService.GetPublicationStatesAcrossTenantsAsync`) |
| B03 (tenant connection registry + connection-qualified identity) | **Done, structural half only** | `ProgrammeOps_SourceConnection` (auto-provisioned on a tenant's first sync of a source), Silver upserts and the identity-link uniqueness indexes keyed on `(TenantId, ExternalSource, ExternalId/externalUserId/email)` so identical upstream ids in two tenants never collide, `ExternalIdentityLink.ConnectionKey` as provenance, sync lease/run state keyed on `(tenantId, source)` so two tenants' syncs never serialize against each other. **Not built, deliberately**: per-tenant source *credentials* — every tenant still authenticates through the one deployment-wide `ClickUp:ApiToken`/`HubPlanner:ApiKey`; real isolation needs a secret-store decision plus dynamic per-request `HttpClient`/auth resolution (today's `AddHttpClient<T,T>` in `ProgrammeOperationsComposer` builds one client, with one auth header, for the process's lifetime) — a separate, larger piece of work than this increment, scoped out by explicit user decision rather than silently dropped |

Known, accepted gaps left by this increment: `SyncRunGuard`'s in-process
latch is still keyed by source alone (not tenant), so two different
tenants triggering the same source on one process at the exact same
instant can get a spurious "already running" retry — narrow and
self-resolving, not a cross-tenant leak (see its doc comment in
`SyncRunCoordinator.cs`). Contract Ops itself still has no `TenantId`
column (R12's remaining half); `ContractCommercialService`/
`InvoiceGenerationService`/`PortfolioMarginService` now thread the caller's
resolved tenant into their `IProgrammeRepository` calls so they don't mix
another tenant's programme data in, but a `Contract` row itself isn't
tenant-owned yet. Member Groups (roles) remain global (R13) — an Admin in
one tenant is still indistinguishable, at the role level, from an Admin in
another.
