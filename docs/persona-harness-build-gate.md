# Persona end-to-end harness build gate

Assessment: 19 September 2026. Scope: repository inspection only. No harness code was written, no persona was seeded, no live HTTP request was issued. Every claim below traces to a file in the working tree. The tree contains unrelated uncommitted changes (`README.md`, `scripts/`, `docs/agent-delivery-build-gate.md`).

## Decision

**Gate status: AMBER — proceed, in phases, after a one-test spike.**

The repository is unusually well placed for this. There is exactly one authorization choke point (`IStaffAuthorizationService`), a real-composition integration harness already exists (`ProgrammePulseWebApplicationFactory`), and there is precedent for both fixture-driven tests and build-failing architecture guards. Nothing structural blocks a persona harness.

Three things stop it being GREEN today:

1. Four mechanical unknowns (B1–B4) decide whether the matrix assertions mean anything. Two of them — `Forbid()`'s HTTP shape and antiforgery — can silently produce a suite that passes while proving nothing. Settle them by spike before writing a single matrix assertion.
2. The current model has **role predicates, not capabilities**, and three of the brief's persona expectations cannot be expressed in it at all (Analyst, read-only Board, portfolio access for Board). Those are product decisions, not test gaps.
3. Inspection surfaced **four pre-existing authorization defects** (section 4). A harness written against today's behaviour would encode them as expected results. Record them as deliberately-failing expectations — the posture `agent-delivery-build-gate.md` takes with its stage 4 — rather than quietly accommodating them.

Do not follow the `scripts/agent-delivery-gate.ps1` pattern for the *evidence* here. That gate replays a JSON export against rules re-implemented in PowerShell, which is right for data the application does not own. Authorization rules the application *does* own must not be restated in a script: that creates a second definition of who can do what, and a green gate would then prove only that the two copies agree. Follow the agent-delivery gate's **reporting and honesty shape** — staged results, `report.json`, nonzero exit, an explicitly red stage — while the assertions themselves run as xUnit tests against the real composition.

---

## 1. What exists today

### 1.1 Identity and roles

`Models/Staff/StaffRole.cs` is the single definition of the group-name literals. Six Umbraco Member Groups, seeded idempotently on startup by `Services/Staff/StaffGroupSeeder`:

| Group | In `StaffRole.All` | Assignable from `/staffops/admin/create` | Notes |
| --- | --- | --- | --- |
| Admin | yes | yes | TOTP MFA required at login |
| Holiday Approver | yes | yes | |
| Team Lead | yes | yes | |
| Staff | yes | yes | |
| Board | yes | yes | Added in `3f08d94`; aggregate calibration only |
| Platform Admin | **no** | **no** | Backoffice-granted only, by design |

`CLAUDE.md` still describes "four tenant roles"; there are five. Worth correcting when this work lands.

### 1.2 Authorization mechanism

`Services/Staff/IStaffAuthorizationService` exposes **seven boolean predicates**, each a fixed union of groups resolved through `MemberManager.IsMemberAuthorizedAsync`:

```
IsLoggedInAsync
IsAdminAsync                    -> [Admin]
IsBoardOrAdminAsync             -> [Board, Admin]
IsHolidayApproverOrAdminAsync   -> [Holiday Approver, Admin]
IsTeamLeadOrAboveAsync          -> [Team Lead, Holiday Approver, Admin]
IsStaffAsync                    -> StaffRole.All (five groups)
IsPlatformAdminAsync            -> [Platform Admin]
```

Enforcement is **per action, in the action body** — roughly ninety `if (!await ...Async()) return Forbid();` call sites across eleven controllers. Two filters sit alongside: `RequireFeatureAttribute` (plan entitlement, 403 view) and the global `TenantAccessFilter` (suspended/archived/expired tenant, 403 view on `/staffops` except `/staffops/account` and `/staffops/branding/theme.css`).

Tenant scope is separate from role: `ITenantContext` resolves from the signed-in member's `StaffProfile.TenantId`, and controllers `Forbid()` when it is unresolved.

### 1.3 Test infrastructure

| Asset | State | Relevance |
| --- | --- | --- |
| `Integration/ProgrammePulseWebApplicationFactory` | Real `Program.cs`, real DI, real LocalDB, unattended install, `LocalDbAvailable` skip guard | The harness this work builds on. **No test currently issues an HTTP request through it** — it is used only to resolve repositories from the DI scope. |
| `Integration/IntegrationTestCollection` | `DisableParallelization` | Persona tests must join this collection; two hosts against one LocalDB race on MainDom. |
| `Controllers/*ControllerTests` | Direct construction with `null!` dependencies, negative path only | Proves "Forbid before touching dependencies" — genuinely valuable, and cheap. Proves nothing about cookies, filters, routing, rendering or positive journeys. |
| `Controllers/FakeStaffAuthorizationService` | All seven flags settable | Unit-level seam. Note `IsBoardOrAdminAsync` already ORs in `IsAdmin` while the others do not. |
| `Experience/professional-capacity-cases.json` + tests | JSON fixture copied via `<Content Include … CopyToOutputDirectory>` | The precedent for persona contract files. |
| `Architecture/SourceIndependenceTests` | Fails the build on namespace violations | The precedent for a "no action without a gate" guard. |

---

## 2. Persona to permission mapping

### 2.1 The brief's capabilities against today's model

| Capability | Expressible today | As | Verdict |
| --- | --- | --- | --- |
| ViewOwnWork | yes | `IsStaffAsync` | fine |
| ApproveLeave | yes | `IsHolidayApproverOrAdminAsync` | fine |
| ManageStaff | yes | `IsAdminAsync` | fine |
| ManageIntegrations | yes | `IsAdminAsync` | fine |
| ViewAudit | yes | `IsAdminAsync` | fine |
| ManageTenant | yes | `IsAdminAsync` + `ITenantContext` | fine |
| ManagePlatform | yes | `IsPlatformAdminAsync` | fine |
| ViewTeamCapacity | partly | `IsTeamLeadOrAboveAsync` | includes Holiday Approver — see D3 |
| ViewPortfolio | **no** | `IsTeamLeadOrAboveAsync` | excludes Board — see D4 |
| ViewCommercials | partly | `IsAdminAsync` | all-or-nothing; no "aggregate margin, no rates" tier |
| *read-only anything* | **no** | — | see D2 |

### 2.2 The six personas

| Persona | Nearest group today | Fit |
| --- | --- | --- |
| Alex Morgan — Developer | Staff | **Good.** Portal, My Work (already filtered to `AssignedStaffKey == staff.StaffKey`), leave submit. Correctly excluded from everything else. |
| Priya Shah — Analyst | *none* | **Gap.** Team Lead is the only group with reporting access, and it carries write: RAID create, baseline lock, governance decide, trend capture, stakeholder edit. "Operational reporting, read" is not separable today. |
| Sarah Evans — Project Manager | Team Lead | **Good**, with the caveat that Team Lead currently cannot approve leave (`IsHolidayApproverOrAdminAsync` excludes it). The brief says "approvals where appropriate" — decide which. |
| James Wilson — Board | Board | **Poor.** Board reaches the portal and estimate calibration only. Executive portfolio, aggregate risk, aggregate capacity and trend all sit behind `IsTeamLeadOrAboveAsync`, which Board is not in. Read-only cannot be expressed. |
| Emma Davies — Tenant Administrator | Admin | **Good.** Tenant scoping is enforced structurally and already integration-tested at the repository layer. MFA applies. |
| Platform Administrator | Platform Admin | **Good** — see section 3. |

### 2.3 Recommendation: a capability layer above Member Groups, not instead of them

Umbraco Member Groups are doing the part they are good at — storing who is in which role, editable in the backoffice, queryable pre-sign-in via `IMemberService.GetMembersByGroup`. Replacing that would be a large change with no customer benefit. What is missing is the indirection between a role and what it permits.

Proposed, mirroring the shape `StaffRole.cs` already uses:

- `Models/Staff/Capability.cs` — string constants plus `Capability.All`, the one place capability names are written.
- `Models/Staff/RoleCapabilities.cs` — a pure, static `role -> capabilities` map. No Umbraco types, no async, directly unit-testable. Later, this is the thing a customer configures.
- `IStaffAuthorizationService.HasAsync(string capability)` — resolves the member's groups once, unions their capabilities.

Migration rule: **the seven existing predicates stay**, re-expressed over the map, and a pure test asserts the new implementations reproduce the old truth table across all sixty-four group combinations. No controller changes in the same commit as the capability model. Controllers migrate afterwards, one area at a time, each migration guarded by the persona matrix already being green.

This directly answers the brief's "do not hard-code `if (user.IsBoardMember)`". The codebase does not do that today — it hard-codes `IsTeamLeadOrAboveAsync`, which is the same problem one level up.

---

## 3. Platform Admin and tenant business data — explicit determination

**Determination: Platform Admin must not have access to tenant business data. This is already true, structurally, and should be locked in by test rather than left as an emergent property.**

Traced from code, a member holding only Platform Admin:

| Path | Result | Why |
| --- | --- | --- |
| `/staffops` portal | redirect to login | `IsStaffAsync` uses `StaffRole.All`, which excludes Platform Admin |
| `/staffops/admin`, `/staffops/contracts`, `/staffops/branding`, identities, connections | `Forbid()` | `IsAdminAsync` is `[Admin]` only |
| `/staffops/reporting`, `/staffops/programme` | `Forbid()` | `IsTeamLeadOrAboveAsync` excludes it |
| `/staffops/platform/tenants` | allowed | `IsPlatformAdminAsync` |
| any tenant-scoped query | unresolved tenant | `ITenantContext` reads *their own* `StaffProfile.TenantId`; there is no tenant-impersonation path |

The rule to write into `docs/tenancy.md`: *platform administration confers tenant lifecycle and plan control, never tenant business data. A platform operator who needs a customer's data holds a second, separately-granted, separately-audited Admin membership in that tenant.*

**Residual risk the harness cannot close, and must say so:** a Platform Admin can create tenants, change plans, lift suspensions, and — having backoffice access — grant themselves Admin in any tenant. No authorization test can detect that escalation path. It needs platform-level audit logging; `StaffOps_AuditLog` is tenant-scoped, so today it would record the *use* of that Admin membership under the victim tenant but not the *grant*. Record it in `docs/release-readiness.md` as a named risk, not as something the persona gate covers.

---

## 4. Defects found during inspection

These exist now. A harness written against current behaviour would encode them as expected.

**D1 — Nav renders links that 403 for most members.** `Views/StaffOps/_Layout.cshtml` gates only three links (calibration by `IsBoardOrAdminAsync`, contracts by feature, platform by `IsPlatformAdminAsync`). **Programme Overview, Reporting, Approvals, Admin and Branding are unconditional.** A plain Staff member — Alex — sees five links, every one of which refuses him. A Board member — James — sees the same. Consistent with the `showContracts` comment ("only hides the link — the controller's `[RequireFeature]` is what actually gates it"), so this is a presentation defect, not a security one. It is precisely the kind of thing a persona journey test exists to catch.

**D2 — No read-only concept.** `IsTeamLeadOrAboveAsync` gates both `GET /staffops/reporting/raid` and `POST /staffops/reporting/raid/risks`, and likewise governance, trend capture and baseline lock. The brief's "Board should normally be read-only" is not expressible. This is the strongest argument for the capability split in 2.3.

**D3 — Holiday Approver has full reporting write access.** `IsTeamLeadOrAboveAsync` includes Holiday Approver, so a leave approver can lock baselines and decide change requests. Probably unintended; it follows from a predicate named for seniority rather than for a capability.

**D4 — Board cannot see the portfolio.** Section 2.2. Board was added for estimate calibration and stops there.

None of these is mine to fix unilaterally. They are the product decisions in section 8.

---

## 5. Mechanical blockers, each needing a decision

**B1 — The login rate limiter will fail the suite.** `Program.cs` sets the `login` policy to 5 requests per minute partitioned on `Connection.RemoteIpAddress`, which in-process collapses to one partition. Six personas is six login POSTs; the two MFA personas add a second POST each on the *same* policy — eight. Options: cache one authenticated cookie container per persona for the whole collection (still eight in the first minute, still over); or make `PermitLimit`/`Window` configurable with today's values as defaults and raise them in the test host. **Recommend the configuration knob** — small, useful in production, and it keeps the real login path under test. Whichever is chosen, add a separate test asserting the limiter still refuses the sixth attempt, or the change silently removes a control.

**B2 — `Forbid()`'s HTTP shape is unknown.** Every "cannot" assertion depends on it. `Forbid()` invokes the default forbid scheme; with Umbraco's member and backoffice cookie schemes registered by `AddBackOffice()`/`AddWebsite()`, this may be a 302 to an access-denied path rather than a 403. **Measure it; do not assume it.** The persona contract should carry the observed shape explicitly (`deny: "403"` vs `deny: "redirect"`), and the assertion helper should reject a 200 *and* reject a 400 — see B4.

**B3 — Admin MFA.** Admin and Tenant Admin personas cannot sign in with a password alone. Solvable deterministically, and it makes the test *stronger*: seed the persona's TOTP secret through `IMfaRepository`, then compute the current code in the test with the in-repo `TotpAuthenticator` against the same `TimeProvider`. The enrollment path and the recovery-code path each deserve their own journey test.

**B4 — Antiforgery is the main way to write a fake-passing suite.** Every POST carries `[ValidateAntiForgeryToken]`. A "cannot" test that posts without a token gets a 400 from the antiforgery filter and looks exactly like a successful denial — while proving nothing about authorization. The harness must fetch the form page, extract `__RequestVerificationToken`, and post it, and the shared assertion helper must **fail on 400** rather than treat any non-200 as a pass.

**B5 — Persona creation is asymmetric, deliberately.** The five tenant personas can be created through the production path (`StaffOnboardingService`, real `MemberManager` from the factory's DI scope) — a genuine bonus, since seeding then exercises real onboarding. Platform Admin cannot: `StaffOnboardingService.Validate` rejects any role outside `StaffRole.All`. The seeder must create that one via `MemberManager.CreateAsync` + `AddToRoleAsync`, mirroring the backoffice. **The seeder must not "simplify" this by adding Platform Admin to `StaffRole.All`** — that asymmetry is the control described in section 3.

**B6 — A skipped gate must not be a green gate.** Every integration test currently begins `if (!LocalDbAvailable) { return; }` and passes. For a capacity calculation that is a reasonable trade; for an authorization gate it is not — the gate would report green on a machine that ran nothing. The report must carry `environment.localDb` and resolve to **INCONCLUSIVE**, exit nonzero, when the personas did not actually run.

**B7 — Fixed identities need idempotent, drift-resistant seeding.** Existing integration tests avoid collisions with fresh GUIDs; personas are fixed by email and cannot. The seeder must find-or-create by email *and* reset group membership, tenant, work hours and profile fields to the declared state on every run, or a journey test that edits a persona poisons the next run. Passwords come from `PROGRAMMEPULSE_PERSONA_PASSWORD`, falling back to a per-run random value; emails on `@northstar.test`; nothing committed. The seeder lives in the **test project only**, so it cannot ship.

**B8 — Reachability is not confidentiality.** A 200 on `/staffops/reporting` does not prove Priya cannot see cost rates. Cheap, high-value addition: seed a sentinel hourly rate (e.g. `1337.77`) and a sentinel cross-tenant project name into `StaffOps_StaffRate` and Meridian's data, then assert those strings are absent from **every** response body a non-entitled persona receives. This mirrors the existing structural rule — `ProgrammeOverviewViewModel` carries no cost field at all — and turns it into a runtime check.

---

## 6. Phased plan

**Phase 0 — spike (half a day). Nothing else starts until this is green.**
One test: Alex signs in over real HTTP, `GET /staffops` returns 200 and contains his name; `GET /staffops/reporting/cost` is refused. Deliverables: the measured answer to B2, the chosen answer to B1, a working antiforgery helper (B4), and a `PersonaHttpClient` helper. The output is two recorded facts, not a framework.

**Phase 1 — capability model, zero behaviour change.** `Capability`, `RoleCapabilities`, `HasAsync`; seven predicates re-expressed over the map; a pure test proving the truth table is unchanged across all sixty-four group combinations. No controller touched.

**Phase 2 — deterministic seeding.** `ProgrammePulse.Tests/Personas/NorthstarPersonaSeeder.cs`. Two tenants: Northstar Digital, and Meridian Labs as the cross-tenant foil. Six members, groups, staff profiles with `TenantId`, work-hours history, TOTP for the two MFA personas, a small programme/project/work-item/time-entry fixture, the B8 sentinels. Idempotent and state-resetting. A test asserts running it twice leaves the member count unchanged.

**Phase 3 — persona contracts.** `TestScenarios/Northstar/personas/{developer,analyst,project-manager,board,tenant-admin,platform-admin}.json`, each with `can` / `cannot`, plus `TestScenarios/Northstar/capability-routes.json` mapping each capability to the concrete route(s) that express it. The JSON is the *expectation*; `RoleCapabilities` is the *truth*. A third test asserts every entry in `Capability.All` appears in at least one persona's `can` or `cannot`, so a new capability cannot be added without a persona decision — the forcing function `SourceIndependenceTests` applies to namespaces.

**Phase 4 — matrix and journeys.** `PersonaAuthorizationMatrixTests`: a Theory over persona × capability-route, real HTTP, asserting allow/deny *and* sentinel absence. `PersonaJourneyTests`: one positive business journey each — Alex submits leave and sees it listed; Sarah locks a baseline; James reads calibration aggregates and is refused the per-estimator view; Emma onboards a member and reads the audit log; Emma is refused Meridian's data; the Platform Admin creates a tenant and is refused all of Northstar's business data.

**Phase 5 — the gate.** `scripts/persona-gate.ps1` and `docs/persona-build-gate.md`, in the shape of `agent-delivery-gate.ps1`: stages, `artifacts/persona-gate/report.json`, nonzero exit. Stage 1 contract (persona files parse; every capability covered; every persona has at least one `cannot`). Stage 2 seed determinism. Stage 3 matrix. Stage 4 leak. Stage 5 — **deliberately red** — the four D-defects, until the section 8 decisions are taken. The script runs the tests and shapes the report; it never re-implements an authorization rule.

---

## 7. What this harness will not prove

- It is not a penetration test. It does not sweep guessable GUIDs for IDOR beyond the cases enumerated in the persona files.
- It covers the Umbraco **backoffice** not at all. A member group says nothing about backoffice user permissions.
- It covers only what routes through a controller action. Branding assets are deliberately web-servable from `wwwroot` and are outside it; Contract Ops documents are deliberately not, and are inside it.
- Sentinel-absence assertions are necessary, not sufficient: they catch the leak shapes seeded, in the responses tested.
- Green proves the deployed rules match the declared rules. It does not prove the declared rules are right for a customer — that is what section 8 is for.

---

## 8. Decisions taken, 19 September 2026

All six were taken in the fuller direction. The consequence is that the capability model is **load-bearing, not preparatory** — four of the six decisions are unimplementable without it, so Phase 1 grows and Phase 4 cannot be written against today's predicates.

| # | Decision | Consequence for the plan |
| --- | --- | --- |
| 1 | **Analyst becomes a real Member Group.** | A seventh name in `StaffRole.Seeded` and a sixth in `StaffRole.All`, so `StaffGroupSeeder` creates it and an Admin can assign it at `/staffops/admin/create`. `IsStaffAsync` widens to include it — check every `StaffRole.All` consumer for that side effect. |
| 2 | **Board gets read-only portfolio access.** | Forces the read/write capability split (D2). `ViewPortfolio`, `ViewTeamCapacity`, `ViewProjectRisk` become distinct from `ManageProjectRisk`, `LockBaseline`, `DecideChangeRequest`, `CaptureTrend`, `ManageStakeholders`. This is the single largest piece of Phase 1 and it touches every `IsTeamLeadOrAboveAsync` call site in `StaffReportingController`. |
| 3 | **Holiday Approver loses reporting write (D3).** | `IsTeamLeadOrAboveAsync` stops being a seniority union and becomes a capability lookup. A Holiday Approver who relied on reporting today will lose it — a behaviour change to call out in the release notes, not a silent fix. |
| 4 | **Team Lead gains `ApproveLeave`.** | `IsHolidayApproverOrAdminAsync` widens to include Team Lead. Check `LeaveApprovalService` and `StaffApprovalsController` for any assumption that the approver is not also the requester — a Team Lead approving their own leave is now reachable and needs an explicit rule. |
| 5 | **Nav links get gated (D1).** | Lands with the capability model, one `HasAsync` check per item in `_Layout.cshtml`. Removes five dead links for a plain Staff member. |
| 6 | **Rate limiter becomes configurable (B1).** | `RateLimiting:Login:PermitLimit` / `:Window` bound in `Program.cs`, defaults 5 and 1 minute so production behaviour is unchanged. Raised in the test host only. Requires a companion test asserting the limiter still refuses the sixth attempt at the default. |

Two consequences worth stating plainly:

- **Decisions 2, 3 and 4 are production behaviour changes**, not test scaffolding. They change who can do what for real members. The persona matrix should be written against the *decided* model, so Phase 4 goes green when Phase 1 lands correctly — rather than encoding today's behaviour and being rewritten.
- **Decision 1 widens `StaffRole.All`**, which is the input to `IsStaffAsync`. Every place that treats "in `StaffRole.All`" as "is an ordinary employee" needs re-reading, including `StaffOnboardingService.Validate`, which uses it to decide which roles an Admin may assign.

### Revised phase order

Phase 0 (spike) is unchanged and still gates everything. Phase 1 absorbs decisions 1–5 and is now the bulk of the work; decision 6 moves into Phase 0, since the spike cannot run six personas without it.

---

## 9. Phase 0 results — measured, 19 September 2026

Executed against LocalDB `(localdb)\GardnerDB`, database `UmbracoBase_IntegrationTests`, real `Program.cs` composition, real Umbraco member sign-in over HTTP. Full suite after the change: **455 passed, 0 failed**. Observations written to `artifacts/persona-gate/phase0-observations.json`.

### What was proved

Alex Morgan (Staff group) was created, signed in through the real login action with a real antiforgery token, reached `/staffops` and `/staffops/my-work`, **submitted a leave request and read it back**, and was refused `/staffops/reporting/cost`, `/staffops/admin`, `/staffops/platform/tenants` and `/staffops/contracts`.

The leave round-trip is the part that matters. A status code proves authentication; reading back a row written against the signed-in member's own `StaffKey` proves the session is bound to *Alex's profile*. Every persona journey in Phase 4 should carry one assertion of that kind.

### B1 — rate limiter: resolved

`Startup/RateLimitSettings` binds `RateLimiting:Login` / `:Sync`, defaulting to the previous hardcoded 5/minute and 3/minute. The test host raises the login limit to 1000; production configuration is untouched. `RateLimitSettingsTests` asserts an absent configuration still yields 5/minute, and that an invalid or zero limit falls back rather than locking everyone out.

### B2 — `Forbid()`: resolved, and not what the matrix would have assumed

**`Forbid()` never returns 403 in this application.** Umbraco's member cookie scheme converts it to:

```
302 -> /Account/AccessDenied?ReturnUrl=<original path>
```

Two consequences, both now handled in `PersonaSession.Outcome`:

1. **A matrix asserting `403` would fail on every single "cannot" case.** Deny is a 302 with an `AccessDenied` location.
2. **More dangerous: a denial and a successful POST are both 302.** Status alone cannot separate them — the same trap as antiforgery, one level up. A redirect counts as a denial only when its `Location` points at the access-denied handler or the login page; every other redirect is a completed action. Without this, a "cannot submit X" test would pass against an application that cheerfully submitted X.

**Incidental finding (UX, not security):** `/Account/AccessDenied` is an ASP.NET Core Identity default path that this application never defines. It returns **200** through Umbraco's content pipeline, not an authored access-denied page — so a refused member currently lands on a page that neither explains the refusal nor offers a way back. There is no `Views/StaffOps/AccessDenied.cshtml`, unlike the authored `TenantSuspended` and `FeatureUnavailable` views the two filters use. Worth fixing alongside the nav gating (D1), since both are "the member is told nothing useful" defects.

### B4 — antiforgery: resolved

A POST with no token returns **400**, cleanly distinguishable from the 302 denial. `AccessOutcome.Malformed` is a distinct value and `AssertRefused` fails on it explicitly, so a token-less test can never masquerade as a passing authorization assertion.

### B7 — seed idempotency: evidenced, not yet asserted

The spike was executed four times against the same persistent test database. The first run created the member; subsequent runs found it by email, re-asserted the password, group membership and profile, and signed in successfully. That is real evidence, but it is incidental — **Phase 2 still owes an explicit test** that asserts the member count is unchanged after a second seed.

### B3, B6, B8 — still open

- **B3 (MFA):** `PersonaSignIn` throws for any persona with `RequiresMfa`. Emma and any other Admin persona cannot sign in until the TOTP step is implemented. Unchanged from the plan.
- **B6 (skip ≠ pass):** the spike still returns early without LocalDB, writing `SKIPPED:` to test output and producing no observations file. Phase 5's script must treat a missing observations file as INCONCLUSIVE. Not yet enforced.
- **B8 (leak sentinels):** not started. No cost or cross-tenant sentinel is seeded yet.

### Files added or changed in Phase 0 (superseded by section 10)

| File | Purpose |
| --- | --- |
| `Startup/RateLimitSettings.cs` | Configurable limits, previous values as defaults (B1) |
| `Program.cs` | Reads the settings instead of hardcoding 5/3 |
| `ProgrammePulse.Tests/Startup/RateLimitSettingsTests.cs` | Defaults unchanged; invalid values fall back |
| `ProgrammePulse.Tests/Integration/ProgrammePulseWebApplicationFactory.cs` | Raises the login limit in the test host only |
| `ProgrammePulse.Tests/Personas/NorthstarPersonas.cs` | The persona cast; Phase 0 seeds the Developer |
| `ProgrammePulse.Tests/Personas/NorthstarPersonaSeeder.cs` | Idempotent, state-resetting provisioning (B5, B7) |
| `ProgrammePulse.Tests/Personas/PersonaSession.cs` | Antiforgery and outcome classification (B2, B4) |
| `ProgrammePulse.Tests/Personas/PersonaSignIn.cs` | Real HTTP sign-in, no test auth handler |
| `ProgrammePulse.Tests/Personas/PersonaSpikeTests.cs` | The measurements above |

Nothing is committed.

---

## 10. Second increment — MFA, boundaries and the gate script, 19 September 2026

Full suite: **469 passed, 0 failed**. Persona gate: `powershell -NoProfile -File scripts/persona-gate.ps1` → **RED, by design**, stages 1–3 PASS, stage 4 red on the outstanding items. Report at `artifacts/persona-gate/report.json`.

### Working tree moved mid-phase

Commit `475f210` plus staged changes landed while this was being built, and two of them matter:

- **Every authorization predicate now requires an active staff profile.** `StaffAuthorizationService` takes `IStaffRepository` and checks `IsActive` before any group check. Good hardening. Note the coupling it creates: `IsPlatformAdminAsync` now also requires a `StaffOps_Staff` row, so a platform operator must have a staff profile even though they are not a tenant employee.
- **MFA now covers Platform Admin, not just Admin** (`StaffAccountController.RequiresMfa`). Login also refuses an inactive staff account outright.

The second one invalidated the persona cast as written — `PlatformAdmin` was declared `RequiresMfa: false` and would have failed to sign in. Corrected.

### B1 — a real bug in the first fix

The Phase 0 rate-limit knob did not work. `Program.cs` read `builder.Configuration` **eagerly**, at service-registration time, so any source added after the builder was constructed was silently ignored — which is exactly how `WebApplicationFactory` injects test configuration. The persona suite was still running against 5 requests/minute and every sign-in after the first few returned 429.

It now binds `RateLimitSettings` through DI from the composed `IConfiguration` and resolves it inside the policy factory. Production defaults were never affected (`CreateBuilder` adds appsettings and environment variables before that line), but the setting was un-overridable by any later configuration source — a latent trap, not only a test problem.

**Related finding worth a product decision:** the MFA POST shares the `login` rate-limit bucket. An Admin's sign-in therefore costs two of the five permitted attempts, so a real Admin who mistypes their password twice and then their TOTP code once is locked out for a minute. Consider a separate `mfa` policy.

### B3 — resolved: personas clear a real TOTP challenge

`NorthstarPersonaSeeder` resets and re-enrolls a fixed Base32 secret per MFA persona (reset first, because `StartEnrollmentAsync` deliberately leaves an existing secret alone). `PersonaSignIn` then answers the real challenge with a code computed through `TotpAuthenticator`'s internal HOTP step, already `InternalsVisibleTo` this test project — **no production code was added or relaxed to make this testable**.

Nothing is bypassed. The harness also asserts the *shape* of the flow in both directions: a persona declared as requiring MFA must be redirected to a challenge rather than signed in, and a persona not declared as requiring it must not be. A regression that quietly dropped the MFA requirement fails the harness instead of passing it.

### Section 3 determination — now enforced by test

`PersonaBoundaryTests` proves the platform operator is refused all eight tenant business pages — `/staffops`, `/staffops/admin`, `/staffops/admin/audit`, `/staffops/reporting`, `/staffops/reporting/cost`, `/staffops/programme`, `/staffops/contracts`, `/staffops/branding` — while reaching `/staffops/platform/tenants`. Both halves are asserted; a refusal-only test would pass against an application where nobody can do anything.

The persona's staff profile deliberately sits **inside Northstar's tenant**. That is the adversarial configuration, and it makes the claim as strong as it can be: the exclusion holds on role grounds alone, not merely because the data belongs to someone else.

Also locked in: Emma clears MFA, administers her own tenant, and is refused the platform console. Sarah reaches delivery reporting and RAID but not cost, contracts or admin. Board's current limitation (D4) is captured in a test named `..._pending_phase_1`, so inverting it when decision 2 lands is deliberate and visible in review.

### B6 — resolved by `scripts/persona-gate.ps1`

Four stages, in the shape of `agent-delivery-gate.ps1` but running the real tests rather than restating any rule:

1. **Environment** — LocalDB reachable, else the whole gate is `INCONCLUSIVE`, never green.
2. **Persona tests** — runs the `Personas` namespace, parses the TRX. **Zero executed tests is red**, so a filter typo cannot pass quietly.
3. **Measured evidence** — the observations file must exist and record both a real denial and an allowed journey to contrast it with.
4. **Outstanding items** — deliberately RED, listing B8, D1, D2 and Phase 1.

### Still open

- **B8 (leak sentinels)** — not started. Reachability is proved; confidentiality of response *bodies* is not.
- **Phase 1 (capability model)** — not started, and it is now the critical path: decisions 1–5 are all unimplemented, including the Analyst group, Board's read-only portfolio access, and the read/write split those depend on.
- **Analyst persona** — declared but unseedable until `StaffRole` gains the group.

---

## 11. Phase 1 — the capability model, 19 September 2026

Full suite: **485 passed, 0 failed**. Persona gate: RED by design, stages 1–3 PASS, 15 persona tests. Decisions 1–5 are live.

### Built additively, because the tree is contended

Codex is concurrently building `Services/Integrations/{Jira,OAuth,Tempo}`, `Controllers/StaffOAuthConnectionController`, a raw-connector payload table and `ProgrammeOperationsComposer` changes. `StaffAuthorizationService` also changed twice during this work (an active-profile requirement, then request-scoped caching), and `StaffAccountController` gained MFA for Platform Admin.

The model was therefore built to minimise conflict surface:

- **Two new files carry all of it** — `Models/Staff/Capability.cs` (names) and `Models/Staff/RoleCapabilities.cs` (the matrix). Neither has any conflict surface at all.
- **One method added** to `IStaffAuthorizationService` and its implementation. The seven legacy predicates are untouched, so every unmigrated call site — including Codex's new OAuth controller — keeps working with no coordination.
- **Divergence is caught, not assumed.** `RoleCapabilitiesTests` restates each legacy predicate as the group union it actually uses and compares it to the matrix, requiring every difference to be one of the three named decisions. If either side moves, the build fails rather than the two models quietly disagreeing.

### The model

`Capability` holds 26 names; `RoleCapabilities` maps group → capabilities as pure data. Deny by default: an unknown group grants nothing, so a typo loses access rather than gaining it. Capabilities union across groups, so holding two roles adds and never subtracts.

Read and write are separate throughout — that is the whole point, and three properties are asserted rather than trusted: every seeded group has an entry, every capability is held by someone, and **no role can write delivery data it cannot read**.

### Decisions now live

| Decision | Effect |
| --- | --- |
| 1 — Analyst | New `StaffRole.Analyst`, seeded by `StaffGroupSeeder`, assignable by an Admin. Delivery read, no write, no commercials, no admin. |
| 2 — Board read-only | Board now reads the portfolio, reporting hub, RAID, trend and governance, and is refused every write. |
| 3 — Holiday Approver | Keeps reporting *read* (approving leave needs a capacity view) and loses RAID, baselines, change requests, trend capture and stakeholders. |
| 4 — Team Lead approvals | `ApproveLeave` is now Admin, Holiday Approver **and** Team Lead. |
| 5 — Nav | Programme Overview, Reporting, Approvals, Admin and Branding are each gated on the capability their controller enforces. Five dead links removed for an ordinary employee. |

`StaffReportingController`'s seventeen `IsTeamLeadOrAboveAsync` gates are now per-action capabilities — reads to read capabilities, writes to `ManageProjectRisk`, `LockBaseline`, `DecideChangeRequest`, `CaptureTrend`, `ManageStakeholders`. `StaffProgrammeOverviewController.Index` moved to `ViewPortfolio`.

**Deliberately not migrated yet:** the Admin-only actions (cost, customers, contracts, branding, staff admin, identities, connections) and the programme sync trigger. They behave identically on `IsAdminAsync`, and the sync action sits in the file Codex is actively extending — migrating it now would create a conflict for no behavioural gain.

### A test that had quietly stopped proving its point

`StaffReportingControllerTests.Forbids_team_lead_who_is_not_admin` sets `IsTeamLeadOrAbove = true` to assert a Team Lead is refused cost and customer management. After the hub gates moved to `HasAsync`, that flag granted nothing, so the caller would have been refused for holding *no capabilities at all* — the test would still pass while no longer testing the Team Lead boundary. The fake now grants the Team Lead capability set explicitly.

This is the failure mode to watch for through the rest of the migration: tests that assert a refusal keep passing when the reason for the refusal changes underneath them. The persona tests are the counterweight, because they assert the allowed half of each journey too.

### Verified behaviour

`PersonaBoundaryTests` now proves Board reads six pages and is refused a **properly formed, token-bearing** POST to `baseline/lock`, and that the Analyst reads reporting while being refused delivery write, commercials, contracts, admin and approvals. Both write refusals fail the test on a 400, so antiforgery cannot be mistaken for authorization.

### Still open

- **B8 (leak sentinels)** — response bodies are still unchecked for cost or cross-tenant data.
- **Persona contract files** — `TestScenarios/Northstar/personas/*.json` and the capability-coverage test are unwritten; personas exist in C# only.
- **Exhaustive matrix** — boundaries are asserted case by case, not as persona × capability.
- **Remaining migration** — the Admin-only gates listed above.

---

## 12. Persona contracts, the matrix and leak detection

`TestScenarios/Northstar/personas/*.json` declares, for each of the six personas, which capabilities it holds and which it must not, across all 26. `capability-routes.json` maps each capability to the HTTP route that expresses it. `PersonaAuthorizationMatrixTests` drives the matrix from those files against the real application.

The JSON is expectation, never authorization truth. `RoleCapabilities` is the truth, and `PersonaContractTests` asserts the two agree in both directions. The forcing function is that **every capability must be decided by every persona** — a new capability cannot be added without stating, per class of user, whether they hold it.

### A real divergence, caught on the first run

The contract tests failed immediately with:

> `ProjectManager forbids TriggerSync but group 'Team Lead' grants it.`

The matrix had `TriggerSync` under delivery write, so Team Lead held it — but `POST /staffops/programme/sync/{source}` has always been gated on `IsAdminAsync`. The application was right and the model was wrong: pulling from an external source spends a customer's API quota, can overwrite imported records, and belongs with the credentials that authorise it. It moved to tenant administration.

### Leak detection, with a positive control

Every persona is seeded an hourly rate of `1337.77`, and the matrix asserts that string appears in no response body served to a persona without `ViewCommercials`.

That assertion is worthless alone: a leak detector wired to a value the application never emits detects nothing. So a positive control proves the sentinel genuinely reaches the staff detail page for the tenant administrator. Only then does the absence asserted everywhere else mean anything.

### Verified by mutation, not by passing

Every matrix test passed first time, which is not evidence. `ManageProjectRisk` was temporarily granted to Board in `RoleCapabilities` — the application widened, the contract files untouched — and all three layers caught it, including over real HTTP:

> `ManageProjectRisk [POST /staffops/reporting/raid/risks]: permitted (Other, 404), but the contract forbids it`

The 404 is the instructive part: the POST got **past** the authorization gate and only then failed on a placeholder key. Clearing the gate is the defect regardless of what happens afterwards. The mutation was reverted.

### An intermittent failure, explained but not closed

One run failed three boundary tests with `Expected: Allowed, Actual: Other` on reporting GETs. It did not reproduce in isolation, in the full suite, or in later runs.

The likely cause is benign and specific to a shared working tree: all three failures were reporting pages, and another agent had unsaved edits to `Views/StaffOps/Reporting/Index.cshtml` and `Cost.cshtml` at that moment. Razor compiles views at runtime in Development, so editing them mid-run can transiently 500. CI builds a static checkout and cannot hit this.

It is recorded rather than declared fixed. Diagnosis was improved so a recurrence is identifiable: `AccessOutcome.ServerError` classifies 5xx separately, so the application *failing* can never read as an allowance or a denial, and allowed-path assertions now name the route and status code. Deliberately not retried — masking instability would be worse than a loud failure.

### Also completed

`StaffApprovalsController` and `StaffPortalController` moved to capability gates. That closed a real gap: decision 4 (Team Lead gains `ApproveLeave`) existed in the model but was **not enforced**, because the controller still gated on `IsHolidayApproverOrAdminAsync`.

### Still open

- **Cross-tenant body leakage** — only cost leakage is proven over HTTP; a second tenant with its own sentinel is not seeded.
- **Remaining migration** — contracts, branding, staff admin, identities, connections, cost and sync still use `IsAdminAsync`. Behaviour matches the capability map today, so this is tidiness rather than a defect.
- **Three unrouted capabilities** — `ViewTeamCapacity`, `ViewEstimatorHistory`, `ManageEstimateCalibration` are asserted only by the pure tests (`ManageCustomers` gained a route, `/staffops/programme/repositories`, on 26 September 2026). Each carries a stated reason in `capability-routes.json`, and the contract test fails if a reason is missing.
- **Write allowances are not matrix-tested.** The matrix asserts write *denials* over HTTP; proving a write succeeds needs seeded programme data the fixture does not create yet.

---

## 13. Consolidation: migration completed, 20 September 2026

Both workstreams are merged on `main` and the capability migration is finished. **526 tests pass** against a real database. The persona gate reports PASS on environment, journeys and evidence, and stays RED only on three named gaps.

### The legacy predicates are gone, deliberately

Every controller, view and service now asks for a `Capability`. With no production callers left, the seven `Is…Async` predicates were **removed** rather than kept — a second way to ask the same question is how two definitions drift apart, and leaving them would have let the next feature reintroduce the pattern the model was built to replace.

Removing them turned drift into compile errors, which immediately exposed hollow tests: eight controller tests set flags like `IsAdmin = true` that nothing read any more. They would have kept passing while no longer testing the boundary they were written for. `FakeStaffAuthorizationService` is now capability-based, and `ForRole(StaffRole.TeamLead)` reads the **real** matrix, so a test saying "a Team Lead" keeps meaning whatever the product says a Team Lead is.

### Two defects found while finishing the migration

- **A Team Lead could not actually approve leave.** Decision 4 was applied to `StaffApprovalsController`, but `LeaveApprovalService` independently gated on `IsHolidayApproverOrAdminAsync`. A Team Lead passed the controller and was then rejected by the service. Both now check `ApproveLeave`.
- **The active-profile requirement had no test at all**, and was briefly lost during a working-tree incident without anything noticing. `PersonaBoundaryTests` now deactivates a signed-in persona's profile and asserts the *same session* loses access immediately, then regains it on reactivation. Verified by mutation: removing the check fails the test.

### Test-database isolation

`EnsureTestDatabaseExists` now creates whichever database the effective connection string names. Honouring `PP_TEST_SQL_CONNECTION` for connecting while creating the hardcoded default left that database absent and failed every integration test — which is exactly what happened when two worktrees shared one database and their migration states diverged. **Give each worktree its own database.**

### Still open

- **Cross-tenant body leakage** — only cost leakage is proven over HTTP.
- **Four unrouted capabilities** — asserted by the pure tests only; each carries a stated reason the contract test enforces.
- **Write allowances** — the matrix asserts write denials over HTTP, not write successes.

### What the incident changed about how this work is done

A `git reset --hard` intended for a temporary worktree ran in the main repository, because it was chained after a command that could fail and the chain used `;` rather than `&&`. It moved `main` and discarded another agent's uncommitted work. Everything was recovered from the object database, but the lessons are cheap to state and worth keeping:

- **Commit before integrating.** Uncommitted work is the only kind that can be lost; the first step of this consolidation was committing the other workstream untouched.
- **Never chain a destructive command behind one that can fail**, and never run `--hard` in a tree holding someone else's work. A `git worktree` checkout does the same job with no blast radius.
- **One agent, one worktree, one test database.** Shared trees caused a transient Razor-recompilation failure, a migration-state collision, and an index that mixed two authors' work so thoroughly that "commit only my changes" could not be done cleanly.
