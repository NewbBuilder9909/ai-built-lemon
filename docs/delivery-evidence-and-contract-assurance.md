# Estimated effort and contract assurance: design

**Status:** 26 September 2026. **Steps 1 and 2 are built**: declared repository links (`/staffops/programme/repositories`) and the obligations register with attestation-based assurance (`/staffops/contracts/assurance`). **Step 3 is built but not yet run against the live workspace** (see "Step 3 as built" below). Step 4 does not exist yet. Decisions are recorded at the end.
**Areas affected:** Programme Ops (Silver), Skills and Evidence (repository evidence), Contract Ops, Staff (roster and leave).

This covers two requests:

1. **Estimated effort.** Where people don't log time, estimate how their available time was spread across projects from the code they worked on, without pretending that code activity measures time.
2. **Contract assurance.** Check engineering practice against what each contract requires. The motivating case: some customers' contracts forbid shipping new High or Critical security findings (as reported by Aikido), so those must be blocked for their code and visible when they are not.

Both depend on one thing the product doesn't have yet: a declared link from a code repository to the project it serves.

## Step 1: declared repository links (the shared foundation)

Repository evidence (`SkillsEvidence_EngineeringEvidence`) records which repository an artefact came from, but nothing says which project, and therefore which customer and contract, that repository serves.

**The link is declared by an Admin, never inferred.** A repository name that resembles a project name is a hint at best; a wrong inference would move effort and contractual obligations onto the wrong customer. This follows the product's existing rule for component ownership and identity links.

| Aspect | Decision |
|---|---|
| Identity of a repository | `(provider, sourceAccountId, repositoryKey)`, the same triple `EngineeringEvidence` carries. GitHub keys are `owner/name`; Azure DevOps keys are `project/repository` under an organisation, so the account is needed to make them unique. Stored lower-case. |
| Target | A Programme Ops **project**. Programme, customer and (through customer and term) contract follow from it. |
| Cardinality | Many-to-many. A shared library can serve several projects and customers, and then **carries every one of their contractual obligations**. For effort, it is flagged as shared rather than split by guesswork. |
| History | A link is ended, not deleted (`removedAtUtc`, `removedByStaffKey`). A filtered unique index keeps one live link per repository and project. |
| Tenancy | `tenantId` non-nullable, in every query. The project must belong to the same tenant. |
| Area | Programme Ops, table `ProgrammeOps_CodeRepositoryLink`. The repository is a plain string, never a typed reference into Skills and Evidence, so Programme Ops stays independent of the evidence area (the same rule as `SupportCodeLink`). |
| Who | `ManageCustomers`: the same people who decide which programme belongs to which customer. |
| Audit | Every link and unlink is written to the Programme Ops audit trail. |
| Gap report | The page lists repositories the evidence area reads that no project claims. The controller composes the two areas, as the skills page does with support participation. |

## Part A: estimated effort

### The principle

**Commits can't measure time, but they can split time you already know about.**

- **Hours come from the roster.** For each person and working day, available hours are contracted hours (`StaffOps_WorkHoursHistory`) minus approved leave and sickness (`StaffOps_Availability`). This is the capacity the Reporting Hub already computes. It is a hard ceiling: no estimate can exceed it.
- **Repository evidence decides only the split.** A person is *active on a project on a day* if they authored, reviewed or merged an artefact that day in a repository linked to that project. That day's available hours are divided equally across the projects the person was active on.
- **Silence is not zero, and it is not spread.** A day with no evidence stays **unattributed** and is reported as such. Meetings, design, support and incident work leave no commits, so this gap will often be large. Showing it is what keeps the estimate honest. It follows the same rule as Delivery Load, where missing data is never read as lighter load.

### Why active days, not commit counts

Commit counts reward people who commit often. They are distorted by squash merges and force-pushes, and they make reviewers look idle. A day-level presence signal is coarse on purpose: it only claims "worked on this project that day".

### Data model

A new Silver type, `EstimatedEffort`, is **never a `TimeEntry`**. That follows the existing rule that `PlannedAllocation` is never delivery.

| Field | Meaning |
|---|---|
| staff, project, date | The cell being estimated |
| hours | That day's available hours ÷ number of active projects |
| method | e.g. `ActiveDaySplit/v1`, so a later method never silently changes history |
| confidence band | e.g. single-project day = firmer; multi-project or shared-repository day = weaker. A band, never a percentage |
| evidence refs | The artefacts that made the day active, for drill-through |

### Hard rules (to be test-enforced)

1. **Estimated effort never reaches an invoice.** Time & Materials billing uses confirmed time only.
2. **The confirmation step is the only way an estimate becomes time.** Each week a person may be offered a proposed split to accept or edit. Accepting creates a `TimeEntry` with its own source (e.g. `ConfirmedEstimate`), which is the person's own record, not an inference.
3. **The per-day estimate never exceeds roster capacity**, and is zero on approved leave.
4. **Estimates are labelled** wherever they appear in margin, budget burn or delivery views, and are never summed with logged time without saying so.
5. **Project and contract totals by default.** A person-level view is for the person and their manager only.
6. **Gated by the processing decision.** This is worker monitoring. It requires the same current lawful basis, staff notice and DPIA record that already gates evidence collection (`SkillsEvidence_ProcessingDecision`), and it must fail closed without one.

### What may be said about it

"Estimated allocation of available time, from repository activity." Never "time tracking", "hours worked" or "productivity". Add this to `docs/skills-evidence-gtm-claims.md` before anything is shown to a customer.

### Known limits

- The GitHub and Azure DevOps evidence paths have never run against a live installation. Until they do, estimates are replay-only and must be labelled so.
- Work outside linked repositories (other tools, documents, calls) is invisible and lands in "unattributed".

## Part B: contract assurance

### Obligations register

Contracts today hold commercial terms only. Add `ContractOps_Obligation`, one row per clause that has an engineering consequence:

| Kind | Example parameters |
|---|---|
| `SecurityFindingGate` | severity threshold (High, Critical); action `Block` or `RemediateWithin` N days |
| `DataResidency` | allowed regions |
| `Encryption` | at rest, in transit |
| `DesignControl` | free-text control with an evidence expectation (e.g. "SSO via customer IdP") |

Each obligation has a clause reference, effective dates (within the contract term) and a named owner. Through contract → customer → programmes → projects → **linked repositories** (step 1), every repository knows which obligations apply to it. A shared repository inherits the strictest.

### Where blocking happens

**The block belongs in CI, not in ProgrammePulse.** Merges happen in GitHub or Azure DevOps. The enforcement point is the security tool's pull-request check (Aikido's CI integration) made mandatory by branch protection.

ProgrammePulse's GitHub App is **read-only by design**. Giving it write access so it can post status checks itself would change the product's security stance and the customer's grant. That is a decision to take explicitly, not a default.

What ProgrammePulse does, read-only:

- **Drift detection.** "Repository X serves a customer whose contract blocks High findings, but its default branch does not require the security check." Reading branch protection needs read access to repository administration metadata, which is another scope change to confirm per provider.
- **Assurance.** Did findings above the threshold reach the default branch while the obligation applied? Were they fixed within the contract's deadline?

### Findings ingestion (Aikido first)

The standard Bronze → Silver pattern:

- **Bronze:** Aikido's API captured verbatim. Endpoint names, fields and rate limits are to be confirmed against Aikido's documentation; nothing here assumes them.
- **Silver:** `SecurityFinding`, metadata only: tool, severity, rule or CVE id, repository, first detected, fixed, and the introducing pull request where the tool provides one. **Never code or snippets**, the same rule as engineering evidence.
- **Own feature area and `ProductFeature`**, so a customer can use contract assurance without person-level evidence. That's the same reason Service Ops is separate.

#### What Aikido's API provides (checked against its documentation, 26 September 2026)

Aikido is now connected to the user's GitHub. These facts come from Aikido's published API reference. `scripts/probe-aikido.ps1` checks each one against the real workspace before any connector code is written.

| Need | Aikido API | Notes |
|---|---|---|
| Authentication | OAuth 2.0 client credentials: `POST https://{region host}/api/oauth/token`, Basic auth with client id and secret | Credentials are created by a workspace admin on Aikido's REST API integration page. Regional hosts: `app.aikido.dev` (EU), `app.us.aikido.dev`, `app.au.aikido.dev`, `app.me.aikido.dev`. Confirmed live: bad credentials get 401 |
| Findings | `GET /api/public/v1/issues/export` (scope `issues:read`) | Filters by status, severity, type and repository; up to 5,000 per page. Fields include `severity`, `status`, `type`, `code_repo_id`, `first_detected_at`, `closed_at`, `cve_id`, `affected_package`, `sla_remediate_by`, `exploitability`. Metadata only is kept: `affected_file` and line numbers are not needed and are not stored |
| Repositories | `GET /api/public/v1/repositories/code` (scope `repositories:read`) | `provider`, `external_repo_id`, `url`, `branch`, `connectivity`. GitHub `owner/name` comes from `url`, which is how findings join to the declared repository links |
| **Is the gate on?** | `GET /api/public/v1/repositories/code/continuous_integration/checks` (scope `repositories:read`) | Per repository: `minimum_severity` and `fail_on_dependency_scan`, `fail_on_sast_scan`, `fail_on_secrets_scan` and others. This is the "security check required" control, **observed**, not attested |
| **Did the gate hold?** | `GET /api/public/v1/report/ciScans` (scope `reports:read`) | Each PR check's `gate_status` (passed, failed, **bypassed**, timed_out), repository, commit and PR link. A bypassed check on a "block" repository is the breach a block clause cares about |
| Rate limit | 20 calls per minute per workspace (50 on request) | 429 with `Retry-After`. A tenant's sync must be paced, and is incremental after the first run |

**Confirmed against the live workspace (probe runs of 26 September, EU region, 2 GitHub repositories, 133 issues):**

- **Pass:** token (lifetime 3,600 s), repositories, the issues export, repository on every code issue, `first_detected_at` on every issue, and server-side status and severity filters.
- **Repository identity:** Aikido gives GitHub repositories in the **API URL form** (`https://api.github.com/repos/{owner}/{name}`), not the web form. The join to repository links derives `owner/name` from that form with an anchored pattern, and refuses two repositories that derive the same key. (A looser first attempt read `repos/{owner}` as the name for both repositories; the probe now fails on that.) `external_repo_id` is GitHub's node id (`R_kgDO…`), which survives renames, so it is stored alongside `owner/name` to detect a renamed repository rather than orphan its findings.
- **The gate is off today:** no repository-specific PR-check configuration, no PR check has run, and the workspace default is disabled (fail on Critical only; dependency, SAST and secret checks off). A repository in this state is recorded as gate **not enforced (observed)**, never unknown or met.
- **Aikido "ignored" is not a contractual risk acceptance:** 42 of the 133 issues are ignored in Aikido. They are shown separately and never count as resolved; only a named, expiring risk acceptance would do that, and that record is **not built yet** (see Exceptions).
- **Still to confirm live:** `closed_at` on a closed issue (none closed yet), and the PR-check run fields, once PR checks are enabled and a pull request is opened.

**This improves decision 2.** Whether a repository's gate is on can be read from Aikido's own configuration, with read-only Aikido scopes and **no additional GitHub permission**. An observation from Aikido supersedes a manual attestation for that repository and control. Attestation remains for repositories Aikido doesn't cover, and for tools other than Aikido.

**What step 3 will be able to say, per contract:** open High and Critical findings in in-scope repositories, with age against the remediation deadline; whether each in-scope repository's Aikido gate blocks at the contracted severity (observed); and any PR check bypassed or timed out while the obligation applied. It still can't prove a finding was never merged in a repository Aikido doesn't scan, so unscanned repositories stay "unknown".

### Step 3 as built (26 September 2026)

Its own feature area, `SecurityAssurance`, with its own migration plan (`"SecurityAssurance"`, step `2026-09-securityassurance-01`), composer and `ProductFeature.SecurityAssurance` (Enterprise). Contract Ops reads it through one tool-neutral snapshot (`ISecurityAssuranceQueryService`); the dependency runs Contract Ops → Security Assurance only, and `SourceIndependenceTests` bars both areas from Aikido's namespace.

- **Connection** at `/staffops/security` (`ManageIntegrations`; sync is `TriggerSync`). Client id and secret are verified against Aikido before they are stored, encrypted under their own Data Protection purpose, with no deployment-wide fallback. Regions are an allow-list (`AikidoRegions`), checked on every request; no redirects, no cookies.
- **What a sync reads:** repositories, per-repository PR-check configuration, every issue (all statuses, so closures arrive), and one page of recent PR-check runs. Bronze is **allow-listed, not verbatim**: each page is reduced to named fields before it is kept, because Aikido's issue payload carries file paths and line numbers. Pages are purged after 30 days.
- **A partial read never replaces observations** and never records a clean sync. Observations are replaced only after a complete read, so a repository Aikido stops listing drops to its attestation (or unknown) rather than keeping a stale gate. Findings of a repository no longer listed are left out of the snapshot.
- **How it changes the assessment** (`ObligationAssurance.Evaluate`, pure and unit-tested):
  - For a repository Aikido lists, the observation **supersedes** the attestation. Repositories Aikido doesn't list keep their attestation.
  - *Block before merge:* in place only if the gate is configured, fails at the contracted severity or lower, and fails on dependency, code and secret findings. No configuration, a check set to never fail, or "Critical only" against a "High and Critical" clause is **not in place (observed)**.
  - *Fix within N days:* the repository must have been scanned, and no finding at the contracted severity may be left not closed past `first_detected_at + N days`. That is a new status, **findings overdue**, which counts as a gap. Ignored and snoozed count.
  - *Bypassed or timed-out PR checks* since the contract started are listed under the obligation, labelled **Unverified**, and do not change the status. `SecurityAssuranceSnapshot.CheckRunsVerified` is hard-coded false until a probe confirms the check-run fields from a real gated pull request.
- **Not built:** the `RiskAcceptance` record below, the "introduced in PR" relationship, and any second tool.
- **Tests:** mapper, client (scripted HTTP), ingestion, connection and evaluation tests, plus `SecurityAssuranceRepositoryIntegrationTests` on real SQL (idempotent replay, in-place status change, observation replace, tenant isolation). Mutation checks caught: treating ignored findings as resolved, replacing observations after a partial read, and narrowing the outstanding-findings SQL to `open`.
- **Before anyone relies on it:** connect the real workspace, run a sync, and compare the page with Aikido's own view.

#### Live acceptance (owner: you; engineering reviews the result)

1. Run `scripts/probe-aikido.ps1`. Besides K01–K14 it prints **"Expected on /staffops/security"**: per repository, the gate as ProgrammePulse will describe it and the High/Critical findings not closed (open, ignored, snoozed), computed with the connector's own rules.
2. At `/staffops/security`, connect with the same read-only client and press **Sync now**. The message must say the read was complete.
3. Compare the page's repository table with the probe's table. **Pass:** every row matches. A difference is a defect; send the probe's results file (it holds no secrets or code).
4. For the contract page to show anything, you also need: each repository linked to the project it serves (`/staffops/programme/repositories`), an **Active** contract whose term covers today, and a security obligation on it (`/staffops/contracts/{key}/obligations`).
5. **Pull-request checks:** enable Aikido's PR check on at least one repository (minimum severity High; dependency, SAST and secrets on), make it required in GitHub branch protection, and open one pull request. Rerun the probe. **When K14 passes**, `SecurityAssuranceSnapshot.CheckRunsVerified` can be switched on and the "Unverified" label removed.

### Exceptions

A breach may be knowingly accepted, for example with no fix available and compensating controls in place. That needs a `RiskAcceptance` record: obligation, finding(s), named approver, rationale, expiry. Superseded rather than edited. This is the same pattern as `ConfirmedRootCause` and the processing decision. An expired acceptance puts the breach back on the report.

### Design decisions

The same structure covers non-security clauses. Each obligation maps to one or more **controls**: an architecture decision record, a test, a scan or a configuration. Each control has **evidence** and a **status**. The per-contract view is a traceability matrix: clause → control → evidence → status. It may be worth reusing the Executive Review decision records, which already carry evidence disposition.

### Hard rules

1. **Findings attach to repositories and pull requests, not people.** "Introduced in PR #123" is a relationship, never a per-developer blame count. This follows Service Ops' `SupportLinkMethod` rule.
2. **An obligation is only as good as the link.** A repository with no declared link is reported as *unassessed*, never as compliant.
3. **Compliant means evidenced.** A contract with an obligation but no ingested findings or controls shows *no evidence*, not a green tick.

## Sequencing

| Step | Delivers | Risk |
|---|---|---|
| 1 | Declared repository → project links, audit, gap report | Low: Admin data entry, no new external access |
| 2 | Obligations register and drift report | Low; drift detection needs a read-scope confirmation |
| 3 | Findings ingestion (Aikido) and per-contract assurance | Medium: needs an Aikido account and API key; untestable here without one |
| 4 | Estimated effort, then the weekly confirmation step | Higher: worker-monitoring privacy obligations; depends on live evidence |

## Decisions (26 September 2026)

You asked for a recommendation on each, from an architecture and a business-process point of view. These are **adopted as the working defaults**, and step 2 is built on them. Each says when it is worth revisiting. Overrule any of them and the design changes as stated.

### 1. Should ProgrammePulse post pull-request checks? **No: report, don't enforce.**

| | |
|---|---|
| Decision | ProgrammePulse never gets write access to a customer's repositories. Blocking stays with the customer's own tooling: Aikido's pull-request gate, made mandatory by branch protection. ProgrammePulse is the **source of the policy** (which repositories must be gated, and why) and the **assurance** that the gate was in place and held. |
| Why (architecture) | Least privilege: every repository grant today is read-only, and a customer's security team will approve a read-only reporting tool far more readily than one that can alter merges. It also keeps ProgrammePulse out of the delivery path: if a merge gate depended on it, an outage here would block every customer's releases, or teams would bypass it, which is worse. And Aikido already does the gating well; duplicating it adds a second enforcer that can disagree with the first. |
| Why (process) | Accountability stays where the control operates. The customer's engineering lead owns the gate; the contract owner owns the obligation; ProgrammePulse evidences the link between them. A tool that silently enforces blurs who decided. |
| Revisit when | A customer contractually requires the supplier's tooling to enforce, or asks for it. Then build a *separate*, opt-in "enforcer" app with its own grant, so the reporting app's permissions never change. |

### 2. Should ProgrammePulse read branch protection? **Yes, eventually, but start with attestation.**

| | |
|---|---|
| Decision | Step 2 records whether each repository's gate is in place as a **dated attestation**: who said so, when, with what evidence reference (a screenshot of the branch rule, an Aikido settings export), expiring after 90 days. Automated reading comes later, as an **observation** that supersedes the attestation for that repository. |
| Why (architecture) | The data model is the same either way (a control's state, its source, when it was established, when it lapses), so starting manual costs nothing later. The permission question is real: GitHub's classic branch-protection API needs administration read access, while its newer rulesets API may need less. That must be confirmed against GitHub's current documentation, and the Azure DevOps equivalent (branch policies) checked, before asking any customer for more scope. |
| Why (process) | An expiring attestation is how assurance works in practice (ISO 27001 and Cyber Essentials evidence ages the same way). It gives the contract owner something true today, and a reason to ask again in 90 days. **"Unknown" is never shown as compliant**: an expired or missing attestation reads as unknown. |
| Revisit when | Two or more customers want continuous assurance, or the attestation workload becomes material. Then request the narrowest read scope that works, as an opt-in per connection. |

### 3. Who sees person-level effort estimates? **The person always; their manager only if the privacy decision covers it; nobody else. The weekly confirmation is offered, opt-in per tenant.**

| | |
|---|---|
| Decision | Estimates are shown at project and contract level by default. A person always sees their own. Their line manager sees them only when the tenant's recorded processing decision explicitly lists "effort estimation" as a purpose. Peers and other teams never see them. The weekly confirmation is offered per tenant (off by default). It proposes a split each week, and the person accepts or edits it by a set deadline. Unconfirmed weeks stay estimates; **nothing is ever auto-confirmed**. |
| Why (architecture) | Confirmation is the only clean way inferred data becomes billable. It moves the record from "the system's guess" to "the person's own statement", which is also what makes it defensible to a customer disputing an invoice. |
| Why (process) | UK data-protection guidance on monitoring workers expects transparency and proportionality. Showing people their own data first is both, and it catches errors early. A fixed rhythm (proposal Friday, confirm by Tuesday) fits how weekly timesheets already work. Estimates must never feed a performance process; that needs a separate lawful purpose and is not offered. |
| Revisit when | A tenant wants estimates for capacity planning across teams (aggregate only), or confirmation rates show the rhythm doesn't fit. |

### 4. Invoice date rule. **Invoice by report date, and add a draft stage.**

| | |
|---|---|
| Decision | Invoice selection uses the same date as contract costing: the provider's work date, else the UTC start date. That fixes Tempo time never being invoiced. Only entries with **known** billability are billed; unknown-billability hours are listed on the draft as "needs review", not silently dropped. Invoices gain a **Draft** stage before Issued: generated, reviewed, then issued. |
| Why (architecture) | One date rule for costing and billing means one number reconciles to the other. Two rules guarantee a margin report and an invoice that disagree about the same hours. |
| Why (process) | Today an invoice is issued the moment it is generated, with no review. Billing errors are the costliest kind to correct once a customer has seen them. Draft → review → issue is the standard control, and it is where "needs review" hours get decided. |
| When | A small change on its own: queued next after step 2. **Built 26 September 2026** (ContractOps step 06); see [contract-ops.md](contract-ops.md). |

### 5. Aikido access. **Get a sandbox before building step 3; build step 3 tool-neutral.**

| | |
|---|---|
| Decision | Step 3 does not start until there is an Aikido workspace with read-only API credentials and a test repository deliberately seeded with a known-vulnerable dependency, so High and Critical findings exist to test against. The findings model is tool-neutral from the start (severity, rule or CVE, repository, first seen, fixed, introducing pull request), with Aikido as the first adapter. |
| Why | Every integration here that was built without a live account (GitHub, Azure DevOps, Freshdesk) is still labelled "never exercised live". Security findings will be read by customers as assurance, so they need a real run before anyone relies on them. Tool-neutral, because customers already use Snyk, Dependabot and GitHub code scanning; the obligation shouldn't care which tool found the issue. |
| Owner | You: the account and a sandbox. Engineering: the adapter, once it exists. |

### How the obligations process runs (step 2 onwards)

| Activity | Responsible | Accountable | When |
|---|---|---|---|
| Record a contract's engineering obligations from its clauses | Contract owner | Commercial lead | At signature and at every contract change |
| Link each repository to the projects it serves | Delivery lead | Contract owner | When a repository is created or reassigned |
| Attest that each gated repository's control is in place | Engineering lead of the repository | Delivery lead | At link time, then every 90 days |
| Review the assurance report: unknown, expired or not-enforced repositories | Contract owner | Commercial lead | Monthly, and before each customer governance meeting |
| Accept a risk where a control can't be met | Named approver | Commercial lead | As needed; expires in 90 days at most (step 3) |
