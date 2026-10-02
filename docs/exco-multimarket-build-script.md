# EXCO and multi-market pivot: coding-agent build script

Status: staged implementation instructions, not an executable deployment script or a claim that every phase is complete. Use this document as the coding-agent task for a sequence of reviewable changes. The market assessment is in `docs/archive/exco-market-pivot-2026-09-22.md`.

Implementation started: [increment 1](executive-review-increment-1.md) delivers opt-in operational snapshots and versioned market settings. [Increment 2](executive-review-increment-2.md) adds a versioned evidence-review/decision/outcome journal and dedicated write permissions, with SQL and HTTP tests. This is partial progress on phases 1, 3, 4 and 5. Formal whole-pack approval, commercial allocation, customer-data lifecycle controls and international launch gates below remain outstanding.

## Objective

Extend ProgrammePulse so a tenant can produce a reviewed, reproducible executive decision pack from reconciled delivery/commercial evidence, with independent market and language settings. Preserve existing .NET 10, Umbraco 18, NPoco, composer, repository, authorisation and resx patterns. Deliver vertical slices with tests and updated evidence claims.

## Phase 0: establish the current baseline

Read applicable AGENTS.md instructions, CLAUDE.md, docs/programme-ops.md, docs/tenancy.md, docs/contract-ops.md, docs/security-assurance.md, docs/data-governance.md and docs/commercial/claim-register.md. Inspect git status and preserve existing edits. Verify actual implementations before relying on older assessments.

Inspect Program.cs, LanguageController, TenantRepository, StaffTenantAdminController, ReportingQueryService, ReportingSnapshotService, ContractCommercialService, PortfolioMarginService, ContestedCostAggregator and current governance/RAID models. Reuse existing workflows where possible.

Run the baseline build and relevant current tests; distinguish pre-existing failures from changes. SQL-backed integration tests must use the existing disposable test database mechanism. Never point a migration or test at a customer database. Record commands and results, and identify which tests did not execute.

## Phase 1: build the executive decision loop

Deliver a tenant-scoped executive page using existing Staff Ops layout conventions. Show material exceptions, changes since the previous reviewed pack, source freshness, coverage, decisions due and action follow-ups. Use a compact table/list with filters and drill-through; support empty, incomplete, stale, disputed and no-permission states.

Define a versioned EvidenceItem contract: tenant, portfolio/project/contract scope, period, source and source-account identity, source record references, source/extraction/publication timestamps, calculation version, numerator/denominator where applicable, currency/unit, exclusions, observed/assumed classification, reviewer and review status. Preserve the actual supporting values required to reproduce the pack; source links alone can change or disappear.

Extend or reuse governance entities for DecisionRequest and DecisionOutcome: evidence references, materiality basis, options, recommendation, accountable owner, due date, decision, rationale, action owner, follow-up date and measured outcome. Workflow: Draft -> Reviewed -> Decided -> Closed; support explicit dismissal, reopening and audit history. Reject invalid transitions and foreign-tenant references. An unresolved or disputed item cannot be silently marked verified.

Create an ExecutivePack snapshot with pack version, as-of instant, reporting period, metric-definition versions, market-setting version and evidence references. Existing ReportingSnapshot stores cost-free aggregate totals; do not put sensitive fields into that existing public contract. Use separate authorised projections and storage as needed. Finalisation must capture a transactionally consistent dataset or clearly bounded source versions; block finalisation when required evidence is partial. Preserve failed/stale-source warnings.

Finalised packs are immutable; corrections create a superseding version. Audit actor/time and relationship between versions. Define retention and erasure treatment for embedded personal data under existing governance rules; immutability must not silently create indefinite personal-data retention.

Acceptance: a seeded exception can be reviewed, decided and closed; the previous pack remains reproducible after source data changes; unknown values stay unknown; rejected transitions and cross-tenant references fail; stale/partial evidence cannot appear verified.

## Phase 2: fix commercial attribution before forecasting

Inspect current contract attribution and retain contested-margin suppression. Add explicit tenant-scoped allocation of source effort to contracts, using stable source/account/entry keys and effective dates or versioned allocations. Choose work-item links only where they unambiguously cover the actual time entries; otherwise use entry allocations.

Allow a reviewed split with decimal precision rules. Total allocated quantity cannot exceed the source quantity, including concurrent edits; enforce transaction/concurrency protections. Under-allocation remains visible. Reject cross-tenant/customer/contract references. Source corrections invalidate or flag affected allocations and create a review trail; never silently rewrite a finalised pack.

Calculate cost using the relevant effective rate and source currency. Preserve missing rates, undated effort, currency mismatch and excluded contracts. Reconcile allocated plus unallocated effort to the in-scope source quantity. Keep headroom, incurred margin, forecast margin and revenue-recognition concepts separate. Remaining effort is a reviewed assumption, not original estimate minus actual hours.

Acceptance: overlapping contracts, split allocation, repeated imports, changed source entries, missing dates/rates, concurrent edits and currency mismatch. Existing contested-cost and portfolio tests must remain valid or be deliberately updated with an explained contract change. Demonstrate one finance-reviewable reconciliation from source entries to pack figures.

## Phase 3: introduce independent market settings

Add a tenant-scoped settings record with an additive NPoco migration, following existing migration plans. Proposed fields:

| Setting | Initial behaviour |
|---|---|
| MarketCode | GB default for existing tenants; commercial default only, never authorisation |
| DefaultUiCulture / enabled UI cultures | en-GB; preserve existing cy-GB availability with honest coverage labels |
| FormatCulture | en-GB default; independent of language |
| ReportingCurrency | GBP default; never relabel historical source amounts |
| TimeZoneId | Europe/London candidate default, verified against existing date semantics before backfill |
| FiscalYearStartMonth | Explicit tenant choice; proposed default 1 requires migration review, not a UK tax-year assumption |
| WorkingCalendarId | Reference to versioned hours/weekend/holiday rules; do not assume one national calendar fits every employee |
| SettingsVersion / effective time | Audit changes; freeze the applicable version in each finalised pack |

Add optional per-user UI and format preferences using the existing identity/profile ownership pattern. Validate allowlists server-side. Tenant configuration must never grant a feature or a data permission. Configure market presets as editable defaults, with validation, rather than large country conditionals in services.

Persist UTC instants and genuine date-only business dates distinctly. Compute report boundaries in the selected reporting zone and convert to UTC using half-open intervals. Test missing/ambiguous DST times and Windows/Linux time-zone support. Background jobs and exports resolve culture explicitly rather than inheriting a request or machine default.

Use additive migration defaults that preserve existing behaviour; audit any dates previously interpreted without a clear zone. Re-running startup must not overwrite tenant choices. Rollback disables the feature without deleting settings or snapshots; document backup/restore and schema compatibility.

Acceptance: two tenants can use different calendars/formats concurrently; changing locale never changes amounts or access; defaults do not modify historical reports; invalid settings fail predictably; DST and fiscal-year boundaries produce expected periods.

## Phase 4: finish localisation of the selected journey

Extend IStringLocalizer<SharedResource> and the existing resource files. Start with executive page, drill-through, settings, validation, language selector and exported pack. Inventory untranslated pages; supported-language claims apply only to completed journeys.

Use a shared supported-culture registry for request options and LanguageController. Reject unsupported or malformed cultures before constructing culture objects or cookies; retain local-only return URLs and define a sensible area-aware fallback. Explicitly define precedence: authorised saved user preference, supported selection cookie, tenant default, platform default. Any browser/query-string provider must have a documented lower-priority or disabled role. Set Culture and UICulture independently where formatting differs from language. Verify pipeline order against Umbraco authentication and tenant resolution; use a scoped resolver without trusting client-supplied tenant IDs.

Localise UI, errors, email templates if introduced, and exports. Use stable resource keys and placeholder parity checks. Preserve user/source text in its original language unless a separately approved translation feature exists. Do not translate status identifiers, API values or numeric storage formats. Use invariant machine serialisation and culture-aware presentation. Protect spreadsheet exports from formula injection and preserve explicit currency codes.

Use pseudolocalisation for overflow, then professional review of Welsh financial/decision terminology. A resource key existing in both files does not prove a correct translation. Additional languages stay disabled until the entire promised journey and support process are reviewed.

Acceptance: unsupported culture and external-return-URL cases, resource placeholder parity, decimal/date input round trips, independent UI/format preferences, concurrent users, and exported pack labels. Run rendered-page/browser checks at desktop and mobile widths; dotnet build alone does not establish Razor correctness in this repository.

## Phase 5: enforce executive permissions and export boundaries

Implement tenant-qualified capabilities for executive operational read, financial read, decision management, pack publication and market administration using the established authorisation service. Do not infer permission from a job-title string. An executive reader must not need Admin or gain rates, account administration or other tenants' data.

Apply the same checks to repositories/services, HTML, JSON, exports, snapshot retrieval and cached output. Financial data must not be fetched on operational-only paths. Cache keys include tenant, relevant access scope, culture and pack/settings version. Foreign-tenant lookups return the established non-disclosing response.

Acceptance: persona matrix for permitted and denied routes and direct IDs; financial fields absent structurally on operational paths; revoked permissions prevent pack downloads; cache tests show no user/tenant leakage. Audit settings changes, publication and decisions.

## Phase 6: pilot one additional market

Enable one English-language international pilot after the buyer and operating model are selected. Ireland/en-IE/EUR/Europe-Dublin is a candidate configuration, not a declared supported market. Culture, currency and time zone remain independent.

Initially present separate currency totals with coverage/exclusions. Do not add unlike currencies. If consolidated reporting is required, add a versioned FX policy: reporting currency, provider or finance-approved rates, rate date, spot/average/closing purpose, rounding, missing-rate behaviour and original values. Persist the applied rates and policy with the pack. Restatement creates a new version. A missing rate produces an incomplete result, not zero or an assumed 1:1 conversion.

LegalEntity is a business reporting scope under explicit tenant permissions, not a synonym for Tenant. Defer group consolidation until entity mappings, intercompany treatment and reconciliation rules are agreed. Never relax tenant isolation to create a group view. Hosting region is deployment metadata backed by actual infrastructure, not a UI selector that promises data residency.

Before launch, document the pilot's contractual terms, privacy/transfer arrangements, support hours, calendar ownership and relevant billing/tax boundary. Keep jurisdiction-specific invoicing or payroll outside the supported claim unless separately implemented and reviewed.

Acceptance: customer signs off a representative pack, currency policy and period boundaries; operational owner demonstrates restore and support; record measured setup/correction/support time and renewal intent. Software tests cannot satisfy this commercial gate.

## Phase 7: bounded scenarios, only after evidence is reliable

Add saved what-if scenarios only after phases 1-5 and customer validation: a reviewed change in remaining effort, dates or staffing produces an explicitly assumed outcome alongside the baseline. Persist assumptions and calculation versions. Do not write scenario values back to source systems or present deterministic results as validated predictions. Test sensitivity, unavailable inputs and reproducibility. Defer AI summaries until every generated statement can cite permitted evidence and a reviewer can correct it.

## Verification and handoff

For each slice, report changed files, actual behaviour, migration effects, tests executed, evidence gaps and rollback method. Run focused domain/controller tests and SQL-backed integration/persona checks appropriate to schema and permissions. Then run the repository build and required CI gates. Do not substitute compilation for rendered Razor validation or skipped tests for passing tests.

Update the claim register and supporting documentation only to the evidence level achieved. A completed build is not a supported market, accepted translation, customer outcome or production release. Do not deploy or modify customer data as part of this implementation brief.

Technical reference: [ASP.NET Core localisation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/localization?view=aspnetcore-10.0). Check the installed framework and current official guidance during implementation.
