# Modernization plan for the current .NET 10 / Umbraco 18 baseline

## Assessment reference
- Report: [.github/modernize/assessment/reports/report-20260923210709/report.json](../../assessment/reports/report-20260923210709/report.json)
- Repo root: [c:\Users\JWGar\MyUmbraco18Project](../..)
- Current target baseline: .NET SDK 10.0.100, target framework `net10.0`, `Umbraco.Cms` 18.2.0, central package management enabled.
- Recommended path: keep the repo on the current supported .NET 10 + Umbraco 18 pairing and apply upgrade hygiene, compatibility validation, and dependency alignment rather than branching into an alternate migration category.

## Executive summary
The repository is already aligned to the current .NET 10 / Umbraco 18 combination. The assessment findings do not indicate a missing major-version jump; instead, they point to hygiene and compatibility work around package drift, validation coverage, local runtime assumptions, configuration discipline, and release-readiness checks.

This plan therefore narrows the modernization work to the recommended upgrade path only:
1. verify baseline compatibility and package hygiene,
2. align central package versions and framework assumptions,
3. validate build, view, and runtime health,
4. check compatibility and operational risks for the current Umbraco app,
5. gate the app for a stable release on the current stack.

## Scope constraints
- No selected categories beyond the recommended upgrade path.
- No alternative migration branches or detours into unrelated Azure platform rewrites.
- Focus is on the current app baseline, not a broader cloud-readiness transformation.

## Planned workstreams

### 1. Baseline compatibility audit
- Confirm the live stack is the intended target: the .NET 10 SDK, `net10.0` target, and Umbraco 18 package set.
- Review central package versions, package drift, and any stale or unsupported dependency pins.
- Identify any compatibility risks between app code, Umbraco runtime, Azure dependencies, and local dev configuration.

### 2. Version alignment and dependency hygiene
- Use central package management to keep versions consistent,
- review the ICU/runtime pinning used by Umbraco,
- ensure the package set remains aligned to the same .NET/Umbraco lifecycle,
- check for known upstream CVEs or compatibility warnings in direct dependencies without widening scope beyond the recommended path.

### 3. Validation and build hygiene
- Run restore, build, and test validation against the actual project set.
- Run the view validation gate used by the repo (`./scripts/check-views.ps1`) because Razor compilation is intentionally disabled on standard builds.
- Verify the app boots cleanly with the expected development safeguards and health checks.

### 4. Runtime compatibility and startup checks
- Confirm startup guards and environment-sensitive configuration still behave correctly for Development versus non-Development.
- Check Umbraco startup, health endpoints, and any custom middleware or security assumptions in the current app.
- Validate that the repo’s local/Windows-specific assumptions do not conflict with the intended deployment and test targets.

### 5. Release-readiness gate
- Review the upgrade against the repo’s existing testing, security, and operational guardrails.
- Ensure the accepted baseline is stable before any further modernization or cloud migration work proceeds.

## Task plan

1. Baseline compatibility and dependency audit
   - Goal: confirm the repo is intentionally on .NET 10 / Umbraco 18 and capture any drift before changes.

2. Align package versions and upgrade hygiene
   - Goal: normalize central package versions, reconcile Umbraco/runtime alignment, and review direct dependency hygiene.

3. Validate build, tests, and views
   - Goal: prove the repo still compiles and passes the repository’s validation gates without hidden Razor regressions.

4. Check runtime compatibility and health behavior
   - Goal: verify startup, environment checks, health endpoints, and Umbraco startup assumptions remain compatible with the target baseline.

5. Final upgrade readiness review
   - Goal: confirm the current baseline is safe, supported, and ready for ongoing maintenance or subsequent platform modernization.

## Dependencies and sequencing
1. Baseline audit
2. Dependency hygiene and version alignment
3. Build, test, and view validation
4. Runtime compatibility checks
5. Final release readiness signoff

## Validation commands
- `dotnet restore`
- `dotnet build`
- `dotnet test "ProgrammePulse.Tests/ProgrammePulse.Tests.csproj"`
- `./scripts/check-views.ps1`
- `dotnet run` followed by health and readiness checks when needed for runtime validation

## Recommended outcome
The repository should conclude this modernization window with a known-good .NET 10 / Umbraco 18 baseline, documented version alignment, passing validation gates, and no compatibility gaps that would block the current release or a later platform migration.
