# Patchwork: one programme, five organisations, six tools

A fictional transport operator, **Ostrevane Transit**, runs a programme
(*Fares and Journeys Replatform*) delivered by four suppliers. Each
organisation works in its own tool, and each file here is that tool's own
export: its column names, units and date style. `answer-key.json` lists every
problem planted in the data, and says whether one tool's data reveals it or
only the files side by side do.

It exists to test one claim: that a neutral check across several
organisations' exports finds things that no single tool, or the AI assistant
inside it, can see. It can show that capability. **It can't show that anyone
will pay for it**; only conversations with buyers can.

Everything is invented. Organisation names were checked against web search
and company listings before use; every email uses the reserved `.example`
domain; no file mentions a real company. `ForbiddenNamesTests` scans the whole
repository for names you list outside it (see below).

## The cast

| Organisation | Role | Tool | Files | Extract date |
|---|---|---|---|---|
| Ostrevane Transit | Client, PMO | Spreadsheet, Hub Planner | `ostrevane-milestone-plan.csv`, `ostrevane-hubplanner-timesheet.csv` | as-of date |
| Kelvaro Digital | Passenger app | Jira Cloud, Tempo | `kelvaro-jira-work-items.csv`, `kelvaro-tempo-worklogs.csv` | as-of date |
| Brantoft Systems | Ticketing API and payments | ClickUp | `brantoft-clickup-tasks.csv`, `brantoft-clickup-time-entries.csv` | as-of date |
| Dunmarrow Studio | Design and accessibility | monday.com, Harvest | `dunmarrow-monday-board.xlsx`, `dunmarrow-harvest-time.csv` | as-of date |
| Ombreton Labs | Data migration | Jira (older export) | `ombreton-jira-issues.csv` | 21 days before |

Eight weeks of history, 20 people. The as-of date is in `answer-key.json`.

## How each file is shaped, and how sure we are

Modelled on each vendor's export as publicly described in 2024–2026. None was
compared with a real export from a live account, so treat column *order* as
approximate and the column *names*, units and date styles as the claim.

| File | Shape | Basis |
|---|---|---|
| Jira (Kelvaro) | "Export CSV (all fields)" with the 2025 renames (`Work item key`, `Work item id`, `Work type`); estimates and time in **seconds**; dates `02/Oct/26 9:00 AM`; `Parent` holds the parent's numeric id | Atlassian KB on CSV export and estimate units; the issue→work item rename |
| Jira (Ombreton) | The same export before the rename (`Issue key`, `Issue id`, `Issue Type`), one `Log Work` column per worklog, each `comment;date;account;seconds` | Atlassian CSV export |
| Tempo | Logged-time raw export with the August 2024 names (`Logged Hours`, `Billable Hours`, `Logged Seconds`) and `Work Item Key` | Tempo's notice of the column changes; Tempo help centre |
| ClickUp tasks | `Task ID`, `Task Name`, `Date Created` as POSIX milliseconds plus `Date Created Text` in US month-first style, `Time Estimated` in **milliseconds** with a `10h 5m` text twin, `[Name, Name]` assignees | ClickUp help on exporting task data |
| ClickUp time | Time-tracking export: `Start`/`Stop` in milliseconds, `Time Tracked` in milliseconds | ClickUp time-tracking export |
| monday.com | "Export board to Excel": an **.xlsx** with the board name, then each group as a block with its own header row, subitems under their parent with a different header, `Time Tracking` as `h:mm:ss` | monday.com support and community threads |
| Harvest | Detailed time report: `Date` as `yyyy-MM-dd`, decimal `Hours`, `First Name` and `Last Name` split, `Billable?`; cost columns emptied as a supplier would before sharing | Harvest help on exporting data |
| Hub Planner | Timesheet export: `Date` (UK), `Resource Name`, `Category`, `Booked Time` and `Actual Time` as `h:mm`; suppliers have bookings but no actuals | Hub Planner knowledge base |
| Milestone plan | A PMO spreadsheet: UK dates, RAG letters, a "last updated" date | Typical practice, not a product format |

## The planted problems

| # | Problem | Seen in | Figure at the committed as-of date |
|---|---|---|---|
| F01 | Committed work with no owner | Jira alone | 3 of 14 open sprint items |
| F02 | Open work past its due date | Jira alone | 4 of 16 open items |
| F03 | Committed work with no estimate | Jira alone | 4 of 14 open sprint items |
| F04 | Past due with a custom status (`parked`, `awaiting client`) | ClickUp alone | 3 of 20 tasks |
| F05 | Worklogs on items missing from the Jira export | Jira with Tempo | 6 worklogs, 22.5 hours |
| F06 | One person billed by two suppliers for the same days | **Across organisations** | 6 days, 45 hours each |
| F07 | Milestone reported green, delivery data red | **Across organisations** | M3 green 6 days late; 2 overdue beta items; design sign-off Stuck |
| F08 | One milestone, two dates | **Across organisations** | Plan 14 Oct, supplier 31 Oct |
| F09 | Milestone marked complete on stale supplier data | **Across organisations** | OMB-4 In Progress; export 21 days old |
| F10 | Hours from someone nobody planned or assigned | **Across organisations** | 48 hours from a contractor seen only in Harvest |
| F11 | One person, several names, no shared email | **Across organisations** | 3 naming schemes |
| F12 | Supplier time that can't be tied to a deliverable | **Across organisations** | 503.5 of 777.5 Harvest hours |
| F13 | A supplier burning more than the client booked | **Across organisations** | 751 hours logged against 570 booked (32% over) |

Records, file names and exact rules are in `answer-key.json`. The ingestion
traps (renamed columns, five time units, five date styles, key versus id,
repeated headers, a spreadsheet instead of a table, split names) are listed
there as `T1`–`T7`.

## What the current importer makes of it

Measured by `PatchworkScenarioTests.What_the_current_importer_accepts_unedited`,
through the real parser and validation:

| File | Result | Why |
|---|---|---|
| Harvest | Accepted | But all 127 entries arrive with nobody matched and nothing linked |
| Jira (Kelvaro) | Rejected | `02/Sep/26 12:00 AM` is not a date it reads |
| Jira (Ombreton) | Rejected | `Issue key` and `Issue id` both mean the item id |
| ClickUp tasks | Rejected | No project column it recognises (`List Name`) |
| Tempo | Rejected | `Logged Hours` is not an hours column it recognises |
| ClickUp time | Rejected | No date or hours column (`Start`, `Time Tracked` in milliseconds) |
| Hub Planner | Rejected | `Booked Time`/`Actual Time` in `h:mm` |
| Milestone plan | Rejected | Not a work-item export |
| monday.com | Not read | The importer reads CSV only |

So the patchwork model needs a reader per native format before anything
else. That is the honest starting point.

## Using it against Rovo

Rovo is included on paid Jira plans. Use a **personal** trial site, never a
work account.

1. Create a Jira project and import `kelvaro-jira-work-items.csv` with the CSV
   importer. Set the date format to `dd/MMM/yy h:mm a`. The people don't exist
   in the trial, so the importer can't set real assignees: create a short-text
   field called *Assignee (export)* and map the `Assignee` column to it, or
   every item arrives unassigned and F01 means nothing.
2. Ask Rovo: *"Can this project's status report be trusted this week? What is
   missing, what is late, and who owns each problem?"*
3. Score it against F01–F03, the only findings inside that one file. Tempo
   (F05) needs the paid Tempo app.
4. F06–F13 are outside any single tool by construction. That is the point of
   the scenario, and also its limit: it proves what a neutral check *can* see,
   not that a buyer needs it.

## Regenerating

The files are generated by `ProgrammePulse.Tests/Scenarios/Patchwork`. To roll
the dates forward to a new week:

```bash
PP_UPDATE_PATCHWORK=1 PP_PATCHWORK_AS_OF=2026-10-09 dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --filter Patchwork
```

The tests then fail if a committed file drifts from the generator, if an
answer-key record is missing from its file, or if any file names a forbidden
organisation or uses a real email domain.

## Keeping real names out

List names that must never appear (an employer, a client) one per line in
`~/.programmepulse/forbidden-names.txt`, outside the repository.
`ForbiddenNamesTests` reads that file and fails if any tracked text file
contains one of them, as a word or a pair of words. The public copy ships with
no names, because even a hash of a known name can be guessed; in a private
fork, `./scripts/forbidden-name-hash.ps1 -Name "..."` prints a hash you can
add to the test instead.

## Sources

- Atlassian: [export issues to CSV](https://support.atlassian.com/jira/kb/how-to-export-issues-from-jira-cloud-in-csv-format/), [Original Estimate in seconds](https://support.atlassian.com/jira/kb/understand-how-the-original-estimate-field-value-is-calculated-when-viewing-the-data-export-in-jira-cloud/), [issue to work item rename in CSV mapping](https://community.atlassian.com/forums/Jira-questions/How-do-I-update-using-CSV-in-non-admin-Jira-tool-if-Issue-Key-is/qaq-p/2995442)
- Tempo: [changes to the logged time raw data export](https://www.tempo.io/product-news/changes-to-logged-time-report-raw-data-export), [exporting reports](https://help.tempo.io/timesheets/latest/exporting-reports)
- ClickUp: [export task data](https://help.clickup.com/hc/en-us/articles/6310551109527-Export-task-data)
- monday.com: [export from monday to Excel](https://support.monday.com/hc/en-us/articles/26989749858578-Export-from-monday-to-Excel), [subitem rows in the export](https://community.monday.com/t/exporting-full-subitem-rows-to-excel/51820)
- Harvest: [exporting data](https://support.getharvest.com/hc/en-us/articles/31625325401229-Exporting-data), [detailed time columns](https://www.easycsv.io/docs/xero/how-to-get-harvest-time-entries-into-xero)
- Hub Planner: [timesheets export columns](https://help.hubplanner.com/kb/timesheets-extension/), [bulk upload bookings](https://help.hubplanner.com/kb/bulk-upload-bookings-to-hub-planner/)
