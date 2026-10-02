# Branch consolidation: 23 September 2026

The user confirmed `main` as the target; this repository has no `master`. Work was consolidated locally without pushing, rewriting existing commits, deleting branches or modifying the separate skills worktree. The starting `main` commit was `54dc612`.

## Branch map

| Branch | Scope and disposition |
|---|---|
| `chore/sql-and-razor-test-gates` | SQL fail-closed checks, isolated purchase-page tests, Razor check script and CI/IDE integration. Committed as `071ed80`, merged into `main`. |
| `fix/contested-cost-reporting` | Distinguishes disputed cost from the overstatement a naive sum would produce; includes domain and render tests. Committed as `7fc3ca4`, merged into `main`. |
| `feature/exco-market-and-delivery-workspace` | EXCO snapshots, decision journal, market settings, delivery-load reporting, shared permissions/navigation and documentation. Committed as `23c9790`, merged into `main`. Shared permission and UI changes are kept together. |
| `feature/skills-evidence-and-service-health` | Descriptive alias for the existing `feature/skills-evidence-slice1` tip `c731c88`; includes all five slices, not only skills slice 1. The original branch/worktree is preserved. |
| `integration/skills-evidence-reconciliation` | Reconciles the above skills/service branch with EXCO and delivery changes, then applies mandatory SQL checks to its 32 integration-test guards. Merged into `main`. |
| `chore/dependency-refresh-2026-09-23` | Merges all eight outstanding Dependabot branch tips, retaining their ancestry; merged into `main`. |
| `docs/consolidation-and-release-gates` | This integration record and updated claims, merged into `main`. |

Other pre-existing local feature branches were already ancestors of `main`. After consolidation, both `git branch --no-merged main` and `git branch -r --no-merged main` should be empty. Remote branches and pull requests are not updated until a separate push. The unauthenticated GitHub CLI could not inspect PR status; remote branch inventory was obtained with a successful `git fetch origin`.

## Conflict resolutions

Nine files conflicted when merging skills/service evidence: the capability registry, role matrix, six persona contracts, and the skills design document. The resolution preserves both sets of capabilities. Persona JSON was parsed and combined with a check that no capability appeared in both `can` and `cannot`. Board/Analyst operational reading does not acquire executive-decision writes or access to other people's skills. Platform Admin remains separate from tenant-business access. Both navigation additions and data-governance inventories are retained. The skills document keeps the implemented status and its explicit unresolved gaps, replacing the obsolete statement that nothing was implemented.

Three dependency merges conflicted in `Directory.Packages.props`; each retained the prior updates instead of replacing the file with an older branch version. Final requested versions:

- Umbraco CMS and development backoffice: `18.2.0` together.
- ASP.NET MVC Testing: `10.0.12`; Microsoft.NET.Test.Sdk: `18.10.1`; coverlet.collector: `10.0.1`.
- GitHub Actions: checkout `v7`, setup-dotnet `v6`, upload-artifact `v7`.

Upstream compatibility references: [Umbraco 18.2.0 release notes](https://github.com/umbraco/Umbraco-CMS/releases/tag/release-18.2.0), [checkout](https://github.com/actions/checkout/blob/v7/README.md), [setup-dotnet](https://github.com/actions/setup-dotnet/blob/v6/README.md), [upload-artifact](https://github.com/actions/upload-artifact/blob/v7/README.md). Hosted CI has not been run by this local consolidation.

## Verification

- Full suite on merged `main`: **1,086 passed, 0 failed, 0 skipped**, including real SQL and HTTP tests. A new disposable database exercised migration startup.
- Razor compilation passed with zero errors. Two existing duplicate `RAID Register` resource warnings remain.
- NuGet vulnerability audit passed for the solution, including transitive packages.
- Evidence lives under ignored `artifacts/consolidation-2026-09-23/results/`: `full-suite.trx` and `nuget-vulnerabilities.json`. A coverage-enabled repeat uses `full-suite-coverage.trx` and the collector's generated coverage directory.

Repeat without touching a customer database:

```powershell
$env:PP_TEST_SQL_CONNECTION = 'Server=(localdb)\GardnerDB;Database=UmbracoBase_Consolidation_20260923_IntegrationTests;Integrated Security=true;TrustServerCertificate=true;'
$env:PP_REQUIRE_SQL_TESTS = '1'
Remove-Item Env:PP_SKIP_SQL_TESTS -ErrorAction SilentlyContinue
dotnet test ProgrammePulse.Tests/ProgrammePulse.Tests.csproj --configuration Release --artifacts-path artifacts/consolidation-2026-09-23/build --logger 'trx;LogFileName=full-suite.trx' --results-directory artifacts/consolidation-2026-09-23/results
dotnet build ProgrammePulse.csproj --configuration Release --artifacts-path artifacts/consolidation-2026-09-23/razor -p:CheckRazorViews=true
dotnet restore ProgrammePulse.slnx
./scripts/test-dependency-audit.ps1 -OutputPath artifacts/consolidation-2026-09-23/results/nuget-vulnerabilities.json
```

No customer deployment or persistent preview was started. GitHub CI, browser/mobile visual validation, live connector acceptance and professional Welsh review are not established by these tests. `ExecutiveReview:Enabled` remains false.

## Outstanding work: proposed next branches

These are proposed branch names, not empty branches created now and not claims of completed implementation. Order them by release risk and dependency:

| Proposed branch | Bounded deliverable |
|---|---|
| `feature/executive-data-lifecycle` | Export, retention, governed redaction/withdrawal and tenant purge for executive records; first gate before identifiable-data pilots. |
| `feature/reviewed-pack-publication` | Formal whole-pack approval, stronger provenance, material exceptions and reviewed-pack comparisons. |
| `feature/commercial-effort-allocation` | Versioned source-effort allocation, concurrent split limits, reconciliation and currency-safe financial projections. |
| `feature/market-calendars-and-localisation` | Versioned working calendars, saved user preferences and completed selected language journeys. |
| `feature/evidence-component-registry` | Shared tenant-scoped component identity and reviewed aliases joining skills/continuity and support evidence, without inferring individual blame. |
| `chore/pilot-release-assurance` | Browser/translation checks, restore rehearsal, live vendor tests, customer reconciliation and one additional-market acceptance record. Some gates require people and live environments, not code. |

The detailed remaining constraints are in [EXCO increment 2](../executive-review-increment-2.md#still-outstanding) and [skills/evidence gaps](../staff-skills-evidence-module.md#gaps-against-this-design-21-september-2026). Consolidation does not complete those product or operating commitments.

Subsequent local work: `feature/executive-data-lifecycle` now implements the first bounded code deliverable above. See its [implementation and verification record](../executive-data-lifecycle.md). It is not merged by this consolidation record, and operating acceptance remains outstanding.
