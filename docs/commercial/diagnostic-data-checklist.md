# Diagnostic data checklist

Agree the minimum data needed for one reporting decision before any files are transferred. Prefer invented or irreversibly sanitised examples during discovery.

## Required context

- Report name, frequency, owner, approver and decision date.
- Decision the report supports and what happens when evidence is missing.
- Definitions for status, estimate, allocation, actual time and reporting period.
- Current preparation steps, participants and measured time.
- One representative period plus the final report produced for that period.

## Minimum source fields

Use pseudonymous person keys unless identity is necessary and approved.

| Area | Minimum fields |
|---|---|
| Work | Source, stable item ID, project/workstream key, status, estimate with unit, assignee key, source updated time |
| Allocation | Person key, project key, period start/end, allocated amount and unit |
| Actual time | Person key, project/item key, work date, duration and unit, source updated time |
| Commitment | Commitment key, description, due period, owner and linked project/work key where available |
| Existing report | Displayed totals, exceptions, commentary, as-of time and definitions |

## Do not provide during discovery

- API keys, passwords, OAuth tokens or production connection strings.
- Ticket bodies, attachments, private messages or customer contact details.
- Source code, raw diffs or proprietary documents.
- Health, equality, disciplinary or other special-category worker data.
- Data belonging to another customer or employer without authority.

## Transfer decision

Before receiving files, record the authorised sender, purpose, fields, transfer method, storage location, access list, retention date and deletion owner. Reject unexpected fields and retain the rejection record without retaining the unnecessary content.

