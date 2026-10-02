# Data governance, retention and compliance baseline

The operating rules for personal and commercial data in this product:
what is held, for how long, who can see it, how a data-subject request is
served, and what the audit trail covers. It extends
[`gdpr.md`](gdpr.md) (which stays the reference for the export/erasure
mechanics) and [`tenancy.md`](tenancy.md) (tenant lifecycle). Written for
the platform operator and whoever signs the customer's data-processing
agreement; nothing here is legal advice — confirm the retention windows
against the organisation's own policy.

## 1. Data classification

Opt-in executive review adds versioned settings, actor IDs and linkable source-record metadata in `ExecutiveReview_MarketVersion` and `ExecutiveReview_Pack`. The [executive data lifecycle increment](executive-data-lifecycle.md) adds subject export/erasure participation and administrator preview/confirmation for retention, withdrawal and executive-module purge. Retention is operator-triggered and disabled until configured; policy approval and operating acceptance are still required.

[Increment 2](executive-review-increment-2.md#personal-data-and-launch-gates) adds `ExecutiveReview_DecisionVersion`: owner/action-owner staff keys, actor member IDs and free-text decision/review/outcome history. Reporting readers can see this text; it is not a financial-only workspace. The registered erasure participant now removes whole journals with structured subject references, including older versions. Free-text-only mentions need operator review and whole-journal redaction. `ExecutiveReview_DataEvent` records lifecycle actions without retaining removed text. Keep the opt-in feature disabled for identifiable customer data until the operating policy, third-party export review, backup handling and remaining launch gates are approved.

| Class | Where it lives | Examples | Who may read it |
|---|---|---|---|
| **Identity / credentials** | Umbraco Members (`umbracoMember*`), `StaffOps_MemberMfa` | email, password hash, TOTP secret, hashed recovery codes | Umbraco backoffice users; the app never displays a secret |
| **Staff personal data** | `StaffOps_Staff`, `StaffOps_Availability`, `StaffOps_LeaveRequest`, `StaffOps_WorkHoursHistory` | name, job title, team, absence, working pattern | Admin (own tenant); the member themself (own rows) |
| **Cost / commercial-sensitive** | `StaffOps_StaffRate`, `ContractOps_*`, `ProgrammeOps_Programme.Budget*` | hourly cost, contract value, invoices, margin | Admin only — enforced structurally (see `CLAUDE.md`, "Cost/rate data isolation") |
| **Suggestions about people** | `SkillsEvidence_Suggestion` (rows with a `subjectStaffKey`) | a proposal that a person be tagged with a skill, its rationale and citations | The subject (in their own GDPR export); Team Lead and Admin via `ViewStaffSkillEvidence`. Deleted with the subject on erasure — including dismissed ones, which record that the system inferred something and a person disagreed |
| **Staff competence** | `SkillsEvidence_StaffSkillAssertion` | declared and reviewed proficiency per skill, the staff member's own evidence note, the reviewer's rationale | The subject themself (`ViewOwnSkills`); Team Lead and Admin for others (`ViewStaffSkillEvidence`). The *aggregate* is a separate, wider grant (`ViewTeamSkillCoverage`) against a type that structurally cannot name a person |
| **Skill taxonomy** | `SkillsEvidence_SkillDefinition` | the tenant's own skill vocabulary | Not personal data. Readable by anyone who can declare a skill; changed only by Admin (`ManageSkillTaxonomy`) |
| **Engineering evidence** | `SkillsEvidence_EngineeringEvidence`, `_ActorLink`, `_UnmappedActor` | commit/PR/review metadata, actor login and email, source links, language hints from changed paths, approved account→person mappings | The subject (in their own GDPR export); Team Lead and Admin via `ViewStaffSkillEvidence`. **No source code, diffs or file contents** — metadata and links only |
| **Evidence source credentials** | `SkillsEvidence_Connection.protectedCredentialJson` | a tenant's read-only GitHub App token | Nobody via the UI. Encrypted with Data Protection under purpose `ProgrammePulse.SkillsEvidence.Connection.Credential.v1`. **No deployment-wide fallback** — unlike ClickUp/Hub Planner, an unreadable credential stops the run |
| **Evidence Bronze captures** | `SkillsEvidence_RawPayload` | verbatim provider JSON for replay | Nobody via the UI; database access only |
| **Support case metadata** | `ServiceOps_SupportCaseFact`, `_SupportCodeLink` | ticket ids, timestamps, status, priority, approved component tags, reopen and deletion state, source links, reviewed root-cause findings | `ViewServiceHealth` (Analyst, Board, Team Lead, Admin) for the component view. **No ticket body, attachments, requester name or contact details** — the record has nowhere to hold them |
| **Support participation** | `ServiceOps_CaseParticipant`, `_AgentLink` | who resolved or reviewed which case, and the approved agent-account mappings | The subject (in their own GDPR export); Team Lead and Admin. Attributed only through an approved `DeskAgentLink` |
| **Desk credentials** | `ServiceOps_DeskConnection.protectedCredentialJson` | a tenant's read-only Freshdesk API key | Nobody via the UI. Encrypted under purpose `ProgrammePulse.ServiceOps.DeskConnection.Credential.v1`. **No deployment-wide fallback** |
| **Desk Bronze captures** | `ServiceOps_RawPayload` | verbatim ticket JSON for replay | Nobody via the UI; database access only |
| **Operational / work data** | `ProgrammeOps_*` (Silver + Gold) | tasks, time entries, RAID, alerts | Team Lead or above (no cost fields) |
| **Bronze captures** | `ProgrammeOps_RawClickUpPayload`, `ProgrammeOps_RawHubPlannerPayload` | verbatim upstream JSON (may embed upstream user emails) | Nobody via the UI; database access only |
| **Identity resolution** | `ProgrammeOps_ExternalIdentityLink`, `ProgrammeOps_UnresolvedIdentity` | source-tool user ids, upstream emails and display names, linked (or not yet linked) to a StaffKey | Admin (`/staffops/programme/identities`); links are in the subject's GDPR export and erased with them |
| **Sync operations** | `ProgrammeOps_SyncRun`, `ProgrammeOps_SyncLease` | run status, stage, error text, triggering member id, instance id | Team Lead or above (Programme Overview header), Platform Admin (console) |
| **Audit trail** | `StaffOps_AuditLog`, `ProgrammeOps_AuditLog`, `BrandingOps_AuditLog`, `ContractOps_AuditLog`, `SkillsEvidence_AuditLog` | who did what, when | Admin (`/staffops/admin/audit`; the SkillsEvidence table is not yet merged into that page — see gaps) |
| **Tenant records** | `Tenancy_Tenant` | organisation name, plan, status | Platform Admin |

## 2. Lifecycles

### Tenant (organisation)

`Tenancy_Tenant.status` — `Trial → Active → Suspended → Archived`, managed
from `/staffops/platform/tenants` by a **Platform Admin** (not a tenant's
own Admin). Access consequence of each state is decided in one place,
`Services/Tenancy/TenantAccessPolicy`, and enforced on every `/staffops`
request by `TenantAccessFilter`:

| Status | Members can sign in | Members can use the product | Data |
|---|---|---|---|
| Trial (before `trialEndsAtUtc`) | yes | yes, per plan | live |
| Trial (expired) | yes | **no** — 403 "trial has ended" | live |
| Active | yes | yes, per plan | live |
| Suspended | yes | **no** — 403 "account is suspended" | retained, untouched; reversible |
| Archived | yes | **no** — 403 "account has been archived" | retained for the retention window below, then purged; **not reversible from the UI** |

Every transition is written to `StaffOps_AuditLog` (`entityType =
"Tenant"`, actions `TenantCreated`, `TenantStatusChanged`,
`TenantPlanChanged`). A Platform Admin cannot suspend or archive the tenant
their own account belongs to (they would lock themselves out).

**Deleting a customer's delivery data on request** (the end of a paid
diagnostic, see `docs/commercial/diagnostic-data-handling.md`): once the
tenant is Suspended or Archived, a Platform Admin opens "Delete delivery
data" on the tenant console, sees the row count per table, types the
tenant's short code and confirms. `DeliveryDataPurgeService` deletes the
tenant's rows from every table in `DeliveryDataTables` (every
`ProgrammeOps_*` table; a test fails the build if one is added without
being listed) in one transaction. The counts are written to
`StaffOps_AuditLog` as `DeliveryDataPurged`, which the purge doesn't touch,
so the record of the deletion outlives the data. It doesn't touch staff
profiles or members (use per-person GDPR erasure), the tenant row, or other
feature areas' tables (the executive-review module has its own governed
purge, `docs/executive-data-lifecycle.md`). Backups keep the data until
they expire.

### User (staff member)

1. **Onboard** — `/staffops/admin/create` creates the Member + `StaffOps_Staff`
   row, stamped with the creating Admin's tenant. Audited (`Staff/Created`).
2. **Active use** — role changes happen in the Umbraco backoffice Member
   Groups (not yet audited by this app — see gaps).
3. **Deactivate** — today this is `IsActive = false` via erasure, or
   disabling the Member in the backoffice. There is no separate
   "leaver" action yet (gap).
4. **Erase** — `/staffops/admin/{staffKey}/erase` pseudonymises the profile
   and sets `IsActive = false`; financial/working-pattern rows stay linked
   by `StaffKey` (see `gdpr.md`). Audited (`Staff/GdprErased`). The Umbraco
   Member must be disabled/deleted as a separate step.
5. **Retention clock** starts at deactivation (table below).

## 3. Retention

| Data | Retention | Basis / mechanism |
|---|---|---|
| `StaffOps_Availability`, `StaffOps_LeaveRequest`, `StaffOps_WorkHoursHistory` | 3 years after the staff member is deactivated | UK employment-record guidance (ACAS). **Manual purge** — no job yet. |
| `StaffOps_StaffRate` | 6 years after deactivation | UK payroll/tax record-keeping (TMA 1970 s.12B). **Manual purge.** |
| `SkillsEvidence_StaffSkillAssertion` | Purge at deactivation; **deleted outright on erasure** (not pseudonymised) | No payroll or employment-law basis holds it open, and a proficiency claim is meaningless detached from the person — see `gdpr.md`, "Skill assertions are deleted, not pseudonymised". A validated level separately falls due for review after 12 months, so staleness is visible before purge. **Manual purge**; erasure is automated via `IStaffDataParticipant` |
| `SkillsEvidence_SkillDefinition` | Keep — the tenant's own vocabulary, and assertions in history point at it | Not personal data; retiring a skill is the lifecycle action, not deletion |
| `SkillsEvidence_EngineeringEvidence` (attributed rows), `_ActorLink` | **Deleted outright on erasure**; otherwise purge at deactivation | Same reasoning as assertions — no payroll or employment-law basis holds observed contribution data open once it is nobody's. **Automated** on erasure via `IStaffDataParticipant`; the deactivation purge is **manual** |
| `SkillsEvidence_EngineeringEvidence` (unattributed rows) | Kept | Never tied to an identified person in this system. See `gdpr.md` — the honest residue of an erasure, and the queue makes it visible rather than hidden |
| `SkillsEvidence_RawPayload` | **30 days** (`SkillsEvidence:RawPayloadRetentionDays`; 0 = keep) | **Automated** — purged at the end of every successful evidence run, mirroring the ProgrammeOps Bronze window |
| `ServiceOps_SupportCaseFact`, `_SupportCodeLink` | Follows the service record they document; **not** deleted on staff erasure | Not personal data about an identified person — a case is a record of something that happened to a customer. **Manual purge** |
| `ServiceOps_CaseParticipant` | Row kept, `staffKey` and agent name cleared on erasure | The organisation's incident history must not change when a person is erased; only the link to a named person goes. See `gdpr.md` |
| `ServiceOps_AgentLink` | Deleted on erasure | It is the statement that an account is this person, so it goes with them |
| `ServiceOps_RawPayload` | **30 days** (`ServiceOps:RawPayloadRetentionDays`; 0 = keep) | **Automated** — purged at the end of every successful desk run |
| `StaffOps_Staff` (pseudonymised row) | Keep — it is the anchor other rows reference | Row carries no PII after erasure |
| ClickUp / Hub Planner–sourced `ProgrammeOps_*` Silver data | Follows the programme/contract it belongs to | Not personal-data-driven |
| **Bronze captures** (`ProgrammeOps_Raw*Payload`) | **90 days** (`ProgrammeOps:RawPayloadRetentionDays`; 0 = keep) | **Automated** — purged at the end of every successful sync of that source (`*SyncService.PurgeExpiredAsync`); a purge failure is audited as `RetentionPurgeFailed`, never silent |
| `ProgrammeOps_UnresolvedIdentity` (unresolved rows) | Same 90-day window, measured from the row's last sighting | **Automated** — same post-sync purge. Resolved rows are kept as history of who was linked when |
| `ProgrammeOps_ExternalIdentityLink` | Life of the staff record; deleted on GDPR erasure (`IdentityLinkDataParticipant`) and by an Admin unlink | Manual (unlink) + erasure |
| `ProgrammeOps_SyncRun` (finished rows) | **180 days** (`ProgrammeOps:SyncRunHistoryRetentionDays`; 0 = keep) | **Automated** — same post-sync purge |
| Audit logs (`*_AuditLog`) | 6 years | Evidence for financial and access-control questions. **Manual purge**; never edited |
| Umbraco Members of erased staff | Disable immediately; delete after 3 years | Backoffice action |
| **Archived tenant** — everything above scoped to that tenant | 90 days after archive, then purge tenant-scoped rows | Manual runbook (§6) — gives a customer a defined window to request an export |
| Serilog files (`umbraco/Logs`) | 30 days | Hosting/log-shipping config, outside the app |

## 4. Data-subject requests (DSR)

| Right | Route | Who | Audit entry |
|---|---|---|---|
| Access (Art. 15) | `GET /staffops/admin/{staffKey}/export` — JSON: profile, availability, leave, rate history, work-hours history, **audit trail of actions against the subject**, and `LinkedRecords` from other feature areas (`IStaffDataParticipant`: Programme Ops identity links; every version of the subject's skill assertions; and their engineering evidence plus the approved account mappings that attributed it, each with a link back to source) | Admin, own tenant only | `Staff/GdprExported` |
| Erasure (Art. 17) | `POST /staffops/admin/{staffKey}/erase` — participants erase first (identity links deleted; skill assertions deleted outright, history included; approved evidence actor links then attributed evidence rows deleted, in that order; desk agent links deleted and support participation **detached**, not deleted), then the staff row is pseudonymised | Admin, own tenant only | `Staff/GdprErased`, plus `Staff/AssertionsErasedForSubject` and `Staff/EvidenceErasedForSubject` in `SkillsEvidence_AuditLog` and `Staff/ParticipationDetachedForSubject` in `ServiceOps_AuditLog`, each with counts only |
| Rectification (Art. 16) | Name/job title/team: backoffice Member edit + `StaffOps_Staff` (no UI yet); hours: `/staffops/admin/{staffKey}/work-hours`; **skills: self-service** — the subject challenges their own record at `/staffops/skills`, which returns it to a reviewer with their stated correction | Admin, and the subject themself for skills | `Staff/WorkHoursChanged` for hours; `SkillAssertion/AssertionChallenged` for skills; **gap** for the rest |
| Portability (Art. 20) | Same JSON export | Admin | as access |
| Restriction / objection | Set the Member inactive in the backoffice; suspend the tenant for organisation-wide restriction | Admin / Platform Admin | tenant changes audited |

Turnaround target: one calendar month (statutory). The export is Admin-run,
so the workflow is: request arrives → Admin verifies identity → Admin runs
export → Admin sends the file through the organisation's secure channel.
There is no self-service "download my data" for the member (gap).

## 5. Audit coverage

| Action | Table | Action name |
|---|---|---|
| Staff created / hours changed / rate changed | `StaffOps_AuditLog` | `Created`, `WorkHoursChanged`, `RateChanged` |
| MFA reset by an Admin | `StaffOps_AuditLog` | `MfaReset` |
| GDPR export / erasure | `StaffOps_AuditLog` | `GdprExported`, `GdprErased` |
| Tenant created / status / plan | `StaffOps_AuditLog` | `TenantCreated`, `TenantStatusChanged`, `TenantPlanChanged` |
| Sync run success / failure / retention purge failure | `ProgrammeOps_AuditLog` (+ the operational row in `ProgrammeOps_SyncRun`, linked by `runKey`) | `SyncCompleted`, `SyncFailed`, `RetentionPurgeFailed` |
| Identity link created / removed | `ProgrammeOps_AuditLog` | `IdentityLinked`, `IdentityUnlinked` |
| Migration removed legacy Hub Planner booking work items | `ProgrammeOps_AuditLog` | `Migration/LegacyHubPlannerWorkItemsRemoved` (with row counts) |
| Branding publish / rollback | `BrandingOps_AuditLog` | see `branding.md` |
| Contract / invoice status changes, document uploads | `ContractOps_AuditLog` | see `contract-ops.md` |
| Skill declared / validated / rejected / challenged / withdrawn | `SkillsEvidence_AuditLog` | `AssertionDeclared`, `AssertionValidated`, `AssertionRejected`, `AssertionChallenged`, `AssertionWithdrawn` — with the skill, status, level and reviewer, but **never** the free-text notes (they go with the subject on erasure; the audit log does not) |
| Skill added / retired / reinstated | `SkillsEvidence_AuditLog` | `SkillCreated`, `SkillRetired`, `SkillReinstated` |
| Skill assertions erased for a subject | `SkillsEvidence_AuditLog` | `AssertionsErasedForSubject` (row count only) |
| Evidence source connected / updated / disconnected | `SkillsEvidence_AuditLog` | `EvidenceConnectionCreated`, `EvidenceConnectionUpdated`, `EvidenceConnectionDisconnected` |
| Evidence sync completed / failed | `SkillsEvidence_AuditLog` | `EvidenceSyncCompleted`, `EvidenceSyncFailed` — with row counts and the partial/permission-lost tallies |
| External account approved as a person / revoked | `SkillsEvidence_AuditLog` | `EvidenceActorLinkApproved`, `EvidenceActorLinkRevoked`. Approving one publishes somebody's work to their record, so it carries the approver's member id |
| Evidence erased for a subject | `SkillsEvidence_AuditLog` | `EvidenceErasedForSubject` (link and row counts only) |
| Desk connected / updated / disconnected, components approved | `ServiceOps_AuditLog` | `DeskConnectionCreated`, `DeskConnectionUpdated`, `DeskConnectionDisconnected`, `DeskComponentsApproved` |
| Desk sync completed / failed | `ServiceOps_AuditLog` | `DeskSyncCompleted`, `DeskSyncFailed` — with case counts and the partial tallies |
| **Root cause confirmed / ruled out** | `ServiceOps_AuditLog` | `SupportRootCauseConfirmed`, `SupportRootCauseRuledOut`. The strongest claim the product makes, so the reviewer, the artefact and the time are all recorded; the rationale lives on the link row |
| Case linked to a change / link removed | `ServiceOps_AuditLog` | `SupportLinkCreated`, `SupportLinkRemoved` |
| Desk agent approved as a person / revoked | `ServiceOps_AuditLog` | `DeskAgentLinkApproved`, `DeskAgentLinkRevoked` |
| Support participation detached for a subject | `ServiceOps_AuditLog` | `ParticipationDetachedForSubject` (counts only) |
| Component declared / reviewed / retired, cover approved or removed | `SkillsEvidence_AuditLog` | `ComponentDeclared`, `ComponentUpdated`, `ComponentReviewed`, `ComponentRetired`, `ComponentBackupApproved`, `ComponentBackupRemoved` |
| Coverage action raised / closed | `SkillsEvidence_AuditLog` | `CoverageActionRaised`, `CoverageActionClosed` — the outcome is recorded, so a decision that was dropped stays visible |
| **Evidence processing decision recorded / withdrawn** | `SkillsEvidence_AuditLog` | `EvidenceProcessingDecisionRecorded`, `EvidenceProcessingDecisionWithdrawn`. Carries the lawful basis, notice and DPIA references and the signatory — the customer's own artefacts, which an auditor needs, unlike free text about a person |
| Continuity records detached for a subject | `SkillsEvidence_AuditLog` | `ContinuityDetachedForSubject` (counts only) |
| Suggestions generated / accepted / dismissed | `SkillsEvidence_AuditLog` | `SuggestionsGenerated`, `SuggestionAccepted`, `SuggestionDismissed`. A suggestion is only ever a proposal, so what is audited is the human decision — who accepted or dismissed what, which is the accountability required when a suggestion becomes an assertion |
| Login success/failure, MFA challenge outcomes | **not audited by this app** (Umbraco Member lockout counts failures; Serilog has the request) | gap |
| Role (Member Group) changes | **not audited** — done in the backoffice | gap |

Admins see the first two tables merged at `/staffops/admin/audit`
(newest 100). Every request also carries `X-Correlation-ID`
(`Middleware/CorrelationIdMiddleware`), which is stamped on every Serilog
event for that request — quote it when raising a support ticket.

## 6. Operator runbooks

**Archived-tenant purge (after the 90-day window).** No job exists. Whole-customer
exit is these steps, in order, after confirming an export was offered:

1. **Delivery data.** Run "Delete delivery data" on the tenant console
   (`DeliveryDataPurgeService`, §2 above). This covers every
   `ProgrammeOps_*` table, including recorded reviews, raw import copies
   and identity matches, and nothing else.
2. **Other feature areas, if the tenant used them.** Every table in
   ContractOps, SkillsEvidence, ServiceOps, SecurityAssurance and
   ExecutiveReview carries `tenantId`, but none has a purge button yet
   (ExecutiveReview has its own governed purge, `executive-data-lifecycle.md`).
   Delete with `DELETE FROM <table> WHERE tenantId = @t`, and remove the
   tenant's contract files from `App_Data/contracts/<contractKey:N>/`
   before deleting `ContractOps_ContractDocument` rows.
3. **Branding and staff** (below), then the tenant's Umbraco Members
   from the backoffice.
4. **Outside the database.** Backups keep the data until they expire:
   state the backup retention in the customer agreement. Files already
   downloaded (CSV exports, printed packs, invoices) are the recipient's
   to dispose of, and the operator's own copies must be listed and deleted.

Do not tell a customer "everything is deleted" until every step that applies
has been run and its counts recorded. Step 1 on its own does not justify it.

Steps 2–3 in SQL, in a transaction, with the tenant key substituted:

```sql
DECLARE @t uniqueidentifier = '<tenantKey>';
-- 1. Branding (already tenant-scoped)
DELETE FROM BrandingOps_AuditLog WHERE tenantId = @t;
DELETE FROM BrandingOps_Asset    WHERE tenantId = @t;   -- and remove wwwroot/media/branding/<tenantKey:N>/
DELETE FROM BrandingOps_Profile  WHERE tenantId = @t;
-- 2. Staff (anchor rows last)
DELETE a FROM StaffOps_Availability a JOIN StaffOps_Staff s ON s.staffKey = a.staffKey WHERE s.tenantId = @t;
DELETE l FROM StaffOps_LeaveRequest l JOIN StaffOps_Staff s ON s.staffKey = l.staffKey WHERE s.tenantId = @t;
DELETE w FROM StaffOps_WorkHoursHistory w JOIN StaffOps_Staff s ON s.staffKey = w.staffKey WHERE s.tenantId = @t;
DELETE r FROM StaffOps_StaffRate r JOIN StaffOps_Staff s ON s.staffKey = r.staffKey WHERE s.tenantId = @t;
DELETE m FROM StaffOps_MemberMfa m JOIN StaffOps_Staff s ON s.memberId = m.memberId WHERE s.tenantId = @t;
DELETE FROM StaffOps_Staff WHERE tenantId = @t;
-- 3. Keep Tenancy_Tenant (status Archived) and audit rows as the record that the tenant existed.
```

Then delete the tenant's Umbraco Members from the backoffice. This runbook has
not yet been rehearsed end to end on a tenant that used every module; rehearse
it before promising it in a customer agreement.

**Staff record purge (3/6-year windows).** Query `StaffOps_Staff WHERE
isActive = 0 AND updatedAtUtc < DATEADD(year, -3, GETUTCDATE())`, then
delete availability/leave/work-hours rows for those keys; rates only after
6 years.

## 7. Platform assumptions this document relies on

- **Encryption at rest / in transit and backups** are the hosting
  platform's responsibility (Azure SQL TDE + TLS, or equivalent). The app
  enforces HTTPS, HSTS and Secure cookies (see `Program.cs`).
- **Single processing region** — no data leaves the deployment's region
  except to ClickUp / Hub Planner (read-only pulls, tokens held only in
  the secret store).
- **Sub-processors**: ClickUp, Hub Planner, the hosting provider, the SQL
  provider. Name them in the customer DPA.

## 8. Known gaps (tracked in `release-readiness.md`)

- No automated purge for staff/audit retention windows or archived tenants.
- No login/MFA/role-change audit events.
- No self-service DSR (member-initiated export) and no rectification UI
  for name/title/team. Skills are the exception: the subject can inspect
  and challenge their own record at `/staffops/skills`.
- ~~`SkillsEvidence_AuditLog` has no reader in the UI~~ — **closed.** The
  Admin audit page at `/staffops/admin/audit` now merges four sources:
  Staff Ops, Programme Ops, Skills &amp; Evidence and Service Ops, so a
  skill validation, an identity mapping, a confirmed root cause and a
  recorded lawful basis are all visible where an Admin looks.
- **The worker-monitoring decision is now a gate, not a gap.** A tenant's
  lawful basis, worker notice and DPIA decision are recorded in
  `SkillsEvidence_ProcessingDecision`, and `GitHubEvidenceIngestionService`
  refuses to run without a current one — no decision, no notice, no DPIA
  or a passed review date each block collection with a specific message.
  Recording supersedes rather than edits, so "what were we relying on in
  March, and who signed it" stays answerable. The pre-enablement check at
  `/staffops/skills/continuity/processing` walks the rest of the list,
  separating what was observed in the tenant's data from what is
  structural in the product and what is the customer's own word. **What
  the product still cannot do is judge whether their basis is sound.**
  The gate covers repository evidence only, not the skills matrix — that
  is self-declared data the subject can see and challenge.
- **Worker-monitoring paperwork is still the customer's.** Before enabling
  the skills matrix for real employees, record purpose, lawful basis,
  proportionality, notice to staff, retention and a DPIA decision with
  their privacy/HR owners (ICO monitoring-workers guidance). The skills
  matrix is reviewed, self-declared data with a correction route, which
  is the mild end of this. **Repository evidence is not**: it collects
  person-level activity from a customer's source control, and a DPIA is
  the expected answer there, not an optional one. The paperwork is the
  customer's to complete; the product cannot assert it on their behalf.
- **Repository evidence has never run against a live GitHub App.** The
  app credentials are unset on every environment, so no tenant can
  complete an installation. The application says so itself
  (`EvidenceCoverageSummary.IsUnverifiedReplay`, and a banner on the
  affected pages) rather than letting fixture data pass as production.
- ~~No subject-facing view of engineering evidence~~ — **closed.** The
  "My Skills" page now shows a staff member their own repository
  evidence and support participation, the external accounts approved as
  them, and the coverage caveats. Being able to inspect what a system
  says about you is the mitigation that makes the collection
  defensible, and it should not require asking an Admin to run a report.
  The correction route for a wrong attribution is still to ask an Admin
  to revoke the account mapping — the page says so explicitly — because
  revoking is an identity decision, not a self-service one.
- **Evidence and desk syncs are manual, but now multi-instance safe.**
  GitHub, Azure DevOps and Freshdesk now reuse the same source-neutral
  `SyncRunCoordinator` / `ISyncRunRepository` lease-and-run-state
  infrastructure as ProgrammeOps, still under their own source names. A
  second Admin pressing sync while one is already running is refused
  whether it lands on the same node or another, and a stalled worker
  that loses its lease cannot publish success over the new owner. The
  connections pages now show durable run status (`running` / `failed` /
  last complete run) rather than only per-stream coverage rows.

  **What is still open is durable background execution.** These syncs
  still run inside the request that triggered them, so there is no queue
  or scheduler yet; every run is started by an Admin and a proxy timeout
  can still interrupt the browser-visible request even though the run
  itself now has durable lease/state tracking.
- **Freshdesk has never been exercised against a live desk.** The field
  mapping in particular — which field carries the product/component tag,
  and any custom status codes — is convention-based and needs confirming
  during pilot setup. The application self-labels service figures as an
  unverified replay until a run completes cleanly.
- **No Zendesk or Zoho Desk adapter.** A staff member can now see their
  own support participation on "My Skills" (gated on the
  `SupportEvidence` feature); the *admin-facing* portfolio at
  `/staffops/skills/portfolio` still shows repository evidence only, so
  a reviewer reading someone else's record sees less than the subject
  does.
- Tenant-level deletion is automated for ProgrammeOps only
  (`DeliveryDataPurgeService`). Every other area is tenant-scoped but is
  purged by hand (§6), and there is no whole-tenant export.
