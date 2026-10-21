# Rovo benchmark scorecard (Q-2)

Load the Patchwork Jira exports (`TestScenarios/Patchwork`) into a personal
Jira trial. Ask Rovo plain questions about owners, due dates and estimates.
Score against `answer-key.json`. Use no work data from any employer.

Date run: ____  Assistant and version: ____  Prompt(s) used (paste verbatim): ____

| Finding | Visible in | Found? (Y / partly / N) | Evidence cited? | Notes |
|---|---|---|---|---|
| F01 Committed work with no owner | Jira alone | | | |
| F02 Open work past due date | Jira alone | | | |
| F03 Committed work with no estimate | Jira alone | | | |
| Cross-organisation findings (9 of 13, see answer key) | Files side by side only | | | Expected: not visible to a single-tool assistant |

**Decision:** if the assistant finds most of what matters inside one tool, stop.
If it can't see the cross-organisation problems, publish a short write-up
with the prompts and results; it is the marketing evidence.
