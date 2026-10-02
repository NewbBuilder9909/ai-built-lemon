# Skills contribution examples — delivery handoff

26 September 2026. Branch `feature/skills-ai-evidence`, isolated worktree
`<local>/MyUmbraco18Project-skills-ai`, originally based on `02b5a33`.
Feature checkpoint `25c0af5`; Phase 2 commit `d7b2459` integrated locally in
merge `42a46ce`. The main worktree remains unchanged.

## What the feature does

A staff member can choose an existing skill assertion and a mapped contribution,
explain what they demonstrated, and optionally declare the AI tool and workflow
involved. A different staff member with ReviewStaffSkills can accept or reject
the example's relevance with a rationale. Acceptance never validates a skill
level. Corrections return the example to Submitted; withdrawal preserves history.

- Self-service: `/staffops/skills/examples`, linked from My Skills.
- Reviewer: `/staffops/skills/examples/staff/{staffKey}`, linked from the existing
  staff portfolio.
- Detail and history: `/staffops/skills/examples/{linkKey}`.
- All routes require the existing GitHubEvidence plan feature (also used by the
  existing provider-neutral evidence pages). The normal capability gates still
  apply. POST routes require antiforgery tokens; tenant comes from CurrentTenant.

Tool names are self-declarations, not inferred use or verified generated-code
percentages. Existing commit/PR/review roles stay visible. No commit-based hours,
productivity rank, automatic proficiency change or live AI API is introduced.

## Persistence and lifecycle

SkillsEvidence migration step `2026-09-skillsevidence-05` adds
`SkillsEvidence_ContributionReview`. Tenant is non-null from creation. Each event
stores link/revision, subject, assertion/evidence keys, state, explanation,
declared tool/workflow, actor and timestamp. A unique tenant/link/revision index
and transactional expected-revision check prevent lost updates. A filtered
unique index prevents duplicate initial links for the same assertion/evidence.

Revisions are immutable except the explicit erasure path. Concurrent stale forms
receive HTTP 409; the user must reload. Ordinary validation returns 400 with the
submitted fields. Source keys and assertion keys are checked against the tenant
and subject, including inside the write transaction. Unmapped/bot evidence,
disconnected connections and repositories removed from the selection are not
eligible. Revoked source attribution hides its link and prevents a new review;
withdrawal remains possible.

An example belongs to the exact assertion revision the subject selected. When
the assertion is superseded, the example remains historical and the page states
that it cannot establish evidence for the new revision. No automatic transfer
of a prior review decision occurs.

The list uses 20 current examples per page; the chooser offers 100 eligible
source records per page, newest first, with older/newer navigation. Both reads
use Phase 2's shared SQL paging convention and a unique ordering tie-breaker.
Validation errors preserve the selected contribution page and entered fields.
Changing contribution pages before submission clears an unfinished form; the
page explains this. Detail shows the latest 50 revisions and says when older
history exists. A subject export includes the entire history.

## Subject data

`ContributionReviewDataParticipant` participates in the existing export/erasure
pipeline. Subject export includes their notes, declarations and decisions;
reviewers export their own decision records without the subject's demonstration
text. Subject erasure deletes all their example revisions. Reviewer erasure
removes their identity and decision rationale while retaining the subject's
example and the decision state. Free-text notes are not copied to a second audit
table; the attributed revision stream is the review history.

## Verification

Use a dedicated database; never run this branch's migrations against Claude's
default test database or the development database while its branch is active.

```powershell
$env:PP_TEST_SQL_CONNECTION='Server=(localdb)\GardnerDB;Database=ProgrammePulse_SkillsAi_20260926;Integrated Security=true;TrustServerCertificate=true;'
$env:PP_REQUIRE_SQL_TESTS='1'
dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj -c Release
./scripts/check-views.ps1
```

The new integration suite covers revision history, unchanged proficiency,
cross-tenant/subject refusal, invalid decisions, concurrent review, duplicate
submission, source revocation, deselection/disconnection, superseded assertions,
erasure, paging, and an actual signed-in subject/reviewer journey with CSRF,
field validation and stale-form checks. No vendor connection is exercised.

Baseline verification before Phase 2 integration: 1,309 tests passed, zero failed or skipped; all 83 Razor views compiled successfully. Results used the dedicated database above, including a fresh migration run.

After integrating Phase 2: 1,329 tests passed, zero failed or skipped
(`artifacts/skills-ai-tests/skills-ai-phase2.trx`). The subsequent older-source
chooser regression covers more than 100 records, tied timestamps, subject and
tenant isolation, invalid page input, HTTP form recovery and successful
submission of an older contribution. All 84 Razor views compiled after the
chooser change, with zero warnings or errors.

Final focused verification after adopting the shared SQL pager: 42 architecture
and contribution integration tests passed, zero failed or skipped
(`artifacts/skills-ai-tests/skills-ai-paging.trx`).

## Integration with Claude's Phase 2 branch

The existing working tree was left untouched. There are no ProgrammeOps,
ContractOps, Staff repository, shared paging or stylesheet changes here.

The local merge retained these integration points:

1. `SkillsEvidenceComposer`: three registrations.
2. `SkillsEvidenceMigrationPlan`: append step 05. If another branch has added a
   later step, append after its tip and use a fresh state name; do not rewrite a
   step already applied to a real database.
3. `Views/StaffOps/Skills/Index.cshtml` and `Portfolio.cshtml`: one conditional
   navigation link each. Retain Claude's new paging code in these files.
4. `staffops-routes.txt`: seven added routes; retain all other branch changes.
5. New contribution controller/service/repository/view/test files are additive.

The combined suite and Razor check above verify the committed Phase 2 integration.
No changes were merged back into the main branch, pushed or deployed.
The main worktree subsequently started repository-to-project link work
(`StaffCodeRepositoryController` / `CodeRepositoryLink*`); those active changes
were neither copied nor modified here.

## Remaining scope

- Automatic source-bound AI observations from approved GitHub/Azure DevOps
  metadata, with correction/retraction provenance, are not wired to these pages.
  The initial observation contracts/projector remain tested foundations.
  Source inspection found an upstream prerequisite: the existing evidence
  upsert and unique key use tenant/connection/type/external ID/role, excluding
  actor identity. Multiple coauthors of one commit therefore target the same
  stored row. Preserve distinct actors and repository identity, with a migration
  and replay tests, before treating stored coauthor metadata as complete AI
  provenance. Existing overwritten records would require a source replay.
- Utilisation context should consume Claude's settled read interfaces; it is
  not inferred from this feature's evidence records.
- LinearB remains an optional adapter proposal, pending customer access and
  validation of the actual data granularity.
- Live-connector and design-partner validation, translation review and fuller
  visual UX validation remain separate from fixture-backed correctness.
