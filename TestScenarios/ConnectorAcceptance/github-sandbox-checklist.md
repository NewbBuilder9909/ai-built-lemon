# GitHub connector sandbox acceptance

Use a disposable GitHub Free organisation and repositories containing invented data. Record date, build commit, app configuration version, tester and evidence links. Never paste tokens into this document.

## Scenario data

- Included and excluded repository.
- Default-branch commit and merged pull request.
- PR review and requested change.
- Squash merge, merge commit and co-authored commit.
- Bot author, unmatched actor, renamed/generated file and deleted branch.

## Acceptance checks

- [ ] Installation callback is bound to the initiating tenant and session.
- [ ] App is restricted to selected repositories and read-only permissions.
- [ ] Granted account and repositories match the connection screen.
- [ ] Pagination completes and records retain source URLs and timestamps.
- [ ] Replay creates no duplicate evidence.
- [ ] Author, committer, PR author, co-author and reviewer remain distinct.
- [ ] Bots are excluded from person attribution.
- [ ] Unknown actors remain unresolved until explicitly approved.
- [ ] Excluded repository data cannot be read or inferred.
- [ ] Rate-limit and partial-run status are visible and do not publish complete coverage.
- [ ] Revocation, uninstall and permission loss fail closed.
- [ ] Disconnect applies the documented retention/deletion behaviour.
- [ ] No token, code body, raw diff or unnecessary personal data appears in logs.

## Result

Outcome: `Not run / Pass / Conditional / Fail`

Limitations and follow-up owner:

