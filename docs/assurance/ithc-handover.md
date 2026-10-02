# ITHC assessor handover and operational acceptance pack

Draft, 25 September 2026. All unfilled fields are OPEN. This is not permission
to attack a system. Named system owners and the assessor must agree written
authorization and rules of engagement before active testing.

## Commissioning record

| Field | Required entry |
|---|---|
| Buyer, contract and security schedule | OPEN |
| Classification, applicable assurance/certification and PSN requirements | OPEN |
| Accountable risk owner / security lead / deployment owner | OPEN: names and deputies |
| Independent assessor and relevant CHECK status/team qualifications | OPEN: verify with buyer |
| Final commit, build run, artifact digest, environment configuration revision | OPEN: one immutable candidate |
| Domain/IP/cloud resource allowlist and ownership | OPEN: staging preferred, production differences declared |
| Third-party permissions and excluded services | OPEN: provider APIs are not automatically authorized targets |
| Dates, tester source IPs, rate/concurrency caps and permitted hours | OPEN |
| Emergency stop contact and stop conditions | OPEN: real-data access, unexpected outage, out-of-scope access |
| Destructive tests, social engineering, phishing and denial of service | Excluded unless separately authorized in writing |
| Sensitive evidence transfer, access, retention and deletion | OPEN: encrypted restricted channel and named recipients |
| Retest window and final report acceptance | OPEN |

## Tailored technical scope

Provide the assessor an asset inventory, threat model, dependency inventory,
architecture/data-flow diagrams and a delta between staging and production.
Agree sampling/exclusions explicitly; do not silently omit corporate/cloud
administration, remote access or infrastructure because source code is the
easiest part to test.

- External unauthenticated: login/enrollment/reset, public CMS, TLS, cookies,
  headers, error handling, endpoint discovery, limits and cache behaviour.
- Authenticated: two synthetic tenants, at least two ordinary users per tenant,
  each configured role, tenant admin, platform admin, suspended/deactivated and
  trial-expired users. Document expected capability/denial per route and method.
- Business operations: staff records, approval workflows, financial/commercial
  data, reporting/exports, document upload/download, skills evidence, identity
  linking, background/sync jobs and deletion/retention. Verify read AND write
  isolation, parameter tampering and cross-tenant object references.
- Identity: passwords, lockout/enumeration, reset authorization, MFA enrollment,
  encrypted seed migration, concurrent TOTP/recovery use, pending challenge
  revocation, session fixation and existing-session invalidation. Explicitly
  assess theft of both challenge cookies and recovery/helpdesk identity proof.
- Integration: Jira/Tempo, ClickUp, Hub Planner and Azure DevOps; credential
  isolation, minimal scopes, OAuth state/callback abuse, SSRF/DNS/redirects,
  payload rendering and provider outage behaviour. Mock provider attacks or
  obtain explicit provider consent; never probe unrelated tenant resources.
- Administration: Umbraco, extensions, setup/debug/demo routes, privileged MFA,
  access gateway/private ingress, role assignment and audit visibility.
- Infrastructure: reverse proxy/header spoofing, ingress/egress/segmentation,
  DNS/TLS, SQL/storage permissions, key ring and vault grants, managed identity,
  workload patching, admin endpoints, monitoring and recovery controls.
- Application baseline: map ASVS 5.0.0 Level 2 requirements to test IDs and
  evidence, with risk-selected stronger tests and explicit N/A explanations.

## Expected deliverables and closure

Request an executive decision summary, exact scope/version/dates and limitations,
tester identities, methodology, assets covered, excluded tests and rationale.
Each finding needs reproducible restricted evidence, affected asset/tenant/role,
severity with CVSS version/vector where appropriate, realistic business impact,
recommended remediation and independent retest outcome. Distinguish a scanner
observation from a manually validated exploit. Record untested controls as such.

Proposed internal triage targets, subject to buyer agreement: critical findings
same-day escalation and containment; high findings triaged within one working
day; medium within five. These are response targets, not promises that every fix
will complete in that time. Before release, no unresolved critical/high finding
may be treated as closed without authorized explicit residual-risk acceptance;
buyer rules can forbid such exceptions. Lower findings also need owners/dates.
Use [finding-template.md](finding-template.md). Fixes require retesting, not just
a developer assertion or a new commit. Material changes after testing need a
documented impact assessment and proportionate reassessment.

## Repository governance change specification

Current main protection is absent per the 25 September branch API observation.
The rulesets API reports a private-repository plan restriction. Obtain appropriate
plan/organisation capability without changing visibility, then configure:

- Apply to `main` and release branches. Require pull requests and at least one
  independent qualified reviewer. Name security reviewers before CODEOWNERS;
  a sole author's self-review is not independent assurance.
- Dismiss stale approvals, require approval of the latest reviewable push and
  resolution of review conversations. Restrict bypass to documented emergency
  responders, audit its use and require retrospective independent review.
- Require actual final-run check contexts for build/test/audit and secret scan
  (`build-test-audit` and `secret-scan` currently). Verify check identities and
  branch freshness; exercise a deliberately failing PR to prove merge is blocked.
- Deny force pushes/deletion; review repository collaborators, Actions permissions,
  privileged MFA, deployment identities/environments and runner isolation.
- Export redacted settings and demonstrate a rejected direct push, failed-check
  merge and unreviewed merge in an authorized test branch. Record who tested and
  when. A screenshot of configured rules alone is weaker than enforcement proof.

## Operating evidence exercises

| Exercise | Procedure and pass condition | Evidence to retain |
|---|---|---|
| Secret provenance | Deploy through selected workload identity; read only approved secrets; deny unrelated vault secrets; rotate representative tenant token and revoke old token | Redacted grant/config revisions, access events, old/new token test results; no token values |
| Key/SQL/document restore | Restore matched backups into an isolated environment; recover historical ciphertext with correct certificates; wrong/missing keys fail closed; verify tenant/role boundaries | Backup identifiers, restore timings against agreed RTO/RPO, key references, checks and operator/reviewer |
| Proxy/segmentation | Spoof forwarded headers from untrusted origin; try direct ingress and forbidden egress; verify permitted proxy and actual client-IP buckets | Redacted requests, firewall decisions, topology revision and expected/actual outcomes |
| MFA across replicas | Race same TOTP/recovery code through two instances; only one succeeds; reset/password change/deactivation invalidate pending challenges | Instance IDs, timestamps, test identity IDs, outcome counts; no codes/seeds |
| Detection | Generate synthetic failed logins, MFA failures, credential change and collector heartbeat loss; confirm alert receipt/acknowledgment | Event/alert IDs, delivery and acknowledgment times, no sensitive values |
| Retention protection | Attempt log deletion with application/operator credentials; verify denied access and independently controlled retention | Role/grant evidence, audit of denied deletion, retention policy |
| Incident tabletop | Walk through tenant data exposure and key compromise; identify containment, preservation, rotation/re-enrollment and customer communications | Attendees, timed decisions, contact validation, corrective actions |

At release, the accountable owner records: candidate/artifact/configuration IDs,
CI and ITHC reports, closed findings, remaining risks with expiry, operational
exercise evidence, deployment approval and next reassessment trigger/date.
No draft checkbox in this pack constitutes acceptance.
