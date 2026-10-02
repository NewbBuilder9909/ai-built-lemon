# Agent delivery build gate

Run `powershell -NoProfile -File scripts/agent-delivery-gate.ps1`. Add `-RunDotnetTests` to run the repository suite. Use `-InputPath` for an exported case and `-OutputDirectory` for the JSON report. The checked-in example is fictional. Stage 4 is deliberately red until measured design-partner data is supplied.

The gate implements four reviewable stages:

1. **Contract:** tenant-scoped work items, human/agent runs, artifacts, and decisions have source identity and required fields. Duplicate source keys and cross-tenant records fail.
2. **Pilot replay:** a work item is accepted only when one completed run links to a pull request with approved review, passed CI, deployed release, and accepted business outcome.
3. **Negative cases:** fixture expectations assert accepted and rejected IDs. A completed agent run without review or a blocked human run cannot count as delivery.
4. **GTM measurement:** actual baseline and pilot minutes yield a saved-minutes value. Missing measurements make the stage red and no ROI claim is generated.

The script writes `artifacts/agent-delivery-gate/report.json` and exits nonzero for a red gate. It accepts a normalized export; it does not connect to Codex, Claude, Grok, Jira, ClickUp, GitHub, or a customer account. Vendor adapters, authentication, event signatures, freshness checks, production persistence, and user access controls remain implementation work. A green offline replay must never be presented as live connector parity or live customer value.

Export `schemaVersion`, `tenantId`, `asOfUtc`, arrays of `workItems`, `runs`, `artifacts`, `decisions`, `expectations`, and `measurements`. See the fixture for exact field names. For measured work, use objects such as `{ "id": "pilot-1", "baselineMinutes": 45, "pilotMinutes": 20 }`. Record the measurement method and source separately in a customer evidence log before quoting savings.
