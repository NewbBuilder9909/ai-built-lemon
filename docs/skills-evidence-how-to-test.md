# Testing the skills and evidence module by hand

21 September 2026. Branch `feature/skills-evidence-slice1`.

Written for whoever picks this branch up to try it. The automated suite
covers the logic; this is for clicking through it, and for knowing which
parts you *cannot* exercise yet and why.

## Start here

```powershell
dotnet build
dotnet test                                    # 927 tests, ~30s
dotnet run                                     # https://localhost:44329
```

Four migration plans run on startup. Confirm in the log:

```
Starting 'SkillsEvidence'...  At 2026-09-skillsevidence-04
Starting 'ServiceOps'...      At 2026-09-serviceops-01
```

Sign in at `/staffops/account/login` as an Admin. Admin requires TOTP —
the existing MFA flow, unchanged by this work.

## What works with nothing connected

Everything in this column needs no vendor account, no credentials and no
configuration. It is the part of the module that can be demonstrated
today.

| Do this | Where | What to look for |
|---|---|---|
| Add a few skills | `/staffops/skills/taxonomy` | Keys normalise — type "C Sharp" and "csharp" and see they are different skills, deliberately |
| Declare one about yourself | `/staffops/skills` | Lands as Submitted, not validated. The rubric is on the page |
| Validate it as someone else | `/staffops/skills/review` | Requires a rationale. Try to validate your own — it is refused |
| Challenge the level | `/staffops/skills` | Returns to the queue; the history shows every earlier version |
| Look at coverage | `/staffops/skills/coverage` | Nobody is named. Single-maintainer warnings are about the organisation |
| Map a component and an owner | `/staffops/skills/continuity` | Owner declared, never inferred. Marks itself reviewed today |
| Approve cover, raise an action, close it | same page | Closing demands an outcome note |
| Record the processing decision | `/staffops/skills/continuity/processing` | Note the three kinds of readiness answer, and that consent is not offered |
| Run a GDPR export on yourself | `/staffops/admin/{staffKey}/export` | Your assertions, with history, in the `LinkedRecords` section |
| Read the audit trail | `/staffops/admin/audit` | Four sources merged; every validation and decision is there |

## What needs a vendor, and what it would take

Nothing below has ever run against a live account. The application says
so itself — `IsUnverifiedReplay` is true and the affected pages carry a
banner — so you will see those banners rather than data.

**GitHub evidence** (`/staffops/skills/evidence`). Needs a GitHub App
registered and these set, per environment:

```json
"SkillsEvidence": {
  "GitHubAppSlug": "...",
  "GitHubClientId": "...",
  "GitHubClientSecret": "...",
  "AllowedEnterpriseHosts": []
}
```

With them unset the connect form tells you the app is not configured
rather than half-working. Even configured, a sync is **blocked** until a
lawful basis, worker notice and DPIA decision are recorded at
`/staffops/skills/continuity/processing` — that gate is deliberate, and
worth seeing: try syncing before recording one.

**Freshdesk service health** (`/staffops/service/desks`). Needs a
Freshdesk account and an API key for an agent with ticket read access.
No app registration required, so this is the cheaper of the two to try
against a real tenant.

```json
"ServiceOps": { "RawPayloadRetentionDays": 30, "DefaultReportingWindowDays": 90 }
```

The field mapping is the thing to check first on a real desk: which
field carries the product/component tag, and any custom status codes.
Both are convention-based today — see
[`skills-evidence-validation-protocol.md`](skills-evidence-validation-protocol.md).

**Plan gating.** Repository evidence is Enterprise-only; service health
is Professional upwards. A tenant on Starter sees neither nav item. The
continuity plan is not gated at all.

## Exercising the connectors without a vendor

The fixtures are the intended route, not a workaround. Every failure
mode that matters — partial pages, rate limits, permission loss,
force-push, squash merges, reopens, deletions, unknown tags — is
expressible as data and none can be produced on demand against a real
account:

```powershell
dotnet test --filter "FullyQualifiedName~EvidenceIngestionTests"
dotnet test --filter "FullyQualifiedName~DeskIngestionTests"
dotnet test --filter "FullyQualifiedName~SupportCausalityTests"
```

A fixture replay is not a live vendor validation, and the product does
not pretend otherwise.

## Things worth trying to break

These are the behaviours most worth poking at, because each is a
deliberate decision rather than a default:

- **Validate your own skill.** Refused — a domain rule, below the
  authorization layer, so holding the reviewer capability is not enough.
- **Open the review queue in two tabs and decide the same item twice.**
  The second is refused as a stale row rather than overwriting.
- **Press sync twice quickly.** The second is refused. Single process
  only — behind two nodes this would not hold.
- **Sign in as a Team Lead** and try `/staffops/skills/continuity/processing`.
  Forbidden: a delivery manager must not unblock collection of their own
  team's activity.
- **Sign in as an Analyst.** Service health yes, skills coverage no —
  support demand is about a product, skills coverage is derived from
  personal records.
- **Erase a staff member who owns a component.** The component stays and
  becomes *unowned*, sorted to the top of the risk view. Deleting it
  would resolve the exposure by hiding it.

## Database

LocalDB `(localdb)\GardnerDB`, database `UmbracoBase`, from
`appsettings.Development.json`. Integration tests use a separate
`UmbracoBase_IntegrationTests` and create it unattended.

To inspect what the migrations built:

```sql
SELECT name FROM sys.tables WHERE name LIKE 'SkillsEvidence%' OR name LIKE 'ServiceOps%';
SELECT name, filter_definition FROM sys.indexes WHERE name LIKE 'UX_SkillsEvidence%' OR name LIKE 'UX_ServiceOps%';
```

The filtered indexes are the interesting ones:
`UX_SkillsEvidence_StaffSkillAssertion_current` (one live assertion per
person per skill, unlimited history) and
`UX_SkillsEvidence_ProcessingDecision_live` (one live decision per
tenant, every superseded one kept).

## Known limits

Listed in full in [`data-governance.md`](data-governance.md) §8. The
ones you will notice while testing: syncs are manual with no scheduler,
Silver retention purges are manual, the new views are English-only, and
no connector has been validated against a live vendor or a design
partner.
