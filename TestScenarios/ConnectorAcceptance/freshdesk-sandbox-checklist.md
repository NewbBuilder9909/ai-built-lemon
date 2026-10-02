# Freshdesk connector sandbox acceptance

Run after fixture tests are stable, using a disposable trial account and invented tickets. Record date, build commit, plan/trial, tester and evidence links. Never record the API key.

## Scenario data

- Multiple pages of tickets across the incremental window.
- Status and priority update, reopened case and deleted case.
- Known, missing and unknown component tags.
- Case with no code relationship and an explicitly reviewed relationship.

## Acceptance checks

- [ ] Credential and Freshdesk account identity are tenant bound.
- [ ] Only approved ticket metadata is retained; body, attachments and contacts are absent from Silver.
- [ ] Pagination and overlapping updated-since windows complete without duplicates.
- [ ] Reopen, update and deletion produce the documented state.
- [ ] Unknown tags and unrelated cases remain visible without invented mappings.
- [ ] `401`, `403`, `429` and transient `5xx` behaviour is safe and observable.
- [ ] A partial run does not appear current or complete.
- [ ] Source links open the expected invented cases for an authorised tester.
- [ ] A relationship does not become an unreviewed causality claim.
- [ ] Disconnect applies the documented retention/deletion behaviour.
- [ ] No credential, ticket body or customer contact data appears in logs.

## Result

Outcome: `Not run / Pass / Conditional / Fail`

Limitations and follow-up owner:

