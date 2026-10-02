# Assayer Software: proposed pilot gates

27 September 2026. Reviewed alongside shared checkout e0ee249, which also has ongoing MFA changes. Earlier isolated validation is not evidence for that combined checkout. This is a pilot decision aid, not a security sign-off or
completed ITHC. Named owners must replace the proposed roles below.

## Recommended launch shape

Start with an invited design-partner pilot: two or three organisations, assisted
onboarding, a small agreed user group, and one clearly defined workflow per
organisation. Use synthetic data for the first rehearsal. Admit real staff,
project or repository data only after the corresponding gates below pass.
Keep public self-service registration disabled for this stage.

Suggested duration: four to six weeks, with a weekly review. Agree the scope,
success criteria, support hours and exit arrangements before onboarding.
These numbers are planning suggestions, not established capacity limits.

## Gates before real customer data

| Gate | Evidence required | Proposed owner | Current evidence boundary |
|---|---|---|---|
| Authorization findings | All five reported access-control findings reproduced or explained, fixed where confirmed, regression-tested, reviewed, and retested by the reporting scanner on the final candidate | Engineering + independent reviewer | Earlier fixes are committed in the shared checkout; final combined-candidate validation and scanner closure not established |
| Release candidate | Reviewed commit; passing build, Razor, SQL/persona tests, dependency audit and history secret scan; artifact digest tied to deployment; security-changing concurrent work included | Engineering | Historical reports do not validate the candidate |
| Independent security assessment | Authenticated testing of deployed staging, two tenants and every enabled role; exports, destructive actions, account recovery, integrations and revocation tested; findings disposition recorded | Security reviewer + service owner | Repository ITHC documents are readiness/handover materials, not a completed assessment |
| Operating environment | Named host and region; HTTPS, restricted database/admin access, privileged MFA, secret storage, durable protected Data Protection keys, safe production configuration | Platform owner | Needs deployment evidence, not code presence |
| Recovery | Restore database plus matching encryption keys into an isolated environment; verify login and representative credential decryption; rehearse release rollback; record actual recovery time and data-loss window | Platform owner | Drill evidence required |
| Monitoring and incident handling | Alerts for failures and suspicious access reach a named operator; support and private vulnerability-reporting routes tested; incident exercise covers containment, notification and evidence retention | Service owner | Logging implementation alone is insufficient |
| Customer and privacy arrangements | Pilot terms; agreed controller/processor roles and appropriate processing contract; privacy notice; subprocessor/hosting information; retention, export, deletion and termination arrangements reviewed | Commercial/privacy owner + customer | Requires review against the actual hosted service |
| Person-level evidence | Customer-approved purpose and lawful basis, worker transparency and applicable DPIA; product processing gate satisfied; withdrawal/correction and revocation exercised | Customer data owner + engineering | The product's recorded decision does not itself establish lawful processing |
| Live connector acceptance | Each enabled connector tested against an authorized pilot account: minimum scopes, representative mappings, partial sync, throttling, expired/revoked credentials, disconnect/reconnect, and deletion behaviour | Integration owner + partner | Test fixtures and a connector's presence in source are insufficient; check each connector's dated acceptance evidence |
| Complete customer journey | Invite/onboard, authenticate and recover access, establish roles, reach first useful result, export, remove a user, disconnect and leave; test empty/error states, keyboard access and agreed browsers | Product owner + partner | Rehearsal required on hosted candidate |

Do not waive unauthorized cross-team access, tenant-data destruction or revoked
processing merely because the scanner labels the findings Medium. Assess impact
against the data and pilot use case. Keep a named risk owner and written
disposition for other findings; do not substitute a target scanner score for review.

## What the customer-facing site should support

- A specific audience, problem and demonstrable outcome. Clearly distinguish
  Assayer Software as supplier from any product names shown in screenshots.
- A working **Request a pilot** route, acknowledgement and named follow-up owner.
- A truthful feature/connector matrix: available and validated, limited preview,
  or planned. Do not advertise an unfinished connector as operational.
- Clearly labelled synthetic demonstrations; no customer information or secrets
  in screenshots, downloadable samples or analytics events.
- Privacy and contact information, support expectations, and a security page
  describing implemented controls with their limits. Do not claim a completed
  ITHC, government approval or certification without the corresponding evidence.
- Skills/evidence copy aligned with `skills-evidence-gtm-claims.md`: observed
  participation and human review, never developer productivity rankings,
  automatically proven proficiency or inferred blame.
- A usable contact form with spam controls and minimal fields; verify delivery,
  keyboard access, errors, mobile layout and the consent behaviour of any
  non-essential tracking actually deployed.

Website publication and customer-data onboarding are separate decisions. A
marketing site can invite interest while the product's real-data gates remain open.

## Pilot outcomes and stop conditions

Before starting, record today's time/effort for the chosen workflow and the
decision the customer wants to improve. Measure time to first useful result,
weekly completion of that workflow, support time per organisation, and whether
the customer would continue at a stated price. Set targets with each partner.

For evidence features, follow `skills-evidence-validation-protocol.md`: inspect
representative artefacts with the people concerned, record disagreement by
category, and test corrections. Do not combine unlike errors into a single
unsupported accuracy claim.

Stop the affected tenant/connector on unauthorized disclosure, processing after
withdrawal, lost or corrupted data, or a security incident. Preserve evidence,
notify the designated owner and resume only after the cause is addressed and
the relevant checks pass. Trial a connector kill switch and tenant suspension
before they are needed in an incident.

At exit, decide explicitly: continue, extend with a narrow unresolved question,
or stop and export/delete according to the agreement. Record outcomes, known
limits, incident history, per-tenant operating cost and partner feedback.

## Work that can wait

Unless required by a pilot partner: public self-service signup, automated
billing, every connector, additional tiers, multi-region availability and broad
marketing campaigns. Establish demand and a repeatable supported workflow first.
An intentionally single-instance pilot still needs backups, monitoring and
verified sync concurrency behaviour; do not enable replicas before reviewing
any in-process-only sync guards.

## Basis and further reading

- Repository: [ITHC readiness](assurance/ithc-readiness.md),
  [security assurance](security-assurance.md),
  [claims boundaries](skills-evidence-gtm-claims.md),
  [design-partner validation](skills-evidence-validation-protocol.md).
- [GOV.UK vulnerability and penetration testing](https://www.gov.uk/service-manual/technology/vulnerability-and-penetration-testing)
  recommends third-party testing before public beta or real user data for
  government services. This is a useful pilot baseline; buyer-specific
  requirements still need agreement.
- [NCSC Software Security Code of Practice guidance](https://www.ncsc.gov.uk/collection/software-security-code-of-practice-implementation-guidance)
  addresses development, build security, deployment, maintenance and customer communication.
- [ICO monitoring workers guidance](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/employment/monitoring-workers/data-protection-and-monitoring-workers/)
  covers lawful, fair and transparent monitoring and impact assessment.
- [ICO controller/processor contracts](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/accountability-and-governance/contracts-and-liabilities-between-controllers-and-processors-multi/when-is-a-contract-needed-and-why-is-it-important/)
  explains the contractual relationship where a provider processes personal
  data for a customer. Obtain advice for the actual pilot arrangements.
