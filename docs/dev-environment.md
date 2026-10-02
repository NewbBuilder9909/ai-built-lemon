# Development environment

How this repository is wired for local work, why C# Dev Kit is fragile here,
and how to keep building when it breaks.

## Run this first

```powershell
./scripts/dev-doctor.ps1          # check
./scripts/dev-doctor.ps1 -Reset   # check, stop orphaned hosts, clear bin/obj, restore
```

Or the **dev doctor** / **reset build output** tasks in VS Code. It separates
the two failure modes that look identical from inside the editor: *the tooling
crashed* versus *the project is broken*. If it passes and VS Code is still
unhappy, the project is fine and the fault is Dev Kit.

## The layout hazard

This workspace contains full copies of itself:

| Path | What it is | Size |
|---|---|---|
| `.claude/worktrees/<branch>/` | A **live registered `git worktree`** — a real checkout on another branch, with its own `ProgrammePulse.csproj` and the same assembly name | ~1.3 GB |
| `artifacts/security/*-scan/` | Whole-tree copies taken for secret scanning | part of ~1.5 GB |

Six duplicate `.csproj` files, sharing assembly names and root namespace with
the two real ones. That is the root cause of most local tooling trouble:

- **C# Dev Kit** auto-discovers projects across the workspace. Duplicate
  project identities, plus `bin`/`obj` churn across 2.8 GB, destabilise its
  project-system server. When that server dies you get:

  ```
  Cannot find an instance of the
  Microsoft.VisualStudio.ProjectSystem.Server.IComponentExportsService service
  ```

  followed by "external file workspace prompt" and "missing project
  references" errors, which are consequences of the same crash, not separate
  problems.

- **MSBuild** would glob those duplicate `.cs` files into the main project and
  produce thousands of `CS0101` duplicate-type errors.

Confirm `git worktree list` before deleting anything under `.claude/` — that
is a real branch checkout, not scratch. Remove it with `git worktree remove`,
never `rm -rf`, or you leave a stale worktree registration behind.

## How each hazard is contained

| Guard | Where | Prevents |
|---|---|---|
| `dotnet.defaultSolution: ProgrammePulse.slnx` | `.vscode/settings.json` | Dev Kit binding to a worktree copy instead of the real project |
| `files.watcherExclude` for `.claude/`, `artifacts/`, `umbraco/Logs/` | `.vscode/settings.json` | File-watcher churn across 2.8 GB and continuous Serilog writes |
| `DefaultItemExcludes` includes `artifacts/**;.claude/**` | `ProgrammePulse.csproj` | Duplicate sources reaching the compiler |
| `<Compile Remove="ProgrammePulse.Tests\**" />` | `ProgrammePulse.csproj` | The nested test project compiling into the web project |
| `global.json` | repo root | A stray SDK (a 5.0.x is installed on this machine) or a preview SDK silently taking over the build |

`.claude/**` is *also* covered today by the SDK's implicit `**/.*/**`
dot-directory exclusion. It is declared explicitly anyway, because relying on
an implicit default is how this breaks silently later. `dev-doctor.ps1`
asserts the property directly rather than trusting either mechanism.

## Recovering from a Dev Kit outage

In order, least disruptive first:

1. `Ctrl+Shift+P` → **Developer: Reload Window**.
2. Quit VS Code completely and reopen. Reload does not always respawn an
   orphaned project-system server.
3. `Ctrl+Shift+P` → **.NET: Restart Language Server**. Check the **C# Dev
   Kit** output channel for the underlying crash.
4. Run `./scripts/dev-doctor.ps1 -Reset` to clear build output and stop
   orphaned hosts, then reload.

**You are not blocked while any of this is happening.** See below.

## Working without Dev Kit

Every task in `.vscode/tasks.json` is `"type": "process"` invoking the
`dotnet` CLI directly. None route through Dev Kit, so build, watch and test
keep working regardless of its state.

Debugging has a deliberate fallback. `launch.json` carries three configs:

| Config | Debugger | Needs Dev Kit? |
|---|---|---|
| Run ProgrammePulse (Dev Kit) | `dotnet` | **Yes** — resolves the project through the project-system server |
| Run ProgrammePulse (no Dev Kit) | `coreclr` (vsdbg, from the base C# extension) | No — launches an already-built DLL |
| Attach to running ProgrammePulse | `coreclr` | No — attaches to a site started from the terminal |

Breakpoints, stepping and the debug console work identically on all three.
If F5 fails, switch to **Run ProgrammePulse (no Dev Kit)** and carry on; fix
the IDE later.

## Razor views are not checked by `dotnet build`

`RazorCompileOnBuild` and `RazorCompileOnPublish` are `false` (required by
Umbraco's `InMemoryAuto` models mode), so **a `.cshtml` error will not fail
the build** — it surfaces as a runtime 500 in front of whoever opened the
page. This is the largest source of "green build, broken page" here.

Two gates cover it. Run both after changing a view; they catch different
things and neither is sufficient alone.

### 1. Type-check every view (fast, ~7s)

```powershell
./scripts/check-views.ps1      # or the "check views" VS Code task
```

Compiles all 55 views with `-p:CheckRazorViews=true`, catching syntax errors,
missing usings, wrong model types and stale property names. Also runs in CI
(before the Build step, in Debug, so it cannot disturb the Release artifact).

Exactly one view is excluded: `Views/GardnerPMBlog.cshtml`, an empty
auto-generated doctype stub bound to `ContentModels.GardnerPmBlog`, which only
exists at runtime under `InMemoryAuto`. If another view starts binding to a
content model the script **tells you and exits 2** rather than failing
confusingly — add it to the `CheckRazorViews` `ItemGroup` in the csproj and to
`$expectedExclusions` in the script.

What this cannot see: anything that only goes wrong at runtime — a null on
real data, a service that isn't registered, options that don't bind,
authorization.

### 2. Render the page over real HTTP

`ProgrammePulse.Tests/Integration/*RenderIntegrationTests.cs` ask for the
page and assert on the body.

For fast feedback, run unit tests without starting the web host:

```powershell
dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --filter "FullyQualifiedName!~Integration&FullyQualifiedName!~Personas"
```

Run startup, SQL-backed, persona and render coverage separately:

```powershell
dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --filter "FullyQualifiedName~Integration|FullyQualifiedName~Personas"
```

The second command requires `PP_TEST_SQL_CONNECTION`; set
`PP_REQUIRE_SQL_TESTS=1` when a missing SQL service must fail rather than skip.
CI runs both commands independently and supplies both variables for the
integration/render command. The matching VS Code tasks are `unit tests` and
`integration and render tests`.

| Test | Covers |
|---|---|
| `ContractsOverviewRenderIntegrationTests` | `/staffops/contracts/overview`, signed in, with a seeded book containing two overlapping contracts |
| `PurchasePageRenderIntegrationTests` | `/purchase`, anonymous — the only unauthenticated surface, and the commercial front door |

Both follow the same shape: real sign-in where needed, real request, assert on
rendered output. Add one for any view carrying a claim that must not silently
regress.

**A render test whose assertions are all `DoesNotContain` proves nothing** —
it passes against an empty body or an error page. Anchor every such test with
a status-code check and one positive `Contains` first.

## Related

- Commands and architecture: [`../CLAUDE.md`](../CLAUDE.md)
- Release posture and gates: [`release-readiness.md`](release-readiness.md)
