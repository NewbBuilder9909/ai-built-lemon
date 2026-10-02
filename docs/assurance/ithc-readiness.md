# UK government supplier ITHC readiness and gap register

Assessment date: 25 September 2026. Repository: NewbBuilder9909/OpsHwb.
Decision: **NOT READY for production security sign-off. Suitable to begin assessor scoping.**

This is a proposed UK government supplier assurance baseline, not a government
certification, completed ITHC or claim of CAF/ASVS compliance. The contracting
authority must confirm classification, contractual requirements, assessment
profile and acceptance authority. Hosting is undecided; Azure integrations in
source do not establish an approved or deployed Azure architecture.

## Basis and applicability

- [GOV.UK Service Manual: vulnerability and penetration testing](https://www.gov.uk/service-manual/technology/vulnerability-and-penetration-testing): whole-system assessment and repeated testing during development.
- [Cabinet Office ITHC supporting guidance](https://www.gov.uk/government/publications/it-health-check-ithc-supporting-guidance/it-health-check-ithc-supporting-guidance): external/internal assessment and assessor reporting. This guidance is specifically for PSN submissions; its PSN requirements are not automatically obligations for this product. For a central-government engagement, plan a CHECK provider and confirm buyer requirements.
- [Government Secure by Design](https://www.gov.uk/guidance/follow-the-government-cyber-security-standard) and [NCSC CAF](https://www.ncsc.gov.uk/collection/cyber-assessment-framework): risk ownership and broader operational assurance. The CAF themes below are organising references, not assessed contributing outcomes or GovAssure completion.
- [Software Security Code of Practice](https://www.gov.uk/government/publications/software-security-code-of-practice/software-security-code-of-practice): voluntary vendor baseline covering leadership, development, build systems, maintenance and customer communication. Buyer contracts may impose additional obligations.
- [OWASP ASVS 5.0.0](https://github.com/OWASP/ASVS/tree/master/5.0): proposed application baseline, Level 2 plus risk-selected stronger controls. The assessor must map individual versioned requirements and justify exclusions; the grouped checklist below is not a complete ASVS mapping.
- [GitHub secure use guidance](https://docs.github.com/en/actions/reference/security/secure-use): immutable action references and restricted workflow privileges.

## Evidence boundary and current position

Local HEAD inspected: `53631178888bd650b7c37834b5fe9d3eb556f7a2`, branch
`modernize/dotnet-20260923`. There were **162 outstanding changes at initial
inspection**, before this assurance work. Claude's architecture work is ongoing.
This is a point-in-time source/documentation assessment, not a fresh execution of
the application test suite. Historical 584/22 results in the older register do
not validate the current checkout; later architecture documents also report
different counts. Only final-revision reports will be accepted.

Read-only GitHub observations on the assessment date:

- Remote `main`: `528b615e9b22853ce93311fc9f4a44edb5f75bc9`.
- [CI run 35908742995](https://github.com/NewbBuilder9909/OpsHwb/actions/runs/35908742995): failed Razor check; build, tests and dependency audit skipped; secret scan passed.
- Failure is a Linux `./Views/GardnerPMBlog.cshtml` exclusion-path mismatch. Local fix normalizes both separators before removing `./`; final Linux CI confirmation remains open.
- Branch endpoint reported `protected=false`, status-check enforcement off. Detailed protection API returned integration permission denied. Rulesets API returned a private-repository plan restriction. These are distinct observations; do not claim all administrator settings were inspected.
- No named independent security reviewer, operating environment or independent assessment evidence was supplied.

Status meanings: **Partial** = implementation or documentation exists but acceptance
evidence is incomplete; **Open** = required evidence/control not established;
**Blocked** = a known dependency or failed gate prevents closure. None of these
means an exploitable vulnerability has been demonstrated. No control is marked
Verified on the basis of file presence alone.

P0 = close before production sign-off; P1 = close before the relevant assessment
or agree a time-limited exception with the buyer/risk owner; P2 = operational
improvement with a scheduled owner. All owner roles below require a named person.
These are proposed priorities, not government-mandated remediation deadlines.

## Control checklist and comparison

### Governance and supplier assurance (CAF A)

| ID | Control / acceptance evidence | Current position | Status | Priority / owner |
|---|---|---|---|---|
| G01 | Named accountable service/risk owner, security lead and independent release reviewer; documented acceptance authority | Roles proposed here; appointments absent | Open | P0 / sponsor |
| G02 | Buyer-agreed classification, data location, contract security schedule, PSN applicability, required certifications and ITHC scope | Buyer requirements not supplied; do not assume OFFICIAL suitability or certification | Open | P0 / commercial + security |
| G03 | Asset/data-flow inventory with trust boundaries, tenant stores, integrations, admin paths and threat model | Architecture and data-governance docs exist; changes need security review and deployed diagrams | Partial | P1 / architect |
| G04 | Supplier/subprocessor inventory, approved scopes, access review and third-party testing consent | Integration code exists; supplier operating agreements/consents not evidenced | Open | P1 / supplier owner |
| G05 | Data inventory, retention/deletion rules, minimisation, applicable privacy assessment and customer export/deletion evidence | `docs/data-governance.md`, `gdpr.md`, lifecycle tests; deployed execution and buyer approval absent | Partial | P1 / data owner |
| G06 | Tested private vulnerability contact, triage owner, customer incident route, support/EOL and patch policy | `SECURITY.md` has conditional reporting instructions; working contact and policy commitments unverified | Partial | P1 / security + commercial |
| G07 | Staff/repository/cloud access review, privileged MFA, joiner/mover/leaver controls, secure developer endpoints | No organisational evidence supplied | Open | P1 / IT owner |

### Build and delivery integrity (CAF A/B)

| ID | Control / acceptance evidence | Current position | Status | Priority / owner |
|---|---|---|---|---|
| D01 | Protected release branch; mandatory independent approval; stale approval dismissal; required exact-revision CI; restricted bypass; no force push/deletion | Remote main unprotected; private rulesets plan restriction; PR template added but cannot enforce reviews | Blocked | P0 / repository owner |
| D02 | Final committed candidate passes Razor, build, SQL tests, vulnerability audit and history secret scan; retain reports and exclusions | Latest remote CI fails; local portability correction supplied; architecture work not frozen | Blocked | P0 / engineering |
| D03 | Immutable workflow action references, least privilege, reviewed updates and isolated runner credentials | Action SHA pins added; read-only workflow token and Dependabot exist; runner/admin access still needs review | Partial | P1 / build owner |
| D04 | Release dependency inventory/SBOM including transitive packages, front-end assets, runtime/base image; dated advisory scan and support status | NuGet audit exists; complete release SBOM/support inventory not demonstrated | Partial | P1 / build owner |
| D05 | Signed or attested release artifact, hash tied to reviewed commit; protected deployment credentials and promotion evidence | New source-hash collector provides provenance only; no release signature/attestation demonstrated | Open | P1 / release owner |
| D06 | Security review covers architectural changes, manual code analysis and appropriate automated static analysis with triaged findings | Prior focused review/tests exist; current changes and full static-analysis coverage not assessed | Partial | P1 / app security |
| D07 | Supported dependency patch process, emergency change process and customer notification exercises | Weekly dependency updates configured; operational patch/notification evidence missing | Partial | P1 / service owner |

### Application verification (CAF B; ASVS mapping required)

| ID | Control / acceptance evidence | Current position | Status | Priority / owner |
|---|---|---|---|---|
| A01 | Authentication, password/reset abuse, enumeration, lockout, MFA enrollment/reset/recovery and admin MFA tested over HTTP | Security/MFA suites and controls exist; rerun refactored paths and independent abuse tests | Partial | P0 / app security |
| A02 | SQL contains no plaintext TOTP; legacy migration rehearsed; key access separated and failed key/migration cases deny access | Protector, verification service, migration and tests exist; production custody/upgrade evidence absent | Partial | P0 / application + platform |
| A03 | Concurrent/same-window TOTP and recovery replay rejected across instances; challenge reset/revocation/expiry confirmed | SQL atomic controls and tests exist; multi-instance deployment not tested | Partial | P0 / application |
| A04 | Session fixation, cookie flags, logout, password reset, account disable, idle/absolute expiry and stolen-session behaviour tested | Pending-challenge binding exists; complete session lifecycle needs independent test; both-cookie theft remains bearer theft | Partial | P0 / app security |
| A05 | Two tenants and every role tested for direct-object access, role escalation, exports, documents, background jobs and platform-admin bypass | SQL/persona boundary tests; architecture refactor changes enforcement; final matrix not assessed | Partial | P0 / application |
| A06 | SQL injection, stored/reflected/DOM XSS, CSRF, mass assignment, redirects and error disclosure manually tested | Prior source review and headers; no current comprehensive dynamic report | Partial | P1 / assessor |
| A07 | File upload/download authorization, size/type limits, path traversal, malware handling, storage outside web root and retention tested | Contract document storage exists; no full independent upload/download evidence | Open | P1 / application + assessor |
| A08 | SSRF, redirect/DNS abuse, OAuth state/callback/token handling, provider scopes and tenant credential isolation tested for every connector | Outbound policies/tests exist; new Azure DevOps connector and all OAuth flows require scope inclusion | Partial | P0 / integrations |
| A09 | Rate limits, request limits, costly reports/sync, queue isolation and per-tenant availability tested across replicas | Architecture review identifies in-process limits and workload concerns; deployed load/abuse tests absent | Partial | P1 / performance + platform |
| A10 | TLS/cookies/cache/CORS/headers and full CSP verified on public, staff and Umbraco routes | Middleware tests exist; backoffice script policy compatibility remains open | Partial | P1 / application + platform |
| A11 | Logs/errors/exports contain no secrets; sensitive responses use appropriate caching; production debug/demo/install paths disabled | Guards/events exist; complete deployed review not supplied | Partial | P0 / app security |

### Infrastructure and data protection (CAF B)

| ID | Control / acceptance evidence | Current position | Status | Priority / owner |
|---|---|---|---|---|
| P01 | Selected hosting; documented shared responsibility; isolated staging/production; inventory of public/private endpoints | Hosting undecided; Azure code is preparation, not deployment evidence | Blocked | P0 / platform owner |
| P02 | Managed secret provenance, workload identity, least privilege, rotation, audited break-glass and no shared provider fallback | Guards and Azure bootstrap options exist; granted permissions and actual origins unverified | Partial | P0 / platform |
| P03 | Separate durable encrypted key ring, restricted SQL/key administration; matched restore and certificate rotation proof | Key-ring/MFA runbook and tests exist; no operating drill | Partial | P0 / platform |
| P04 | Private database/storage/admin ingress, tested segmentation, egress restrictions, approved DNS and blocked direct app ingress | Source host allowlists do not prove firewall/network enforcement | Open | P0 / platform |
| P05 | Reverse-proxy trust, host allowlist, stripped spoofed headers, TLS chain and client-IP/rate-limit correctness | Explicit proxy configuration exists; topology unspecified | Partial | P0 / platform |
| P06 | Umbraco backoffice/admin access isolated with privileged MFA; account/API/extension inventory and least privilege | No deployed isolation evidence | Open | P0 / platform + CMS owner |
| P07 | Credentialed configuration/vulnerability scans of hosts/containers/cloud; patching, encryption, workload identity and admin endpoints reviewed | No deployed infrastructure or scan results supplied | Open | P1 / platform + assessor |
| P08 | Developer/admin endpoints, remote access and corporate network assessed where in buyer scope | Not assessable from application source; agree estate boundaries with assessor | Open | P1 / IT owner |

### Detection, response and recovery (CAF C/D)

| ID | Control / acceptance evidence | Current position | Status | Priority / owner |
|---|---|---|---|---|
| O01 | Central security collection in separate admin boundary; immutable retention; clock sync; deletion/access audit | Structured events exist; collector/retention operation not evidenced | Partial | P0 / security operations |
| O02 | Tested alerts for login/MFA abuse, privileged changes, credential changes, collector failure; named on-call acknowledgments | Alert proposals in existing register; no exercised alert evidence | Partial | P0 / security operations |
| O03 | Incident playbook covers tenant breach, credential/key compromise, ransomware and supplier compromise; timed tabletop and contacts | Rotation guidance exists; full exercise and communications approval absent | Open | P1 / incident lead |
| O04 | Agreed RTO/RPO; restore database, keys and documents; verify tenant separation and MFA/source-secret recovery after restore | Unit/integration key tests are not disaster-recovery evidence | Partial | P0 / platform + service owner |
| O05 | Independent ITHC, authenticated application and infrastructure tests, complete findings register and retest evidence | No independent report supplied | Open | P0 / assessor + risk owner |
| O06 | Every finding has severity/rationale, owner, due date, mitigation, fixed revision, retest and explicit residual acceptance/expiry | Finding template supplied; owner appointments and assessor findings pending | Partial | P0 / security lead |

## Immediate action queue

1. **Now — repository owner:** select a private-repository plan/organisation that supports required protections. Do not make the repository public to unlock them. Apply the settings in the handover pack after confirming named reviewers. Enforcement is not closed by this document or a PR checkbox.
2. **Now — engineering:** finish Claude's review, resolve its changes and review the Linux Razor fix plus workflow pins. Commit the combined candidate through review; publish and rerun CI. No historical green run substitutes for it.
3. **Now — sponsor/security:** name accountable owners, confirm buyer/classification requirements and reserve an independent assessor. Use the supplied scope/RoE draft; no testing of third parties until consent is recorded.
4. **Before environment build — platform:** decide hosting and secret/key custody, then implement P01-P07 and monitoring. Source configuration alone cannot close these controls.
5. **Before assessment — engineering/security:** freeze the candidate, generate release evidence/SBOM, provide two synthetic tenants and role accounts, execute recovery/alert drills and complete ASVS applicability mapping.
6. **Before production — risk owner:** close P0 items, accept only explicit time-limited residual risks, obtain independent retest results and record sign-off for the exact deployed artifact/configuration.

## Changes delivered by this work

- Corrected Linux/Windows Razor exclusion normalization, retaining the original narrow exclusion.
- Pinned existing GitHub Actions to commit SHAs resolved from their official repositories; retained Dependabot and minimal permissions.
- Added a security-aware PR template; this supports but does not enforce review.
- Added `scripts/collect-ithc-evidence.ps1`: double-pass source hashing, revision/dirty-state capture and clean-tree rejection. No source/secret contents or environment variables are captured. Hashes do not prove authenticity or passing checks.
- Added [assessor handover and operating gates](ithc-handover.md) and [finding record](finding-template.md).

Targeted local validation on 25 September: Windows and Linux-style path
normalization examples passed, including preservation of an unexpected view;
both PowerShell scripts parsed successfully. Evidence collection succeeded on
the working tree, rejected it with `-RequireClean`, and succeeded in a separate
clean disposable Git fixture with a matching inventory digest. All workflow
action references are full SHA pins and `git diff --check` passed. CI now
collects clean revision provenance and retains it for 90 days. These checks do
not establish a successful Linux CI run or validate the changing application;
the full solution suite was not rerun during the concurrent architecture work.

Run `pwsh -File scripts/collect-ithc-evidence.ps1` for a diagnostic snapshot;
use `-RequireClean` only after committing all intended changes. Attach final CI
reports, advisory scan timestamps, tool versions, SBOM, artifact hashes and
deployment evidence to a restricted evidence store. Do not commit confidential
penetration-test reports, credentials or customer data. Recollect after any
source, dependency, infrastructure or configuration change affecting the claim.

See the [existing security register](../security-assurance.md) and
[MFA runbook](../mfa-security-runbook.md) for implementation details. This dated
assessment supersedes their older CI/current-readiness observations only; it
does not replace their technical procedures.
