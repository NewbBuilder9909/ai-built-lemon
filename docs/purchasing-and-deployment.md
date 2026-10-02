# Purchasing experience and deployment boundary

## Decision

The proposed selector matches the programme assurance direction, but the current product supports **assisted pilots**, not automatic purchase and provisioning. `/purchase` is a credential-free discovery page for one offer, the fixed-fee diagnostic. It lets a buyer see the price, model their own case, select sources and a deployment boundary, then copy a brief for a sales and security discussion. It does not accept payment, collect leads, connect sources, or claim that customer data stays inside a perimeter. It provisions nothing unless `Commercial:SelfServiceTrialEnabled` is switched on (off by default): only then do the plan catalogue and the self-service trial appear, and only then does `POST /purchase/start-trial` create a tenant and first admin.

The paid outcome is a traceable view of commitments, work, capacity, exceptions, ownership and source freshness. Source selection should qualify that outcome; connector count is not the value proposition. Jira Service Management is treated separately from Jira Software because support ticket semantics and permission scopes need discovery. CSV/manual is also discovery scope.

## Current implementation and gap

| Capability | Current state | Purchase claim |
|---|---|---|
| ClickUp, Hub Planner | Tenant-scoped credential forms and source sync | Pilot connector, with customer-specific validation |
| Jira, Tempo | Tenant-admin OAuth connection routes and source sync | Pilot connector, with consent and scope review |
| Jira Service Management | No dedicated mapping shown in the repository | Discovery only |
| Data storage | SQL Server, private contract files, raw connector payloads, logs and Data Protection keys | Map every store and retention rule per deployment |
| Customer-controlled runtime | Same app can be published as an artifact; no automated customer deployment or verified network isolation | Architecture assessment only |
| Billing and entitlement reconciliation | Tenant plans exist; no checkout, payment or automated provisioning evidence | Assisted commercial process only |

## Recommended buyer journey

1. **Explore:** Choose tools and data boundary without signing in or sharing secrets. Show a sample linked commitment and source evidence with clearly synthetic data.
2. **Qualify:** Capture organisation, buyer, selected systems, expected volume, region, retention and whether the customer controls the runtime. Obtain explicit consent before storing contact data. No API keys in this step.
3. **Security review:** Agree data flow, subprocessors, deployment owner, DPA, identity, egress, backup, incident response, patching and exit. For a customer-controlled deployment, verify where SQL, files, logs, key ring, telemetry and support access live. A custom domain is only DNS and TLS routing.
4. **Commercial agreement:** Quote scope, assisted onboarding, deployment model, pilot success criteria and support responsibilities. Do not charge for automatic activation until billing and entitlement reconciliation are implemented.
5. **Provision:** Operator creates tenant and admin, validates isolation, then admin grants least-privilege source access. Prefer OAuth where available. API keys must be scoped, stored in an approved secrets boundary, rotated and revocable. Do not transmit credentials through the discovery page, email or a sales CRM.
6. **Prove value:** Link one commitment to delivery evidence, show missing data and freshness, review an exception with an owner, and demonstrate disconnect, export and deletion.

## Deployment models

**Vendor-operated pilot:** Dedicated or carefully isolated hosting, tenant-owned connections, encrypted key ring, access and audit controls, and a signed data processing agreement. The current architecture review still has release gates for public backoffice exposure, SQL-backed persona tests and operational proof.

**Customer-controlled deployment:** Build the same application into a customer-owned environment with customer-owned SQL, file storage, key ring, logs, backup and IAM. This needs an installation contract, infrastructure templates, upgrades, monitoring and restore tests. Source APIs are external services: normal integration traffic may leave the perimeter. A zero-egress claim is incompatible with live cloud connector calls unless the customer approves a controlled path or uses an offline import. Support telemetry and update checks must also be inventoried.

`scripts/build-pilot-package.ps1 -Mode VendorPilot` and `-Mode CustomerControlled` publish a Release artifact and a manifest with its assembly hash and deployment checks. The mode is packaging metadata, not a runtime isolation switch. No environment secrets are packaged. A real deployment needs separate infrastructure and acceptance evidence.

## Security and release gates before open purchase

- Finish SQL-backed cross-tenant HTTP tests, including source connection and export paths.
- Remove deployment-wide credentials from production use and prove tenant connection ownership.
- Persist, encrypt, back up and rotate Data Protection keys; prove restore and multi-instance decrypt.
- Put `/umbraco` on a private or allowlisted edge and verify admin MFA.
- Define data inventory, retention, deletion, breach response and customer support access per deployment.
- Add consented lead capture, CRM handoff, quote/contract state, payment or invoice state, tenant provisioning, entitlement reconciliation, cancellation and refund handling before self-service checkout.
- Validate customer-controlled infrastructure with threat modelling, egress observation, penetration testing and restore rehearsal before marketing a perimeter guarantee.

Security design follows [OWASP secrets management](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html), [OWASP OAuth guidance](https://cheatsheetseries.owasp.org/cheatsheets/OAuth2_Cheat_Sheet.html), and [NIST Zero Trust Architecture](https://csrc.nist.gov/pubs/sp/800/207/final). The repository's [architecture and GTM review](archive/architecture-gtm-review-2026-09-19.md) remains the release decision record.
