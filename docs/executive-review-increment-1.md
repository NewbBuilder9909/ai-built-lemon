# Executive review: increment 1

Implemented 22 September 2026 as the first controlled increment of `exco-multimarket-build-script.md`. This delivers versioned market settings and operational evidence snapshots. It does not complete the reviewed decision workflow or establish an internationally supported product.

## Enablement and routes

The feature is off by default in `appsettings.json`. Enable in a controlled environment with `ExecutiveReview:Enabled=true` (environment variable `ExecutiveReview__Enabled=true`). `ExecutiveReview:MaximumPublicationAgeHours` defaults to 48 and accepts 1-168. Invalid age configuration fails options validation at startup.

| Route | Existing capability used |
|---|---|
| `GET /staffops/market` | `ManageStaff` |
| `POST /staffops/market` | `ManageStaff` and antiforgery |
| `GET /staffops/executive` | `ViewDeliveryReporting` |
| `POST /staffops/executive/capture` | `CaptureTrend`, `ViewDeliveryReporting` and antiforgery |
| `GET /staffops/executive/{packKey}` | `ViewDeliveryReporting` |
| `GET /staffops/executive/{packKey}/download` | `ViewDeliveryReporting` |

Board/Analyst/Approver can read; Team Lead/Admin can capture; only tenant Admin can edit market defaults under the current role matrix. Platform Admin alone has no business-data access. Service entry points independently require an active staff profile and resolved permitted tenant; tenant IDs and actors are not accepted from forms. There is no new financial permission, role or grant in this increment. Future decision publication and financial packs require their own capability decisions.

## Settings behaviour

UI language (en-GB/cy-GB), number/date format (en-GB/cy-GB/en-IE), market (GB/IE), reporting currency preference (GBP/EUR/USD), zone (Europe/London, Europe/Dublin, Etc/UTC) and fiscal start month are independent allowlisted fields. GB/en-GB/GBP/Europe-London/January are version-zero defaults, not assertions about an organisation's tax year or historic report boundaries.

Saving creates an append-only version with the actor and UTC timestamp. A parent tenant lock serialises writers, and the form's expected version prevents lost updates. Reload is required after a 409 conflict. Startup never rewrites settings. No automatic currency conversion, local tax calculation, payroll calendar or invoicing behaviour is implied by these fields. The currency is currently a saved preference for subsequent financial work.

Only the new market and executive routes apply tenant culture defaults, after authentication and before model binding. A valid explicit culture cookie overrides those defaults. Language and format are selected independently; query-string and browser-header culture providers no longer override the cookie. Existing en-GB/cy-GB cookies remain supported. Saved per-user preferences and whole-application tenant defaults remain future work. Staff navigation carries the current format through a language switch.

The pack uses its captured format and zone when displaying evidence timestamps and its captured fiscal start for the financial-year label. Existing financial and capacity reports are not reinterpreted. The half-open UTC-period helper rejects ambiguous/missing local midnight and is tested for DST; period-based executive financial calculations have not yet been introduced.

## Evidence contract

A capture selects one registered work-producing source, not the whole portfolio or every connected system. It requires a successful completed latest publication within the configured age limit and an active connection with an account reference. Running, failed, future-dated, missing and stale publications fail validation. Empty evidence and missing source record IDs fail validation too.

The SQL capture transaction uses serializable isolation for publication, connection, market settings and work-item reads, then inserts the pack before releasing locks. It does not call a provider API. The existing incremental ingestion mechanism is unchanged. This transaction can block ingestion while a large source is read; pilot sizing and a future publication-version architecture remain necessary before large deployments.

The pack stores the source and current connection/account reference, publication run ID/time, capture time, calculation definition version, freshness threshold, full settings version and the exact selected work-item evidence. It stores totals as well as evidence so later calculation changes cannot silently recalculate history. Evidence omits names, task titles, descriptions, assignees, rates, costs and credentials. Keys, status, milestone flag and source-row update/due timestamps remain. Account-at-capture is not proof of each historical row's original account: current Silver keys do not establish that after an account switch.

Metrics are observed open, blocked, overdue, unmapped and missing-due-date counts. Done/cancelled items are excluded from open and overdue. Due dates are compared as stored UTC instants; all-day due-date interpretation remains source-dependent. Missing dates remain unknown. These are not financial exposure, remaining capacity, schedule forecasts or reviewed findings.

Packs have no update endpoint. A new capture links to the previous capture for that source; it does not claim to be a formally approved correction. History shows the latest 50 captures and a same-source open-count delta when the prior pack is in that window. Detail and JSON download read only the saved pack and recheck access. Responses are no-store. The displayed review status explicitly says the snapshot has not been independently reviewed.

## Schema and rollback

Tenancy migration `2026-09-tenancy-03` adds `ExecutiveReview_MarketVersion` and `ExecutiveReview_Pack`; it does not alter source or financial rows. Settings have a unique tenant/version index. Packs have a unique key and tenant/capture index. Rows record the creating member ID as the audit actor.

Rollback the feature by setting `ExecutiveReview:Enabled=false`; retain the additive tables and data. Do not roll the migration plan back or delete snapshot history as part of ordinary rollback. Restore uses the database backup process; a customer-environment backup/restore rehearsal remains a deployment gate.

## Data lifecycle

Update: the [executive data lifecycle increment](executive-data-lifecycle.md) now implements export/erasure participation, governed whole-record redaction/withdrawal, operator-triggered retention and executive-module purge. The following paragraphs describe the original increment-1 gaps; the linked document is the current implementation and operating-limit reference.

Pack source IDs and actor IDs are linkable metadata, not anonymous data. Include both new tables in tenant export and deletion inventories. Current automated staff GDPR participants do not yet export or erase these new actor/source links. Before using identifiable customer data, the operator must approve retention and implement or execute a documented export/redaction/purge procedure covering these records, backups and downloaded copies. The feature should remain disabled for that deployment until this is satisfied.

During controlled synthetic-data evaluation, an operator may purge the isolated test tenant's review rows under the existing test-data procedure. For a later lawful correction/erasure, preserve a minimal redaction event and withdraw affected packs rather than silently presenting a modified pack as its original version. There is no automated retention job or redaction endpoint in this increment; immutability means application write protection, not indefinite retention or tamper-proof storage.

## Verification

New coverage lives in `ProgrammePulse.Tests/ExecutiveReview` and `ProgrammePulse.Tests/Integration/ExecutiveReviewIntegrationTests.cs`. It covers settings validation, independent locale choices, UTC and fiscal boundaries, language allowlisting, publication gating, missing provenance, stored evidence, service access guards, SQL tenant isolation/concurrency, HTTP capture, Board denials with real antiforgery, export isolation, deactivation/suspension and English/Welsh page rendering. Resource key parity is checked; professional Welsh terminology review remains outstanding.

Run the scoped suite with:

```powershell
dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --configuration Release --filter FullyQualifiedName~ExecutiveReview
dotnet build ProgrammePulse.csproj --configuration Release -p:CheckRazorViews=true
```

The SQL tests use the existing disposable integration database. Do not enable the skip flag and report those tests as SQL evidence. Use a normal build after the Razor check before publishing. Final run results and any environment limitations are recorded in the implementation handoff.

### Implementation handoff, 22 September 2026

- Baseline before this increment: 422 non-SQL and 182 integration/persona tests passed.
- Dedicated increment suite: 31 tests passed, zero skipped, including actual SQL and HTTP rendering. Result: `artifacts/executive-review-tests/executive-review.trx`.
- Final Razor check compiled successfully. It reported two existing duplicate `RAID Register` resource warnings, unrelated to the new `Review.*` keys.
- Final isolated full suite: 654 passed, zero failures, zero skipped, including the 31 new review tests. Result: `artifacts/executive-review-tests/full-suite.trx`. Earlier runs overlapped in-progress delivery-load capability mappings and shared authentication fixtures; an intermediate run also aborted. The final run completed after the mappings were present, using isolated build output and SQL data.
- Dedicated verification used build output under `artifacts/executive-review-build` and the separate local database `UmbracoBase_ExecutiveReview_IntegrationTests` on `(localdb)\GardnerDB`, with SQL tests required. It avoided stopping existing app processes or overwriting their locked Debug output.
- Automatic approval review rejected both the proposed preview setup and a narrower preview-server launch with only `blocked by policy` given as the reason. No browser screenshot check or persistent local preview was completed. HTTP render tests and Razor compilation did complete.

For an isolated repeat on this workstation:

```powershell
$env:PP_TEST_SQL_CONNECTION = 'Server=(localdb)\GardnerDB;Database=UmbracoBase_ExecutiveReview_IntegrationTests;Integrated Security=true;TrustServerCertificate=true;'
$env:PP_REQUIRE_SQL_TESTS = '1'
dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --configuration Release --artifacts-path artifacts/executive-review-build --filter FullyQualifiedName~ExecutiveReview
```

## Next increments

Progress update: [increment 2](executive-review-increment-2.md) now implements the decision lifecycle, review/dispute state, scoped ownership, follow-up outcomes and dedicated write capabilities. Formal whole-pack approval and the remaining gates below are not complete.

1. Reviewed evidence and decision lifecycle, scoped ownership, review/dispute status, immutable approved pack versions and follow-up outcomes.
2. Explicit contract allocation with concurrency and reconciliation, maintaining contested-margin suppression until allocation is defensible.
3. Dedicated executive capabilities, financial projections and export boundaries; versioned working calendars and user preferences.
4. Customer data lifecycle automation, professionally reviewed translations, deployment assurance and one paid international pilot.
5. Approved FX/group consolidation and bounded scenarios only when the evidence and buyer demand justify them.

The competitive assessment remains a hypothesis to validate through paid diagnostics and renewal evidence. No code test substitutes for those commercial gates.
