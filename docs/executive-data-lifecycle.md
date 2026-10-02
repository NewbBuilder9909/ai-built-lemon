# Executive data lifecycle: controlled-pilot foundation

This increment implements the first dependency in the September EXCO evolution plan. It adds customer-data controls before more source connectors or AI distribution. It does not implement Entra, Teams, finance, Azure DevOps, MCP or LinearB integrations, and does not enable executive reporting by default.

## Administrator workflow

`/staffops/executive/data` is linked from Staff Administration. An active tenant administrator with `ManageStaff` can preview and confirm an operation. Tenant and actor are resolved server-side. Every POST requires antiforgery; responses are no-store. The controls remain available when `ExecutiveReview:Enabled=false`, so disabling the reporting feature does not strand its data.

| Operation | Effect |
|---|---|
| Redact entire decision journal | Physically removes every version, including all free text and owner/actor references. This is whole-record removal, not selective editing or an anonymised replacement decision. |
| Withdraw pack | Removes the snapshot and every version of any decision that ever used it, including decisions subsequently refreshed to another pack. The preview includes those dependent journals. Other packs remain. |
| Apply retention | Removes journals whose **latest** revision is Closed or Dismissed and older than the UTC cutoff. Retains open decisions and every pack referenced by any retained revision. Removes old, unreferenced packs, obsolete market settings and old lifecycle events; preserves current market settings. |
| Purge tenant executive data | Removes this tenant's executive packs, decision versions, market settings and lifecycle events. Does not delete the tenant, staff, source data or other feature areas. Returns a receipt but stores no new executive audit event. |

Reasons are fixed codes, not another free-text store. The ordinary lifecycle audit records operation, target record key, actor, UTC time and counts; it never copies deleted text. Staff erasure clears an erased actor from this audit. Only administrators can read the lifecycle audit, limited to the latest 100 entries in the UI.

Preview is read-only. Its confirmation token binds the operation, tenant, settings, cutoff and complete executive data state. Apply re-reads under the same tenant lock as pack capture and decision writes. Changed state returns 409 with no deletion; missing or foreign targeted records return 404. Another preview is required after a conflict. Destructive confirmation is an explicit second POST, never a GET.

## Subject access and erasure

`ExecutiveDataParticipant` joins the existing `IStaffDataParticipant` pipeline independently of the executive feature flag:

- Export includes the entire journal when **any version** names the subject as owner, action owner or actor; also pack-capture metadata, authored market settings and lifecycle audit actions. Administrators must review third-party information before releasing an export to the subject.
- Erasure removes those entire journals. It clears capture/settings actor IDs and lifecycle audit actor IDs without changing cost-free operational pack evidence or current market preferences. It leaves a counts-only subject-erasure receipt with no subject identifier.
- `TransactionalGdprService` wraps all registered participant erasures and profile deactivation in one SQL transaction. A failing participant rolls back the operation. It also rejects a subject export for the wrong tenant before reaching the export pipeline.
- Free-text-only mentions in otherwise unrelated journals are **not automatically discovered**. Operators must locate and redact those journals. External source IDs can also be identifying: withdraw affected packs where required and correct the upstream source before capturing again.

Normal decision changes remain append-only. Governed deletion is now an explicit exception; do not describe the database as immutable or tamper-proof. Subject erasure may remove a still-open management decision: review dependencies and recreate an appropriate non-personal decision if needed, rather than silently retaining sensitive text.

## Configuration and operating limits

```json
"ExecutiveReview": {
  "Enabled": false,
  "MaximumPublicationAgeHours": 48,
  "DataLifecycle": { "RetentionDays": 0 }
}
```

`RetentionDays` is 0 (disabled) or 1-3650; invalid configuration fails startup. Agree a period before configuring it. This is an **operator-triggered** policy, not a scheduler, legal-hold system or automatic disposal guarantee. Changing configuration or crossing UTC midnight invalidates a retention preview. The current configuration is deployment-wide, not a tenant-specific policy editor.

The conservative pilot implementation reads a tenant's executive records to compute dependencies and the confirmation token; large-volume throughput is not established. Withdrawn pack keys may remain as previous-pack pointers in surviving immutable snapshots; those keys no longer resolve to downloadable content.

Downloaded files, backups, transaction logs, replicas and third-party systems are outside application deletion. Define their retention, restore/re-erasure procedure, access and incident responsibilities before identifiable-data use. Suspended/archived tenants remain blocked by the existing access filter; offboarding through this UI must precede suspension, or use a separately approved operator procedure. This feature does not bypass that filter for platform operators.

## Migration and rollback

Additive Tenancy migration `2026-09-tenancy-05` creates `ExecutiveReview_DataEvent`. Existing snapshot/journal shapes are unchanged. The erased capture/settings actor sentinel is 0; current settings project it as null. Disable executive reporting to roll back exposure, but retain the lifecycle code and additive schema so cleanup remains possible. Redaction and purge are irreversible through the application.

## Repeatable build gate

```powershell
$env:PP_TEST_SQL_CONNECTION = 'Server=(localdb)\GardnerDB;Database=UmbracoBase_ExecutiveData_20260923_IntegrationTests;Integrated Security=true;TrustServerCertificate=true;'
./scripts/test-executive-data-lifecycle.ps1 -FocusedOnly
./scripts/test-executive-data-lifecycle.ps1
```

The script rejects database names not ending in `_IntegrationTests`, requires SQL tests, collects coverage, checks native exit codes, restores environment variables and compiles Razor separately. It does not deploy, enable reporting, call vendors or migrate a customer database. Evidence is under ignored `artifacts/executive-data-lifecycle/`.

Coverage includes configuration bounds, terminal-state/cutoff retention rules, subject references, real SQL migration and deletion, previous-version dependencies, tenant isolation, stale confirmations, rollback, disabled-feature cleanup, HTTP authorization, antiforgery, revoked access and English/Welsh rendering. Translation approval, backup/restore rehearsal, live connector reconciliation, independent review policy and customer acceptance remain release gates.

### Verification record, 23 September 2026

- Branch: `feature/executive-data-lifecycle`; changes remain local and uncommitted. The pre-existing reporting/resource cleanup was preserved.
- Final full suite: **1,112 passed, 0 failed, 0 skipped**, including 26 added cases. SQL was required against `UmbracoBase_ExecutiveData_20260923_IntegrationTests`; the first focused run created the database and exercised migration startup.
- Coverage collected in `artifacts/executive-data-lifecycle/results/b32494b6-9738-460c-b8b8-804acd6e4d3e/coverage.cobertura.xml`; full results in `results/full-suite.trx` under the same artifact root.
- Separate Razor compilation: zero errors and zero warnings. `git diff --check` passed. The build script's non-test-database rejection was also exercised without opening SQL.
- An isolated preview-server launch was blocked by execution policy. Browser screenshots, responsive layout and browser-side JavaScript behaviour remain **unverified**; authenticated HTTP rendering and POST journeys did run. No server was started by this increment, and the existing app process was not stopped.
- No customer migration, vendor connection, deployment, merge or push was performed. Executive reporting and retention remain disabled in the checked-in defaults.

## Next increment

Complete Jira/Tempo reconciliation, starting with deleted/amended worklogs and source coverage, then commercial effort allocation. Only introduce finance or AI distribution once the numbers and retained evidence can be explained. Entra/Teams and other vendor connections still require customer-led scope, credentials and live acceptance; no new integration is claimed by this increment.
