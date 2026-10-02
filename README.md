# ai-built-lemon

A SaaS product for delivery and programme management, built in five weeks by
one person directing AI coding agents: 58,000 lines of C#, 2,200+ tests,
13 AI-written strategy reviews, no customers. Published as a lesson.

**Start with [READ-ME-FIRST.md](READ-ME-FIRST.md)**: what was built, why it's
a lemon, what to do differently, and which parts are worth reusing.

- Stack: Umbraco 18, .NET 10, SQL Server, NPoco migrations, Razor views; no
  JavaScript framework.
- Where it stopped, and the plan it was following: [docs/direction.md](docs/direction.md).
- The instructions the AI agents worked under: [CLAUDE.md](CLAUDE.md).
- A fictional five-organisation data set in native Jira, Tempo, ClickUp,
  monday.com, Harvest and Hub Planner formats: [TestScenarios/Patchwork](TestScenarios/Patchwork/README.md).
- Licence: [MIT](LICENSE). Provided as is; not maintained.

## Local setup

### Prerequisites

- .NET 10 SDK
- A local SQL Server / LocalDB instance for Umbraco data. The development
  connection string names a LocalDB instance called `GardnerDB`; point
  `appsettings.Development.json` and
  `ProgrammePulse.Tests/Integration/ProgrammePulseWebApplicationFactory.cs`
  at your own, for example `(localdb)\MSSQLLocalDB`.
- HTTPS localhost support enabled in your development environment

### Restore and run

```powershell
dotnet restore
dotnet build
dotnet run
```

Optional modules (reporting, contracts, skills and others) start switched off
for every organisation; an Admin switches them on in Settings → Modules.

### Run tests

```powershell
dotnet test
```

The database tests need SQL Server; see `CLAUDE.md` for the filters that run
the fast unit tests on their own. Load demo data only into a scratch database
(`docs/demo-data.md`).

## Secret management

This project stores local-only secrets in .NET user secrets rather than in the repository.

To inspect local secrets:

```powershell
dotnet user-secrets list --project .
```

To set the Umbraco HMAC secret locally:

```powershell
dotnet user-secrets set "Umbraco:CMS:Imaging:HMACSecretKey" "<your-local-secret>" --project .
```

To set the ClickUp API token (required for Programme Ops sync — see
`docs/programme-ops.md` for the full setup):

```powershell
dotnet user-secrets set "ClickUp:ApiToken" "<your-clickup-personal-api-token>" --project .
```

Do not commit secret values or local override files.

## Health, logging and operational endpoints

| Endpoint / signal | Purpose |
|---|---|
| `GET /health` | Liveness — process up, pipeline wired, no dependency checks. Anonymous. |
| `GET /health/ready` | Readiness — includes a SQL round-trip (`Startup/DatabaseHealthCheck`). Anonymous. |
| `X-Correlation-ID` response header | Set on every response (`Middleware/CorrelationIdMiddleware`); an inbound header of the same name is honoured. The same value is on every Serilog event for that request. |
| `/staffops/admin/audit` | Admin view of the audit tables. |
| `/staffops/platform/tenants` | Platform Admin console: tenant lifecycle, plans, last sync success or failure. |

## Deployment safety checklist

If you deploy it anywhere other than your own machine:

- set `ASPNETCORE_ENVIRONMENT` to `Production` (or `Staging` — anything
  other than `Development` is held to the same rules)
- provide the connection string as `CONNECTIONSTRINGS__UMBRACODBDSN` — the
  base `appsettings.json` leaves it empty on purpose and
  `Startup/ProductionConfigurationGuard` refuses to start if it's empty or
  points at LocalDB
- keep `Umbraco:CMS:Global:UseHttps=true` and `Umbraco:CMS:Hosting:Debug=false`
  (both enforced by the same guard)
- inject secrets from a managed secret store; never put them in appsettings
  files. Shared ClickUp/Hub Planner tokens are refused outside Development;
  each tenant connects its own account
- explicitly set `ReverseProxy:Enabled=false` for direct HTTPS, or `true`
  with `ReverseProxy:KnownProxies` listing the immediate proxy IP addresses
- point readiness probes at `/health/ready` and liveness at `/health`
- grant the operator account the `Platform Admin` Member Group from the
  backoffice (it cannot be granted from inside the app)
- read [`docs/release-readiness.md`](docs/release-readiness.md) and
  [`docs/security-assurance.md`](docs/security-assurance.md) first; the
  product was never cleared for real customer data

## Repository structure highlights

- `Program.cs` bootstraps the ASP.NET Core app and Umbraco host.
- `Directory.Packages.props` pins the central package versions.
- `Composers/` wires up DI and startup handlers per feature area.
- `Migrations/` holds the NPoco migration plans (one subfolder per feature area).
- `Models/`, `Services/`, `Controllers/`, `Views/` are organised by feature
  area, not by technical layer.
- `ProgrammePulse.Tests/` holds the unit, architecture, database and persona
  tests; `ProgrammePulse.Tests/Architecture` holds the rules enforced as tests.
- `docs/` holds the design documents and reviews; `docs/archive` the
  strategy reviews that were superseded.
