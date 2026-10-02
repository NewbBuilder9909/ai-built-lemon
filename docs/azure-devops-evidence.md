# Azure DevOps engineering evidence

23 September 2026 · Built on branch `feature/azure-devops-evidence`. **Not exercised against a live Azure DevOps organisation.**

Azure DevOps Services (dev.azure.com) is the second forge behind the Skills and Evidence area's provider-neutral `EngineeringEvidence` contract. It exists to reach Microsoft-led engineering organisations that GitHub does not cover. It writes the same Silver rows, coverage cursors and identity-mapping queue as the GitHub connector, so the portfolio, continuity view and suggestions read its evidence without any change above Bronze. No migration was needed.

Read [staff-skills-evidence-module.md](staff-skills-evidence-module.md) first. Every rule there applies unchanged. This note covers only what is different.

## What is collected

| Stream | Source | Evidence rows |
|---|---|---|
| Commits | Default-branch commits (`git/repositories/{repo}/commits`, windowed by `fromDate`) | Author, committer (when a different email) and `Co-authored-by` trailers, as separate roles |
| Pull requests | Completed PRs (`pullrequests?searchCriteria.status=completed`, windowed by close time) | PR author |
| Reviews | The reviewer list inline on each completed PR | One row per reviewer with a non-zero vote; the vote is recorded in the title ("approved", "rejected"…) |

Only metadata and links are collected: no diffs, file contents or commit bodies beyond the subject line. Language hints are empty because the list endpoints carry no changed paths. Empty means "not looked", not "no languages".

## How it differs from GitHub

1. **Authentication is a tenant-entered personal access token (PAT), not an app installation.** Azure DevOps' own OAuth no longer accepts new app registrations. Its successor, Microsoft Entra ID, is a separate identity decision. The token:
   - should have the Code (Read) scope only
   - is verified against the organisation before it is stored
   - is encrypted with the existing evidence protector and never displayed again
   - never falls back to a deployment credential

   The admin may record the expiry date, and a run after that date stops with a message naming it. A PAT reads whatever its owner can read, so the connect form recommends a dedicated read-only account.
2. **Repository selection happens in the product, not on the vendor's site.** The picker lists what the stored token can read right now. The save action re-reads that list and refuses any repository not on it; nothing is taken from the posted form alone.
3. **Commit actors have no account id.** Azure DevOps gives a commit's git name and email only. Commit rows are therefore keyed `email:…`, and a person's pull-request identity (a stable account id) is a separate queue entry. Linking one person may take two approvals. That is deliberate: an email and an account are never assumed to be the same human.
4. **Reviews are votes.** Reviewers with vote 0 (asked, never responded) and container reviewers (teams and groups) are dropped because they are not a person's review. One review row per reviewer per PR, so a changed vote updates the row.
5. **Build identities are bots.** "… Build Service (org)", "Azure Pipelines" and `Build\…` unique names are marked with the bot account type. They are excluded from attribution but remain visible on the queue.
6. **A refused token is recognised in every form Azure DevOps uses:** 401, a 203 sign-in page, a redirect, or a 200 carrying HTML. The HTTP client follows no redirects, so none of these can be read as an empty result. During a run, each is treated as lost permission, not as partial data.

## Guarantees re-asserted for this forge

These are covered by tests under `ProgrammePulse.Tests/SkillsEvidence/AzureDevOps/`:

- The processing-decision gate runs before anything is fetched.
- There is no credential fallback. An unreadable or expired credential stops the run.
- Replaying a page is idempotent on `(tenant, connection, sourceType, externalId, role)`.
- A partial page keeps its rows and does not advance `CompleteThroughUtc`.
- Lost access marks coverage `PermissionLost` and keeps collected evidence.
- Tenant isolation holds: another tenant's connection key reads as not found.
- Ingestion creates no skill assertion.
- Each ingestion service refuses the other forge's connection.
- Azure DevOps and GitHub never reference each other. The neutral contract references neither. No Azure DevOps service can reach `ISkillAssertionService`.
- The host policy admits `https://dev.azure.com/{org}` only. `*.visualstudio.com`, Azure DevOps Server, user-info, extra paths, queries and non-default ports are all refused, and the policy is re-checked on every request.

`Integration/AzureDevOpsEvidenceWiringIntegrationTests` also boots the real application. It checks that the container builds the connector and its controller, and that an anonymous request is refused rather than causing a 500. A signed-in render of the connections page is not covered, because it needs an Enterprise-plan tenant in the persona harness.

## Not established

- **No live run.** The REST shapes come from the api-version 7.1 reference, not from a real organisation. In particular, `searchCriteria.queryTimeRangeType=closed` / `minTime` on pull requests and the date format `fromDate` accepts need a sandbox check. The client also filters close time itself, so a server that ignores the parameter causes over-reading, not missed data. The existing "unverified replay" banner applies until a run completes cleanly.
- **No Entra ID / OAuth.** A PAT is pilot-grade. Rotation is manual: reconnect with a new token, and the repository selection is kept.
- **No Azure DevOps Server (on-premises).** Adding it would mean an operator-configured host allow-list, as GitHub Enterprise has.
- **Plan gating reuses `ProductFeature.GitHubEvidence`.** The flag governs person-level repository evidence from any forge. Renaming it would touch `PlanEntitlements` and the purchase page, and is left for a coordinated change.
- **Still request-triggered, not queued.** Like GitHub evidence, Azure
  DevOps now takes the shared `SyncRunCoordinator` lease/run-state path,
  so a second node cannot race the same tenant/source and the admin page
  can show running/failed/last-success status. What it still does not
  have is a background queue or scheduler: an Admin click starts the run
  directly.

## Live acceptance probe

`scripts/probe-azure-devops.ps1` is how the "no live run" gap above gets closed with evidence. It sends the connector's own read-only requests to a real organisation and checks each assumption the connector depends on:

| Check | Assumption |
|---|---|
| C01–C03 | A valid token is accepted; a refused token is recognised as refused (never as an empty result); an unknown organisation reads as not found |
| C04–C05 | The repository list and lookup carry the fields the connector reads |
| C06–C07, C14 | Default-branch commits are readable, `fromDate` filters by committer date, and paging moves forward |
| C08–C10 | Completed pull requests are readable, the close-time window is honoured, and paging moves forward |
| C11–C13 | Commit and pull-request identity shapes, build and service accounts (for the bot rules), and reviewer vote values |

Run it with a **Code (Read)** personal access token in `PP_ADO_PAT` (or at the masked prompt), then remove the variable:

```powershell
$env:PP_ADO_PAT = '<Code (Read) token>'
./scripts/probe-azure-devops.ps1 -Organisation acme -Repository 'Web/portal' -WindowDays 30
Remove-Item Env:PP_ADO_PAT
```

It is GET-only and touches no application code, database or configuration. The results file (under `artifacts/azure-devops-probe/`) records status codes, counts and pass/fail only; never the token, commit messages, PR titles or personal email addresses. `FAIL` means an assumption is wrong and the connector needs changing; `CHECK` means a person should look (for example, a window that excluded nothing proves nothing). Choose a window that contains some, but not all, of the repository's history. Keep the results file as the evidence when removing "not exercised against a live organisation" from this page.

## Files

New: `Services/Integrations/AzureDevOps/*`, `Models/Integrations/AzureDevOps/Raw/*`, `Controllers/StaffAzureDevOpsEvidenceController.cs`, `Views/StaffOps/Skills/Evidence/AzureDevOpsRepositories.cshtml`, `Models/ViewModels/SkillsEvidence/AzureDevOpsRepositorySelectionViewModel.cs`, tests.

Shared files touched, additively:
- `Composers/SkillsEvidenceComposer.cs`: registrations.
- `Services/Integrations/GitHub/GitHubEvidenceIngestionService.cs`: a provider guard, so an Azure DevOps connection can never be sent to the GitHub client.
- `Views/StaffOps/Skills/Evidence/Connections.cshtml`: per-provider sync button and the connect panel.
