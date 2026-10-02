# SaaS onboarding assessment and implementation plan — 26 September 2026

## Position and evidence

ProgrammePulse has a credible assisted-onboarding foundation, but is not yet an unattended SaaS provisioning product. This assessment uses the current controllers, repositories, identity flow and deployment documentation, rather than treating historic readiness summaries as current evidence.

| Area | Current evidence | Implication |
|---|---|---|
| Organisation lifecycle | TenantAdminService, TenantAccessPolicy, plan entitlements | Reuse existing lifecycle; onboarding must not bypass suspended or expired accounts. |
| Identity | StaffOnboardingService creates Member, group, profile and hours history | Reuse the identity path; avoid a second identity system or moving existing customer identities. |
| Isolation | Explicit tenant predicates and per-tenant credentials | Keep tenant selection platform-only; do not introduce tenant switching or shared source credentials. |
| Source activation | Durable publication state, partial-data warnings, identity resolution queue | Publication is observable; correctness and customer value still require reconciliation. |
| Acquisition | `/purchase` is a diagnostic journey | It does not collect credentials, take payment or provision tenants. |
| Operations | Health, audit, deployment guards and security assurance register | Local code checks do not replace a deployed restore test, security assessment or live connector verification. |

## Delivered increment: assisted setup

Open `/staffops/platform/tenants`, create an organisation, then select its name. The new onboarding workspace provides:

1. An outcome-first discovery and acceptance sequence.
2. Platform-authorised creation of a new tenant administrator without manual tenant SQL edits. Role is fixed to Admin, never Platform Admin; existing email identities are rejected by the existing identity service.
3. Server validation and access-policy checks before provisioning; a surrounding Umbraco scope is completed only after identity/profile/history creation and the tenant audit entry succeed. Passwords are not included in the audit or redirect URL.
4. Tenant-specific source publication evidence, retaining running/failed partial-data caveats.
5. Explicit handover steps for first sign-in, MFA, source reconciliation, customer acceptance and support ownership.

The workspace is an operator guide and live observation page, not a persisted project checklist. Active staff count does not attest to administrator membership, MFA enrollment or customer acceptance. No invitations, email delivery, billing automation, SSO or automatic activation are added. Provisioning uses an operator-supplied initial password and the existing Identity password policy, with a 12–128 character form constraint. Secure delivery and customer identity verification remain assisted steps. Tenant creation and administrator creation are separate, recoverable stages: an empty tenant can be revisited.

The provisioning operation can intentionally add another administrator. Duplicate email checks provide ordinary retry protection, not a durable idempotency guarantee for arbitrary parallel requests. SQL-backed tests exercise real MemberManager creation and rollback after an injected audit failure, followed by a successful retry. Concurrent duplicate submissions, first sign-in and MFA still need a live walkthrough before customer use. No claim of distributed atomicity is made.

## Architecture decisions and alternatives

Keep the modular monolith and existing tenant boundary for this increment. Extracting provisioning into a new service would add distributed failure handling before onboarding volume has demonstrated that need. Reconsider extraction when measured queue volume, independent scaling or team ownership justifies it.

Use assisted provisioning now because it closes the manual SQL gap without pretending an email invitation is delivered. Prefer expiring, single-use invitations for the next release. Building public signup before invitation delivery, abuse controls and durable provisioning recovery would increase support burden.

Keep commercial lifecycle and customer onboarding distinct: Active is an access decision, not proof of acceptance or paid status. Do not introduce a single readiness percentage that obscures missing governance, partial data or an untested connector. Offer a dedicated deployment where customer isolation requirements exceed the shared-host evidence; compare operating cost and support burden explicitly rather than assuming either model is universally best.

## Delivery sequence and acceptance gates

| Priority | Deliverable | Owner | Exit evidence |
|---|---|---|---|
| P0 — assisted pilot | New workspace, administrator provisioning, controlled implementation record | Implementation + engineering | Two fresh organisations onboarded without SQL; accounts cannot read each other's data; rollback and duplicate-submit walkthrough passes. |
| P0 — repeatable service | Standard discovery, outcome, data scope, named sponsor/admin, target date, acceptance and support handover template | Customer success | Every pilot has named owners, a reconciled source sample and written outcome acceptance. |
| P1 — reliable provisioning | Durable onboarding aggregate and operation key; atomic uniqueness; stage history; retry/reconciliation worker; failure injection | Engineering | Parallel retries yield one intended identity; interrupted operations resume or surface an actionable recovery state. |
| P1 — secure invitations | Verified destination, expiring single-use token, resend/revoke, durable email outbox, rate limits, first-login setup | Identity + operations | Replay, expiry, wrong-recipient and delivery-failure tests; operator never handles the customer's password. |
| P1 — guided tenant setup | Tenant-admin workspace, selected use case/source, persisted owners and milestones, plan-aware routes | Product + implementation | Customer completes setup without platform access; optional connectors do not block unrelated use cases. |
| P1 — first value | Credential preflight, queued sync, progress, mapping review, source reconciliation and explicit acceptance | Data engineering + customer | Agreed source totals and sample match; partial runs cannot satisfy acceptance; named customer accepts outcome. |
| P2 — commercial automation | Payment provider, signed/replay-safe webhooks, subscription state reconciliation, grace/cancel flows | Commercial + engineering | Payment retries cannot create duplicate tenants; access follows reconciled subscription policy. |
| P2 — enterprise scale | SSO/domain verification, data region options, lifecycle automation, restore/exit exercises, support SLOs | Architecture + operations | Evidence meets contracted requirements; no unsupported residency or compliance claims. |

Sequence is by dependency and risk, not an unvalidated calendar estimate. Estimate effort after the pilot identifies actual support time and identity constraints.

## Standard implementation record

Record organisation key, sponsor, administrator, implementation owner, agreed use case/plan, approved data scope, source owner, target first-value date, success criterion, dependencies and support contact. For each checkpoint capture owner, UTC timestamp, evidence reference and unresolved exception: access confirmed, MFA confirmed, source authorised, first complete publication, mapping reviewed, sample reconciled, acceptance received, support handover completed. Store no passwords, tokens or raw customer exports in the record.

Measure elapsed and hands-on time separately: organisation created → administrator first login → first usable publication → customer-accepted outcome. Track invitation delivery failures, abandoned stages, retries, mapping exceptions and support touches per tenant. Collect a baseline from the first five assisted customers before setting numerical targets; report median and slow cases, excluding test tenants. No telemetry for these milestones is claimed in this increment.

## Verification and release boundary

Automated checks cover platform capability isolation, antiforgery declaration and administrator input validation, alongside the repository's existing architecture, tenancy and access-policy tests. SQL-backed provisioning tests cover tenant assignment, Admin-only role assignment, duplicate email rejection, suspended/missing tenants, secret-free audit detail, and rollback/retry after audit failure. Compile Razor explicitly because normal builds skip views. Deployment requires the existing security-assurance and release-readiness gates plus the live identity walkthrough above. Existing-member transfers remain a separate migration problem because downstream records must not silently cross tenant boundaries.
