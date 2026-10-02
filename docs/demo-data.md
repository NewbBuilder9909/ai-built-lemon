# Northstar demo data

**28 September 2026.** A fictional agency, Northstar Digital, with enough data
in it that every page shows something real and every value case the product
is sold on can be walked through. It exists for three jobs: demonstrations,
manual testing of paths that need volume, and the SQL-backed tests that
assert those value cases still hold.

## What is in it

| | |
|---|---|
| People | 25 staff (the six Northstar personas plus 20 onboarded colleagues) across PM, BA, UX, frontend, backend, QA, DevOps and leadership roles, one part-time; one unmatched contractor |
| Portfolio | 4 client programmes and internal work: 8 projects, 20 workstreams, 103 work items |
| Time | 12 weeks of timesheets, about 1,800 entries, generated from the work items so effort matches each story |
| Bookings | 8 weeks forward and 4 back, weekly per person and project |
| Leave | approved past and future leave, pending and rejected requests |
| RAID and governance | 11 risks and issues, 4 change requests, stakeholders, locked baselines |
| Commercial | 4 contracts (fixed price, T&M, retainer, draft), cost rates effective from each person's start (one mid-window pay rise), non-labour costs, one issued and one draft invoice, contract obligations |
| Estimates | 26 estimates captured before work began: 20 reviewed as comparable, 1 excluded with a reason, 5 awaiting completion or review |
| Reporting history | One four-week period snapshot, captured the morning after it ended, and the alerts a sync would raise. Only one, because a snapshot records the data as it stands when it is taken and the demo is all imported on the day it loads |
| Evidence review | this week's Evidence Check recorded by the project manager, with owners and target dates on the fixable gaps, one caveat accepted and stated, one finding disputed, and the rest still to decide |
| Skills | an 11-skill taxonomy, 30 assertions (validated, pending, challenged, rejected, lapsed), 5 declared components with owners, cover and actions |
| Second tenant | Meridian Consulting on a trial, with the sales sample imported, so isolation is visible |

Dates are relative to the day it is loaded, so overdue stays overdue.

### The stories

- **Harbour Health, fixed price (£240k), overrunning.** Completed work is 25%
  over estimate; three milestones are overdue; HL7 work is blocked on the
  client's test environment; one engineer holds the HL7 knowledge with no
  validated cover; a contractor's time and assignment arrive with an email
  nobody has matched.
- **Kestrel Insurance, time and materials, healthy.** Estimates hold to
  within a few per cent: the comparison that shows the product is not
  simply alarmist.
- **Civic Transport, retainer, drifting.** Support hours above the 120-hour
  allowance, statuses in the client's own vocabulary ("With client",
  "Awaiting CAB") that map to no stage, time recorded against no item, and
  governance hours of unknown billability.
- **Brightwater Retail, pipeline.** A draft contract and bookings from week
  two, which push Owen Griffiths, the shared DevOps engineer, to about 49
  hours a week against 37.5, in the week before his approved leave.

`ProgrammePulse.Tests/Demo/NorthstarDemoDatasetTests.cs` pins each of these,
so data that drifts away from its story fails the build.

- **Estimate calibration.** The Health team's estimates run light and the
  Insurance team's hold, so the calibration page has a finding to show.

## How it loads

`NorthstarDemoSeeder` goes through the same services a customer's actions do:
staff are onboarded by the admin service (real Umbraco members), delivery data
arrives as two CSV exports through the file import (real status mapping,
identity queue and sync run), contracts and invoices through the contract
admin service, skills through declare-then-validate. Repositories are used
directly only where the product's own path needs a signed-in approver (leave)
or has no command yet (bookings, RAID with fixed keys, baselines, persona
teams).

History is replayed rather than written in its final state. The work items
are imported as they stood at planning, estimates are captured (the product
refuses an estimate once work has started), then today's export and the
recorded time are imported and completed work is reviewed. Rates and estimates
go through the real services with their clock set to the date each happened,
so a rate is effective before the hours it prices. Trend snapshots are not
backdated: a snapshot holds the data as it stands when taken, so a shifted
clock would file today's figures under an earlier period.

It lives in the test project, like the persona seeder, so demo accounts and
their shared password cannot ship.

## Loading it

It loads as part of an xUnit fixture. Point it at a database and run the demo
tests:

```bash
export PP_TEST_SQL_CONNECTION='Server=...;Database=UmbracoBase_IntegrationTests;...'
export PP_DEMO_SQL_CONNECTION='Server=...;Database=UmbracoBase;...'   # your development database
export PROGRAMMEPULSE_PERSONA_TOTP_SECRET='<a Base32 secret you choose>'
dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --filter "FullyQualifiedName~NorthstarDemo"
```

Without `PP_DEMO_SQL_CONNECTION` it uses a database called `UmbracoBase_Demo`
on the integration server, never the shared integration database: 25 staff in
the default tenant would change what every other suite sees. Then run the site
against that database and sign in.

Load into an empty database. Re-running is safe (every step checks what is
there, and imports are idempotent by id) but dates are fixed at the first run,
so reload into a fresh database to roll the window forward.

### Signing in

Every account uses the persona password (`NorthstarPersonaSeeder.Password`,
overridable with `PROGRAMMEPULSE_PERSONA_PASSWORD`).

| Account | Sees |
|---|---|
| `pm.sarah@northstar.test` | Team Lead: portfolio, reporting scoped to the Health team |
| `admin.emma@northstar.test` | Admin: everything, including cost, contracts and approvals. MFA |
| `dev.alex@northstar.test` | Staff: My Work, own skills and profile |
| `board.james@northstar.test`, `analyst.priya@northstar.test` | Read-only reporting |
| `platform.operator@programmepulse.test` | Platform Admin: the tenant console. MFA |
| `daniel.okafor@northstar.test` and the other colleagues | Their own roles (Holiday Approver, Team Lead, Staff) |

The Admin and Platform Admin accounts need a TOTP code from the Base32 secret
in `PROGRAMMEPULSE_PERSONA_TOTP_SECRET`; add it to an authenticator app. If
the variable is unset, the loader generates a fresh secret per run, which is
only useful to the tests themselves. The value above is the well-known RFC
example secret: it is fine for a disposable demo database on the reserved
`.test` domain and must never be used anywhere else.

## Tests over it

`NorthstarDemoIntegrationTests` (SQL-backed, in the integration filter) walks
the demo over real HTTP as the persona who would look: the portfolio with its
overdue milestone and none of Meridian's data, the Evidence Check verdict,
the unmatched contractor on the identity queue, every active contract on the
margin overview, the single-maintainer skills, pending leave, and a
developer's own work with no cost figures.
