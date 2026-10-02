# Modernization Plan: Razor, OpenTelemetry, and Test Efficiency

**Project**: ProgrammePulse

## Technical Framework

- **Language**: C# / .NET 10.0.100
- **Framework**: ASP.NET Core with Umbraco CMS 18.2.0
- **Build Tool**: .NET SDK / MSBuild
- **Database**: SQL Server for Umbraco and integration tests
- **Key Dependencies**: Razor SDK, xUnit, ASP.NET Core MVC testing,
  Azure Monitor OpenTelemetry, Umbraco CMS

## Overview

This plan hardens the repository's build and validation workflow without
changing application deployment or infrastructure. It will make Razor view
validation a dependable build/CI gate, remediate the centrally managed
OpenTelemetry advisory with evidence from the existing dependency audit, and
separate fast unit feedback from SQL-backed startup and render verification.
The existing Umbraco `InMemoryAuto` exception remains explicit and tested.

Execution is sequential: establish the Razor gate, partition test execution,
then remediate and verify dependency security. Each phase must resolve its
own failures before the next phase begins.

## Scope and Validation

Razor work is limited to `ProgrammePulse.csproj`,
`scripts/check-views.ps1`, `.github/workflows/ci.yml`, `.vscode/tasks.json`,
and `docs/dev-environment.md`. It must preserve
`RazorCompileOnBuild=false`, the conditional `CheckRazorViews=true` override,
and the sole `Views/GardnerPMBlog.cshtml` exclusion. Validation includes the
script's exclusion-list guard and its `dotnet build ProgrammePulse.csproj
-p:CheckRazorViews=true --nologo -v quiet` check, followed by the normal build
and the CI-equivalent Razor step.

Dependency work is limited to `Directory.Packages.props`,
`ProgrammePulse.csproj`, `scripts/test-dependency-audit.ps1`,
`.github/workflows/ci.yml`, and any narrowly required dependency evidence or
policy convention. The OpenTelemetry.Api NU1902 advisory must be resolved
through the centrally managed `Azure.Monitor.OpenTelemetry.AspNetCore` chain;
post-change audit output must remain at
`artifacts/security/nuget-vulnerabilities.json` and contain no vulnerable
packages or audit diagnostics.

Test efficiency work is limited to `ProgrammePulse.Tests/ProgrammePulse.Tests.csproj`,
the existing unit and integration/render test files, `.github/workflows/ci.yml`,
`.vscode/tasks.json`, `docs/dev-environment.md`, and `CLAUDE.md` as needed.
It must preserve every test, xUnit conventions, the
`IntegrationTestCollection`/`ProgrammePulseWebApplicationFactory` boundary,
and `PP_TEST_SQL_CONNECTION` plus `PP_REQUIRE_SQL_TESTS` for SQL-backed tests.
Fast unit and startup-heavy integration/render commands must be independently
runnable and both remain covered in CI.

The final execution step inspects `git status` and the complete diff, confirms
that only intended plan-scope files changed, preserves unrelated user changes,
and commits the completed modernization work with an explicit message.

## Open Questions & Questionnaire

No clarification questions were required; the user specified the scope,
anchors, validation requirements, sequencing, and commit behavior.
