# Read me first: an AI-built lemon

Between 29 August and 2 October 2026 I directed AI coding agents, mostly
Claude through Claude Code with some GitHub Copilot and OpenAI Codex, to
build a SaaS product for delivery and programme management. It is well
engineered, heavily tested and reasonably secure. Nobody wanted it, and I
never found out whether anybody would, because I never asked.

I'm publishing it so that other people building with AI can learn from what
I did wrong, and take anything useful.

## The numbers

| | |
|---|---|
| Time | 35 days |
| Commits | 262 |
| Application code | 58,000 lines of C#, plus 121 Razor views |
| Test code | 41,000 lines; 2,200+ automated tests, all passing at the last full run |
| Documentation | 69 documents, 125,000 words, plus a 5,500-word instruction file for the agents |
| Strategy and go-to-market reviews | 13, all written by AI agents |
| Feature areas | 10, later cut to a core of five menu items |
| Pages | 91 |
| Conversations with a potential buyer | 0 |
| Users other than me | 0 |
| Revenue | £0 |

## What it became, one sensible step at a time

A CMS scaffold became a demo dashboard. Then staff operations: roles, leave,
cost rates, two-factor sign-in, GDPR export. Then a sync from ClickUp, Hub
Planner, Jira and Tempo into a programme overview. Then branding, then
contracts and invoicing, then multi-tenant SaaS with plans and a platform
console. Then a skills matrix fed by GitHub and Azure DevOps evidence, a
support-desk health view, security-scanner assurance and a multi-market
executive review. Finally, a "delivery evidence check": a paid diagnostic
that tells you whether this week's delivery report can be trusted.

Every step looked reasonable when it was taken. Together they made a product
with no centre.

## Why it's a lemon

1. **I started from a solution, not a buyer.** Features arrived before anyone
   had said who would lose money without them.
2. **AI made building nearly free, so nothing asked "should we?"** The hard
   question moved from "can we build it?" to "should we?", and nothing in my
   process asked the second one. Every review added scope, rules and
   documents.
3. **I measured the wrong things.** Tests passing, architecture rules,
   documents kept up to date. The first real user was me, on day 33, and it
   failed me: 19 menu items, no search, and no way to reset someone's
   password.
4. **Thirteen strategy reviews, no conversations.** Each review graded the
   product against the previous review. The competitive analysis compared
   it with Jira, ClickUp and Power BI, and never mentioned the obvious
   substitute: the AI assistants now built into those tools. Atlassian's
   Rovo is included with paid Jira plans.
5. **The AI was a superb builder and a poor sceptic.** It carried out every
   brief well. When I finally asked it for an honest assessment, it said: "I
   carried out every brief well and never said 'nobody has asked for this
   yet'. I should have pushed back by the second feature area." I should have
   asked much sooner.
6. **The one problem that might be real was never tested.** The in-tool
   assistants can't check delivery figures that come from *several
   organisations'* tools: a client with three suppliers, each in a different
   tool. That might be worth something. Nobody was ever asked.

## What I'd do differently

- **No code until five people who would pay have described the problem**, in
  their own words, without being shown anything.
- **One screen in front of one real user in the first week.**
- **Measure users and money**, not tests and documents.
- **Give the AI a sceptic's job as well as a builder's.** Before every new
  area, make it answer "who has asked for this?" and argue for stopping.
- **List competitors from the buyer's desk**, not from the codebase: what are
  they already using today, and what does it cost them?
- **One page of strategy, kept current**, beats thirteen reviews.

## What's worth taking

If you want working patterns rather than lessons:

- **Rules enforced by tests** (`ProgrammePulse.Tests/Architecture`): every SQL
  statement carries the tenant, no cycles between feature areas, every action
  declares its permission gate, a snapshot of every route and its gate, and a
  test that makes each new page declare whether it is core or optional.
- **Multi-tenant isolation in Umbraco** with members and member groups
  (`docs/tenancy.md`).
- **An import pipeline** from ClickUp, Hub Planner, Jira and Tempo through
  raw, cleaned and reporting layers, with a queue for matching people across
  tools instead of guessing by email (`docs/programme-ops.md`).
- **The Evidence Check rules**: readiness as a band with stated thresholds,
  and every finding with a count, a total and the source records behind it
  (`Services/ProgrammeOps/EvidenceCheckCalculator.cs`).
- **Patchwork** (`TestScenarios/Patchwork`): a fictional programme across five
  organisations, exported in Jira, Tempo, ClickUp, monday.com, Harvest and Hub
  Planner formats, with an answer key of planted problems. It may be the most
  reusable thing here.
- **A persona harness** that signs in as each role over real HTTP and checks
  what it can reach (`ProgrammePulse.Tests/Personas`).
- **`CLAUDE.md`**, the instructions the agents worked under. It shows how a
  long agent-built project was steered, and where the steering went wrong.
- **`docs/archive`**, the AI-written strategy reviews, kept as evidence.

## Running it

You'll need the .NET 10 SDK and SQL Server (LocalDB on Windows is enough).
The development connection string points at a LocalDB instance called
`GardnerDB`; change it to yours, for example `(localdb)\MSSQLLocalDB`, in
`appsettings.Development.json` and in
`ProgrammePulse.Tests/Integration/ProgrammePulseWebApplicationFactory.cs`.

```bash
dotnet run      # the site
dotnet test     # unit tests, plus database tests when SQL Server is reachable
```

Demo data is described in `docs/demo-data.md`; load it into a scratch
database, never one you care about. The product is switched off in places on
purpose: optional modules start off and are switched on in Settings →
Modules.

Links in the documents to `NewbBuilder9909/OpsHwb` or to pull requests point
at the original private repository and won't open. It's provided as is, and
I won't be maintaining it.

## Licence

MIT. Take what's useful.

John Gardner, October 2026
