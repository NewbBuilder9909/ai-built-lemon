# Tenancy: multi-tenant foundation, piloted on Branding Ops

September 2026 onboarding update: platform operators can select an organisation at `/staffops/platform/tenants` to create a new customer administrator directly in that tenant and inspect setup evidence. See [the onboarding assessment and delivery plan](saas-onboarding.md). Public signup and existing-member transfers remain unavailable; earlier backoffice-only provisioning descriptions below are historical.

A new `Tenancy` feature area (`Models/Tenancy`, `Services/Tenancy`,
`Migrations/Tenancy`, `Composers/TenancyComposer.cs`), reversing a decision
[`docs/branding.md`](branding.md) previously documented: this codebase's
original brief also described a multi-tenant SaaS platform, and Branding Ops
was deliberately built single-tenant instead, with multi-tenancy called out
as "a deliberate follow-up project, not an extension of this one." This is
that follow-up — piloted on Branding Ops only, not rolled out everywhere at
once.

## Why member-based resolution, not subdomain/custom-domain

There is no self-service member registration anywhere in this codebase —
members are provisioned via the Umbraco backoffice Members section only —
and no subdomain/custom-domain routing infrastructure exists (single
Kestrel/IIS site, one hostname). Building either before there's a real need
for it would be exactly the over-engineering this codebase's conventions
warn against, so tenant resolution is **member-based only**:
`Services/Tenancy/TenantContextAccessor` resolves the current tenant from
the signed-in member's `StaffProfile.TenantId` (via
`IStaffRepository.GetByMemberIdAsync`) — the same member → `StaffProfile`
lookup `IStaffRoleAssignmentService.GetForCurrentMemberAsync` already does.

Consequence: an anonymous/pre-login request has no way to know which
tenant it belongs to. `StaffBrandingController.ThemeCss` (unauthenticated
by design — see `docs/branding.md`) and the login page therefore render the
**platform default theme**, not a tenant-specific one, until a member signs
in. `BrandingThemeResolverService.GetActiveThemeAsync(Guid? tenantId)`
returns the platform default immediately — no cache lookup, no repository
call — whenever `tenantId` is `null`.

## The default tenant and backfill

`Models/Tenancy/Tenant.DefaultTenantKey` is a **fixed literal Guid**, not a
database lookup. `Migrations/Tenancy/AddTenancyTables` creates
`Tenancy_Tenant` and seeds exactly one row with that key. Every other
feature area's tenant-column migration (today: `StaffOps_Staff.tenantId` via
`Migrations/StaffOps/AddStaffTenantColumn`, and the three `BrandingOps_*`
tables via `Migrations/BrandingOps/AddBrandingTenantColumns`) adds the
column nullable, then backfills existing rows to that same constant. Using a
constant rather than reading `Tenancy_Tenant` at backfill time means none of
these migration plans need to depend on the `Tenancy` plan having already
run — each plan still runs independently, same as every other feature area.

## Branding Ops isolation

`IBrandingRepository`'s eight methods all take a mandatory `tenantId` and
filter by it in their own query (not in memory afterward) — see the
interface's doc comments for the specific risk on `RollbackAsync`: its
`targetVersionKey` lookup is the one place a missing tenant clause wouldn't
just return nothing, it would let one tenant successfully roll back to
*another tenant's* archived version, since `VersionKey` is only globally
unique, not tenant-scoped.

Asset storage is tenant-scoped on disk too
(`wwwroot/media/branding/{tenantId:N}/{assetKey:N}.{ext}`), not just in the
database — asset URLs are content-addressed by GUID with no tenant check at
request time, so a flat shared directory would let a leaked/guessed
`assetKey` resolve cross-tenant regardless of what the DB row says.

`StaffBrandingController` resolves tenant via `ITenantContext` at the top of
every admin action (right after the existing `IsAdminAsync()` check) and
`Forbid()`s if it can't — every admin action here now requires a real
resolved tenant, unlike `ThemeCss`.

## Known gap: the member lookup half of tenant resolution is live-run-only

`TenantContextAccessor.EnsureResolvedAsync` depends on the concrete
`MemberManager` (a `UserManager<MemberIdentityUser>` subclass), which this
codebase already can't fake without introducing a mocking framework — the
same constraint `ProgrammePulse.Tests/Staff/StaffOnboardingServiceTests.cs`
documents for `StaffOnboardingService`'s Member-creation path, and why
`StaffAccountController`'s login/MFA flow has no unit tests either. What
*is* unit-tested: tenant isolation (`BrandingThemeResolverServiceTenancyTests`,
`StaffAdminControllerTests`), the access policy and the accessor's
decision step (`TenantAccessPolicyTests`), entitlements
(`PlanEntitlementsTests`) and both filters (`TenancyFilterTests`). Only
the "which member is signed in → which staff row" lookup is verified by
live-run walkthrough.

## Commercial lifecycle: status, plan, access policy

`Tenancy_Tenant` gained `status`, `planName` (not `plan` — a T-SQL reserved
word), `trialEndsAtUtc`, `updatedAtUtc`
(`Migrations/Tenancy/AddTenantLifecycleColumns` — nullable-then-backfilled:
existing rows become `Active` on the `Enterprise` plan, so nothing that
worked before the migration is gated afterwards).

- **`TenantStatus`** (`Models/Tenancy/TenantStatus.cs`): `Trial`, `Active`,
  `Suspended`, `Archived`. `Tenant.IsActive` is kept as a denormalised
  convenience (true for Trial/Active) — decisions use `Status`.
- **`TenantAccessPolicy.Evaluate(tenant, now)`** is the single place that
  turns a status into "may this tenant's members use the product": Active
  and unexpired Trial → allowed; expired Trial, Suspended, Archived →
  blocked with a member-facing reason. Pure, exhaustively unit-tested
  (`TenantAccessPolicyTests`).
- **`TenantContextAccessor`** now loads the tenant row and applies the
  policy. A blocked tenant is reported as `IsBlocked = true` with
  `IsResolved = false` and `CurrentTenantId = null`, so tenant-scoped code
  sees the member exactly as it would see an anonymous caller. The
  decision-making half is the static `Apply(tenant, now)`, unit-tested
  directly; only the MemberManager lookup remains live-run-only.
- **`TenantAccessFilter`** (global MVC filter, registered in
  `TenancyComposer`) turns `IsBlocked` into a 403
  (`Views/StaffOps/TenantSuspended.cshtml`) for every `/staffops` action
  before it runs — except `/staffops/account/*` (they must still be able to
  sign in, read the message and sign out) and the anonymous
  `theme.css`. Nothing outside `/staffops` is touched.

## Plans and entitlements (feature gating)

- **`TenantPlan`** (`Starter`, `Professional`, `Enterprise`) and
  **`ProductFeature`** (`ClickUpSync`, `HubPlannerSync`, `ReportingHub`,
  `ContractOps`, `Branding`) are string constants, same reasoning as
  `StaffRole`.
- **`PlanEntitlements`** holds the default plan → feature matrix (Starter:
  ClickUp + Reporting; Professional: + Hub Planner + Branding; Enterprise:
  everything). A deployment can override any one plan's set via
  configuration: `"Entitlements": { "Plans": { "Starter": ["ClickUpSync"] } }`
  (`EntitlementOptions`).
- **`Tenancy_TenantFeatureSelection`** is the per-tenant add-on layer above
  the plan defaults: it stores selected `ProductFeature` keys that a customer
  bought on top of the base plan, and `TenantFeatureGate` unions them with the
  plan matrix before answering. This is the structural seam that turns
  "feature keys in code" into "modules a tenant can actually buy
  selectively", and the same selected keys feed the platform console's
  commercial totals and the public `/purchase` quote.
- **`IFeatureGate.EvaluateAsync(feature)`** (`TenantFeatureGate`) answers
  from the resolved tenant's plan **plus any selected add-ons** with a
  `FeatureGateDecision` (`Enabled`, `Outcome`, `Plan`); `IsEnabledAsync` is
  the boolean shorthand. It **fails closed for an unresolved tenant**
  (`FeatureGateOutcome.TenantUnresolved`) — reversed from the original
  fail-open behaviour after the GTM review (B08 / release register R29):
  every staff row is stamped with a tenant at onboarding and backfilled by
  migration, so an unresolved tenant is an account-setup anomaly to surface,
  not a reason to grant every plan-gated feature. `RequireFeatureFilter`
  renders a different message for that outcome ("ask your platform operator
  to attach your account") than for `NotInPlan` ("contact your account
  manager"), so the customer is sent to the right person. Still a commercial
  control layered on top of authorization, never a replacement for the
  in-body `IsAdminAsync()` / `IsTeamLeadOrAboveAsync()` checks. The pure
  decision step is
  `TenantFeatureGate.Decide` (`TenantFeatureGateTests`).
- **`[RequireFeature(ProductFeature.X)]`** (`RequireFeatureAttribute` /
  `RequireFeatureFilter`) is the hook on controllers/actions. Applied now to
  Contract Ops, Reporting Hub / estimate calibration / executive review,
  Branding Ops (every admin action except anonymous `theme.css`), and the
  evidence/service-health areas. `Views/StaffOps/_Layout.cshtml` hides the
  same links off the same gate.

## Platform Admin and the tenant console

- **`StaffRole.PlatformAdmin`** ("Platform Admin") is a fifth Member Group,
  seeded by `StaffGroupSeeder` but deliberately **not** in `StaffRole.All`
  — a tenant Admin can't create one through `/staffops/admin/create`;
  grant it from the Umbraco backoffice Members section. It is not implied
  by Admin and does not imply Admin; an operator who needs both holds both.
- **`IStaffAuthorizationService.IsPlatformAdminAsync()`** gates
  **`StaffTenantAdminController`** (`/staffops/platform/tenants`): list
  tenants with active/total staff counts, effective features and current
  commercial totals, create a tenant (Trial or Active, plan, trial length),
  change status, change plan and edit self-service add-on modules. Guards:
  can't suspend/archive your own tenant; an Archived tenant can't be
  reactivated from the UI. Also shows platform support signals: last sync
  publication state per source across tenants
  (`ISyncStatusQueryService.GetPublicationStatesAcrossTenantsAsync`) and the
  count of staff rows with no valid tenant.
- All changes are audited to `StaffOps_AuditLog` with `entityType =
  "Tenant"`. On `/staffops/admin/audit` a tenant's Admin sees only the
  Tenant rows for their own organisation (see the audit trail row below).
- Attaching *existing* members to a tenant is still a manual
  `StaffOps_Staff.tenantId` update — see the gaps below. The public
  `/purchase` route can now create a new Trial tenant and its first Admin
  member in one step for Starter/Professional self-service plans.

## Enforcement matrix (what is tenant-scoped today)

| Area | Tenant-scoped? | How |
|---|---|---|
| Branding Ops | ✅ | every repository method takes `tenantId`; controller forbids unresolved |
| Staff admin roster (`/staffops/admin`) | ✅ | `IStaffRepository.GetByTenantAsync`; per-staff actions return NotFound for a key outside the caller's tenant; forbids unresolved |
| Staff onboarding | ✅ | the caller passes its resolved tenant to `StaffOnboardingService.CreateStaffAsync`; there is no fallback. Until 24 Sep 2026 the service read `ITenantContext` itself and defaulted an unresolved tenant to the platform tenant. (`Migrations/StaffOps/BackfillStaffTenantColumn` repairs rows created before tenancy.) |
| Staff audit log (`/staffops/admin/audit`) | ✅ | `StaffOps_AuditLog.tenantId` (`AddStaffAuditLogTenantColumn`); `IStaffAuditLogRepository.LogAsync` stamps it on every write and `GetRecentAsync` filters by it. Platform-console tenant lifecycle events are recorded under the **target** tenant, so a tenant's Admin sees changes made to their own plan or status. Legacy rows are backfilled to their owning tenant (tenant key, then staff profile, then acting member, then the sole tenant if only one exists); anything unprovable stays NULL and is visible to no tenant. `GetForEntityAsync` stays unfiltered because its only caller, the GDPR export, has already verified that the staff key belongs to the caller's tenant. The page previously read the latest 100 rows platform-wide, showing other tenants' staff emails and cost-rate changes to any tenant Admin. Pinned by `StaffAuditLogTenantIsolationIntegrationTests` and `StaffAuditTenantIsolationIntegrationTests` (real SQL). The other trails on that page (ProgrammeOps, SkillsEvidence, ServiceOps) were already tenant-filtered |
| Suspended / archived / expired-trial access | ✅ | `TenantAccessFilter` on all of `/staffops` |
| Plan entitlements and sellable add-ons | ✅ | `PlanEntitlements` + `Tenancy_TenantFeatureSelection`, enforced through `IFeatureGate` / `[RequireFeature]` |
| Reporting Hub, Programme Overview, RAID, Governance, Alerts, Identity queue, sync runs/leases | ✅ | every `ProgrammeOps_*` table carries `tenantId`; `IProgrammeRepository`/`IIdentityResolutionRepository`/`ISyncRunRepository`/`IAuditLogRepository` take it on every method; controllers forbid unresolved; sync lease/run keyed on `(tenantId, source)` so two tenants' syncs never serialize against each other; every FK-carrying `ProgrammeRepository` write additionally verifies the referenced parent row belongs to the same tenant (`CrossTenantReferenceException`), not just the row being written itself. Credentials are isolated too — see "Tenant-owned source connections" below |
| Contract Ops | ✅ | `tenantId` on all 6 `ContractOps_*` tables (`AddContractOpsTenantColumns`); `IContractRepository`/`IContractAuditLogRepository` filter reads, stamp writes, and verify a document/cost/invoice's `ContractKey` belongs to the caller's tenant before writing; `StaffContractController` resolves tenant and forbids unresolved |
| Contract obligations and control attestations | ✅ | `ContractOps_Obligation` and `ContractOps_RepositoryControlAttestation` (26 September 2026) have **non-nullable** `tenantId`; every `IContractObligationRepository` method filters on it, `AddAsync` refuses another tenant's contract (`CrossTenantReferenceException`), and an attestation is accepted only for a repository this tenant has linked to a project. The current-attestation index is per tenant. Covered by `Integration/ContractObligationIntegrationTests` |
| Code repository links | ✅ | `ProgrammeOps_CodeRepositoryLink` (26 September 2026) has a **non-nullable** `tenantId`; every `ICodeRepositoryLinkRepository` method filters on it, and `TryCreateAsync` refuses a project from another tenant (`CrossTenantReferenceException`) before writing. `UX_ProgrammeOps_CodeRepositoryLink_live` is per tenant, so two tenants can link the same repository name independently. The repository is a plain string, never a reference into the evidence area. Covered by `Integration/CodeRepositoryLinkIntegrationTests` |
| Approvals | ✅ | `tenantId` on `StaffOps_LeaveRequest`; `ILeaveRequestRepository`/`ILeaveApprovalService` take it on every method, `GetPendingAsync` is tenant-filtered, `ApproveAsync`/`RejectAsync` refuse a request belonging to another tenant (`CrossTenantReferenceException`); `StaffApprovalsController` resolves tenant and forbids unresolved |
| Skills and evidence | ✅ | `tenantId` is **non-nullable** on all nine `SkillsEvidence_*` tables — the first area built after tenancy, so there is no legacy row to accommodate and no nullable column to forget. `ISkillsEvidenceRepository` takes `tenantId` on every method and offers no unscoped overload; `SkillsEvidenceRepository.AppendAsync` verifies both the referenced `SkillDefinition` and the row being superseded belong to the caller's tenant (`CrossTenantReferenceException`) before it writes; `UX_SkillsEvidence_SkillDefinition_tenant_skillKey` lets two tenants track the same skill key independently. `StaffSkillsController` resolves tenant and forbids unresolved on every action. The GDPR participant resolves the *subject's* own tenant and filters on it, so an export cannot be the place the boundary is crossed. Covered by `ProgrammePulse.Tests/Integration/SkillsEvidenceRepositoryIntegrationTests` against real LocalDB. **Engineering evidence goes further than any other area on credentials**: `SkillsEvidence_Connection` is unique on `(tenantId, provider, sourceAccountId)` so one tenant may connect several source accounts, each with its own credential, cursor and identity links; the credential has its own Data Protection purpose and — unlike ClickUp/Hub Planner — there is **no deployment-wide fallback**, so an unreadable credential stops the run rather than borrowing one. An `EvidenceActorLink` is scoped to the connection, not the tenant, so approving a login in one organisation does not vouch for the same login in another; `EngineeringEvidenceRepositoryIntegrationTests` pins that against real LocalDB |
| Service Ops (support desks) | ✅ | `tenantId` non-nullable on all eight `ServiceOps_*` tables; `IServiceOpsRepository` takes it on every method with no unscoped overload. `ServiceOps_DeskConnection` is unique on `(tenantId, provider, sourceAccountId)` so a tenant may connect several desks, each with its own credential, cursor and agent links; the credential has its own Data Protection purpose and **no deployment-wide fallback**. A `SupportCodeLink` write verifies the case belongs to the caller's tenant first, and a `DeskAgentLink` verifies the connection does. Covered by `ProgrammePulse.Tests/Integration/ServiceOpsRepositoryIntegrationTests` against real LocalDB |
| Security Assurance (scanning tools) | ✅ | `tenantId` non-nullable on all six `SecurityAssurance_*` tables; `ISecurityAssuranceRepository` takes it on every method, and every identity index leads with it, so the same Aikido ids in two tenants are two rows. One connection per (tenant, tool), credential under its own Data Protection purpose with **no deployment-wide fallback**. Only the Bronze retention purge is cross-tenant (exempted in `TenantPredicateTests`). Covered by `SecurityAssuranceRepositoryIntegrationTests` against real LocalDB |
| My Profile, My Work | ✅ (self) | the caller's own leave requests and assigned work, read with the caller's tenant (`ILeaveQueryService`, `IMyWorkQueryService`) |
| Member Groups (roles) | ❌ global, but see below | every tenant-scoped *action* above independently verifies the record's own tenant, so a global Member Group no longer implies cross-tenant reach in practice |

## Tenant-owned source connections (ProgrammeOps B03)

`ProgrammeOps_SourceConnection` (`Models/Programme/SourceConnection`,
`Services/ProgrammeOps/SourceConnectionRepository`) gives each tenant its
own connection row per source (ClickUp/Hub Planner), auto-created on that
tenant's first sync, and `ExternalIdentityLink` carries a `ConnectionKey`
provenance field. Credentials are isolated too: a tenant's own Admin can
set/rotate a ClickUp token+workspace or Hub Planner key at
`/staffops/programme/connections` (`StaffSourceConnectionController`),
encrypted at rest via `ISourceCredentialProtector` (ASP.NET Core Data
Protection, purpose `ProgrammePulse.SourceConnection.Credential.v1`) and
resolved once per run (`ClickUpSyncService`/`HubPlannerSyncService` call
`IClickUpApiClient.WithCredential`/`IHubPlannerApiClient.WithCredential`
before the run starts). Each returns an immutable client that sends its token
per request. Missing or invalid tenant credentials fail closed outside
Development. Shared defaults require both Development and explicit
`ProgrammeOps:AllowSharedSourceCredentials=true`; startup rejects shared
provider tokens in every other environment. See [security assurance](security-assurance.md).

## Extension points left for later phases

- **StaffOps** beyond the roster/onboarding/leave-request scoping above —
  availability and `StaffRate` (cost/rate data) queries are still global.
- **Member Groups are still global**, not tenant-scoped
  (`Models/Staff/StaffRole.cs`'s four constants) — an Admin in one tenant is
  still indistinguishable, at the Member Group level, from an Admin in
  another. This is now a defence-in-depth gap rather than a live one: every
  tenant-scoped action (Branding Ops, Staff admin, ProgrammeOps, Contract
  Ops, Approvals) independently verifies the record it acts on belongs to
  the caller's own tenant before reading/writing it, so a global Member
  Group no longer implies cross-tenant *reach* — but tenant-scoped Member
  Groups (or a per-tenant role table) remain the architecturally cleaner
  fix, deliberately deferred to post-launch scale.
- **Self-service is limited to the first admin account** — `/purchase`
  provisions a Trial tenant plus its first Admin member only for the
  self-service plans/modules configured in `CommercialOptions`. Onboarding
  more members still happens from inside the tenant by an Admin, and
  attaching a pre-existing member to a tenant is still a manual
  `StaffOps_Staff.tenantId` update.
- **Subdomain/custom-domain resolution** — deferred, per the reasoning
  above; would only make sense once anonymous, pre-login tenant branding is
  an actual product requirement.
