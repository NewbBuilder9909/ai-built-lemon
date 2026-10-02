# Claude build handoff: staff skills and engineering evidence

20 September 2026. This is a build brief, not a claim that the module exists.

## Paste this task to Claude

> Implement the staff skills and engineering evidence module in ordered, reviewable slices. Read `docs/staff-skills-evidence-module.md` as the product and data contract, then read `CLAUDE.md`, `docs/tenancy.md`, `docs/data-governance.md` and the current authorization/capability code. Begin **now** with Slice 1 below: a tenant-scoped, manually maintained skills matrix with a proficiency rubric, staff self-view/correction, manager/admin review, audit, GDPR export/erasure participation and focused tests. Deliver working code and a concise report of files changed, tests run and remaining gates. Do not substitute a design document for Slice 1. After Slice 1 is green, continue with Slice 2 only if there is a safe, separate implementation commit and no conflict with another agent's files.
>
> You own implementation and its tests for this feature. Codex is handling design/review and will not edit your test files while you build. Use a dedicated branch/worktree; inspect status before changing Git state and preserve untracked files. The design and this handoff are currently untracked in the main working tree, so read them from the shared workspace before creating a worktree or copy them into your branch. Do not use `git reset --hard`, force-move `main`, prune Git objects or alter another agent's worktree. Create new test files under `ProgrammePulse.Tests/SkillsEvidence/` where possible. If a new capability requires a shared persona contract or existing test file, make that edit in its own focused commit after checking for concurrent edits.

## Current codebase facts to verify at start

- `Models/Staff/StaffProfile.cs` has free-text job title, department and team; there is no skills taxonomy or proficiency store. `docs/archive/solution-maturity-reassessment-2026-09-16.md` previously proposed a manually entered skills matrix.
- Persistence is Umbraco/NPoco migrations, not EF Core. `CLAUDE.md` calls for a separate migration plan for a new feature area. Use `SkillsEvidence_*` tables and a `SkillsEvidence` migration plan/composer rather than expanding the existing `StaffOps` plan without a clear reason.
- Authorization now uses `Models/Staff/Capability.cs` and its persona contract. Read the current implementation; portions of `CLAUDE.md` predate the capability migration. Define separate self, reviewer and team coverage actions if the existing capabilities cannot express them. Update the persona contract deliberately for every new capability.
- Tenant context, audit, `IStaffDataParticipant`, feature gates, and explicit `ExternalIdentityLink`/unresolved identity handling already exist. Reuse their patterns, not their assumptions about a single provider account. Keep staff evidence out of the commercial/rate data path.
- `docs/agent-delivery-build-gate.md` describes a separate agent-delivery replay gate. It is not evidence that this feature or a vendor connection is live.

## Slice 1 — manual skills matrix: implement first

1. Define a small, versioned skill taxonomy with kinds such as language, framework, component, practice and domain. Define a plain-language proficiency rubric and a review date. Do not infer proficiency from activity.
2. Add tenant-scoped `SkillDefinition` and historical `StaffSkillAssertion` persistence, with uniqueness and cross-tenant reference checks at write time. Keep the current assertion easy to query without discarding prior reviews. Avoid using nullable tenant IDs for new rows.
3. Add an admin/reviewer workflow to create skills, validate or reject a staff assertion, and record reviewer, timestamp, rationale and audit event. Add a staff view for their own assertions and a correction/request flow. Aggregate coverage can be read by the authorized team role; personal evidence requires narrower access.
4. Integrate new person-level records with the existing GDPR export/erasure participant mechanism, retention policy and tenant lifecycle. Never export another tenant's assertions. Resolve what remains as a non-personal aggregate after erasure before implementing it.
5. Add focused tests in new `SkillsEvidence` test files for same-tenant CRUD, cross-tenant ID rejection, self vs reviewer access, correction/history, audit and export/erasure. Update shared persona fixtures only if authorization additions require it, with exclusive file ownership for that commit.

**Slice 1 exit:** a staff member can see and challenge their own skills; a permitted reviewer can validate them; another tenant cannot read or mutate them; the current assertion has provenance and history; GDPR and audit work; build and relevant tests pass. Show screenshots or route examples only after the application boots with the migration. No repository or support data is required for this exit.

## Slice 2 — GitHub repository evidence

1. Add a selected-repository, read-only GitHub App installation connection. The tenant admin must choose repositories and see the granted account. Bind OAuth/install state and callback to the initiating tenant/session; encrypt credentials; reject arbitrary API hosts. Do not inherit deployment-wide credentials as a tenant fallback.
2. Add a provider-neutral engineering evidence contract with `connectionKey` and source account in every stable key. Ingest default-branch commits, merged PRs, PR file paths and reviews incrementally; track pagination, cursor/window overlap, rate limits, complete-run status and stale/partial coverage. Store metadata and links, not code bodies or raw diffs.
3. Keep commit author, committer, PR author, co-author and reviewer roles distinct. Ignore bots for person attribution. Map external accounts to staff only through an explicit approved tenant/connection identity link; ambiguous and unmatched actors go to a visible queue. Do not use display-name or email-only matching to publish a staff profile.
4. Suggest language/component experience from changed file paths with generated/vendor files excluded, but keep it as unvalidated evidence. Do not silently create or raise a proficiency assertion, individual score or defect attribution.
5. Test idempotent replay, multi-tenant and multi-account identity separation, partial/failed pages, permission loss, force-push/squash examples, bot handling, source deletion/disconnect and GDPR propagation.

**Slice 2 exit:** a reviewer can open a staff evidence portfolio with source links and a clear observation window; unmapped actors and incomplete sync are visible; no automatic quality or value rank is exposed. A sandbox or fixture replay is labelled as such until a tenant installation is exercised end to end.

## Slice 3 — support desk evidence

1. Build the provider-neutral `SupportCaseFact` contract and choose the first live adapter from the pilot customer's desk. If there is no customer choice yet, build Zendesk as the reference adapter because it has a documented cursor export, then add Zoho Desk and Freshdesk behind the same contract.
2. Import only ticket metadata needed for service health: IDs, timestamps, status, priority, approved component/category tags, reopen and deletion state, and source link. Do not store ticket body, attachments or customer contact details in Silver. Keep each vendor's credentials, account identity and cursor tenant bound.
3. Add explicit issue/PR/release relationship links and a reviewed RCA state. A matching issue key is a relationship, not proof that a commit caused a ticket. Give incident resolvers and reviewers their correct role; do not assign blame to code authors by temporal proximity or `git blame`.
4. Show support demand, recurrence and resolution trends by component/team with period, severity, exposure and missing-link coverage. Test incremental replay, reopens, deletions, partial results, rate limits, unknown component tags and cases unrelated to code.

**Slice 3 exit:** the connected desk reports honest service trends and links to source cases; any person-level contribution or causal finding has an auditable review. A customer can use this view without connecting Git.

## Slice 4 — reviewed coverage and GTM readiness

- Add manager-approved component ownership and backup coverage. Flag single-maintainer exposure as a business continuity risk, with source coverage and last review date. Suggest training, pairing or runbooks as actions with owners and follow-up outcomes.
- Validate sample attribution and support links with a design partner, including pair work, squash merges, inherited code and non-code support demand. Document false positives, unknowns and correction flow.
- Check tenant isolation, least privilege, credential protection, data minimization, retention, disconnect/deletion, GDPR, access audits and stale/partial sync before customer enablement. Record the customer's worker-notice, lawful-basis and DPIA decision before collecting person-level evidence.
- Use a GTM claim about **reviewed skills, expertise coverage and service improvement**. Do not claim automated developer valuation, proven individual code quality or automatic identification of who caused support cases.

## Delivery discipline

- Keep each slice independently buildable and migration-safe. Run `dotnet build`, focused tests, relevant full-suite tests and `git diff --check`; report any test that could not run. Verify migration startup against the configured local database when available. A fixture-only connector test is not a live vendor validation.
- Put each slice in a separate commit or reviewable commit group. Include a short release note stating what is live, what is behind a feature flag, what was tested and which customer configuration is still needed.
- Maintain separate file ownership while Codex or another agent is active. Do not modify existing shared test files concurrently. If a shared capability/persona file must change, coordinate the handoff before editing that file; keep all other work moving in new feature files.
