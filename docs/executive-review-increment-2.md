# Executive review: increment 2

Implemented 22 September 2026: an opt-in, tenant-scoped decision journal over the operational packs from [increment 1](executive-review-increment-1.md). This completes another part of the [build plan](exco-multimarket-build-script.md), not the whole EXCO or international launch programme.

## Workflow

From an operational pack, a Team Lead or tenant Admin can create a decision with a selected evidence record, materiality, options, recommendation, active accountable owner and date-only deadline. The queue shows up to 100 current decisions ordered by decision/follow-up date, with a status filter. Detail and JSON history retain every version.

The lifecycle is `Draft -> Reviewed -> Decided -> Closed`. Accepting evidence records a reviewer, time and note. A decision requires recorded rationale, an active action owner and follow-up date. Closing requires an outcome. Draft/reviewed records may be disputed, revised or dismissed; closed/dismissed records can be reopened. Revisions and reopening clear the current acceptance/decision/outcome, but never delete the previous versions. The editor can select another saved pack from the same source to refresh evidence. Loading a pack is a GET and does not save unsent edits.

Both evidence acceptance and decision recording check the saved publication's age against the stricter of its captured threshold and the current configured threshold. Future-dated, expired, absent or unmapped evidence cannot pass those checks. This is a bounded freshness check on a saved publication, not a new call to the live source. Unknown source due dates remain unknown. An explicit subsequent acceptance can resolve a dispute, with the disputed version retained in the history.

The source pack remains immutable and labelled as not independently reviewed. Acceptance applies to the decision's selected evidence record, not the entire pack. The same authorised person may create, review and decide: this is **not independent two-person assurance or formal pack approval**. Outcomes are manually recorded statements, not automatically measured benefits. Existing RAID and change-request workflows are unchanged.

## Permissions and routes

All entry points require an active staff profile in the resolved permitted tenant. The feature remains off by default through `ExecutiveReview:Enabled`. No form accepts an actor or tenant ID.

| Route | Capability, in addition to tenant access |
|---|---|
| `GET /staffops/executive/decisions` | `ViewDeliveryReporting` |
| `GET /staffops/executive/decisions/{key}` and `/download` | `ViewDeliveryReporting` |
| `GET /staffops/executive/decisions/new?packKey=...` and `/{key}/edit` | `ManageExecutiveDecisions`, `ViewDeliveryReporting` |
| `POST /staffops/executive/decisions/create` and `/{key}/revise` | `ManageExecutiveDecisions`, `ViewDeliveryReporting`, antiforgery |
| `POST /staffops/executive/decisions/{key}/act` | `ReviewExecutiveEvidence` for acceptance/dispute; otherwise `ManageExecutiveDecisions`; also reporting access and antiforgery |

The two new capabilities are currently granted only to Team Lead and tenant Admin. Board, Analyst and Holiday Approver remain operational readers; ordinary Staff and Platform Admin alone have no access. Foreign decision/pack IDs return 404 after access checks. Downloads recheck access and exclude the staff directory. All responses are no-store. Free text is HTML encoded on display; there is no HTML or spreadsheet export in this increment.

English/Welsh resources cover the new decision pages and domain errors. User-authored text is not translated. Dates are business `DateOnly` values with invariant machine serialisation; audit timestamps are UTC. Professional Welsh terminology review and desktop/mobile visual checks remain release gates.

## Storage and concurrency

Additive Tenancy migration `2026-09-tenancy-04` creates `ExecutiveReview_DecisionVersion`, with a unique `(tenantId, decisionKey, version)` index. Every write appends a full decision revision and actor/time metadata. Source records and packs are untouched. There is no application endpoint for rewriting journal history.

Serializable transactions and the existing parent-tenant lock serialise writers; the submitted expected version is checked inside that transaction. A stale submission returns 409 without appending a revision. Invalid transitions, inactive/foreign owners and mismatched evidence fail without a write. Locking is deliberately conservative for the pilot; high-volume throughput has not been established.

Disable `ExecutiveReview:Enabled` to roll back exposure without deleting tables or history. Keep the additive migration state. Backup/restore rehearsal remains required before customer deployment; no customer database is migrated by the verification commands below.

## Personal data and launch gates

The decision journal adds owner staff keys, actor member IDs and free-text materiality, recommendations, rationale, review notes and outcomes. Those fields can contain personal or commercial information even though the operational source projection does not import costs or task titles. All reporting readers can see the submitted text; do not treat this as a restricted financial workspace. The subsequent [data lifecycle increment](executive-data-lifecycle.md) supersedes the original implementation gaps below with export/erasure participation and governed whole-journal removal, pack withdrawal, retention and executive-module purge. Normal revisions remain append-only; governed removal is an explicit exception.

Display names are resolved from current tenant staff records, not frozen in the journal. Direct profile anonymisation alone does not remove journal history; use the registered GDPR service. Whole-journal redaction is implemented, not selective free-text editing or automatic discovery of unstructured mentions. Downloaded-copy/backup handling remains an operating responsibility. Keep the feature disabled for identifiable customer data until the operating policy and remaining launch gates are approved. Application history is not tamper-proof storage or a licence for indefinite retention.

## Verification

Coverage is in `ProgrammePulse.Tests/ExecutiveReview` and the two `Integration/Executive*IntegrationTests.cs` files (shared test fixture). It checks lifecycle rules, strict freshness at both review and decision, unmapped/unknown evidence, required fields, capability boundaries, resource parity, SQL history, foreign references, inactive owners and concurrent edits. HTTP tests exercise create/review/decide/close/reopen/revise, English/Welsh rendering, pack refresh, HTML encoding, invalid/stale forms, antiforgery, reader denials and download revocation.

Use the disposable integration database, with SQL tests required:

```powershell
$env:PP_TEST_SQL_CONNECTION = 'Server=(localdb)\GardnerDB;Database=UmbracoBase_ExecutiveReview_IntegrationTests;Integrated Security=true;TrustServerCertificate=true;'
$env:PP_REQUIRE_SQL_TESTS = '1'
dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --configuration Release --artifacts-path artifacts/executive-review-build --filter FullyQualifiedName~ExecutiveReview
dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --configuration Release --artifacts-path artifacts/executive-review-build
dotnet build ProgrammePulse.csproj --configuration Release --artifacts-path artifacts/executive-review-razor -p:CheckRazorViews=true
```

The separate output paths avoid locked Debug binaries from other local app processes. Normal test/build output remains separate from the Razor-check output.

### Handoff results, 22 September 2026

- Full isolated regression suite: **685 passed, zero failed, zero skipped**, with SQL tests required. This includes all **57 executive-review tests**, 26 added in this increment. TRX: `artifacts/executive-review-tests/increment-2-full-suite.trx`.
- Final Razor compilation passed with zero errors and warnings. An earlier resource rebuild reported the two pre-existing duplicate `RAID Register` warnings. The new `Review.*` and `Decision.*` resources have unique, nonempty keys in both languages; tests also check literal page keys and lifecycle labels.
- Real HTTP journeys ran against the test host and disposable LocalDB database. Browser screenshots, visual layout, and browser-side JavaScript behaviour have not been verified. No persistent preview server was launched; the previous preview launch was blocked by policy. No production deployment or customer migration was performed.
- Keep `ExecutiveReview:Enabled=false` outside controlled synthetic-data evaluation. The next release-critical work is the personal-data lifecycle gate, not enabling further markets.

## Still outstanding

1. Acceptance of the implemented customer-data lifecycle controls and operating policy, including manual free-text review, configured retention, downloaded copies and backups, before an identifiable-data pilot.
2. Formal approval of whole executive packs, material-exception selection, reviewed-pack comparisons and stronger source/account provenance.
3. Commercial source-effort allocation, reconciliation, financial permissions and currency-safe projections before forecasting.
4. Versioned working calendars, saved user preferences, broader localisation and professional translation review.
5. Browser/mobile visual validation, customer reconciliation, deployment/restore assurance and acceptance of one additional market.
6. FX policy, group consolidation and bounded scenarios only after the evidence and pilot justify them.
