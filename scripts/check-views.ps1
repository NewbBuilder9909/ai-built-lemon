<#
.SYNOPSIS
    Type-checks the Razor views, which an ordinary `dotnet build` does not.

.DESCRIPTION
    ProgrammePulse.csproj sets RazorCompileOnBuild=false, because Umbraco's
    InMemoryAuto models mode generates ContentModels.* at runtime. The side
    effect is that .cshtml files are never compiled during a normal build: a
    typo, a renamed view-model property or a deleted helper produces a green
    build and a runtime 500 on the page.

    This runs a throwaway build with Razor compilation turned back on,
    excluding the one view that genuinely cannot compile ahead of time. It
    catches syntax errors, missing usings, wrong model types and stale
    property names across every other view.

    It does NOT replace the render integration tests: those catch runtime
    faults this cannot see (null references on real data, authorization,
    model-binding). Both gates are needed.

.NOTES
    The build output produced here is a check, not an artifact. Run a normal
    `dotnet build` afterwards before publishing.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo

try {
    # Guard the exclusion list. The csproj excludes exactly one view from the
    # check. If another view starts binding to a generated content model, the
    # check would fail for a reason that is not a real defect - so detect that
    # here and say so precisely, rather than letting someone conclude the gate
    # is noisy and disable it.
    $expectedExclusions = @('Views/GardnerPMBlog.cshtml')
    $modelBound = @(
        Get-ChildItem -Path (Join-Path $repo 'Views') -Filter '*.cshtml' -Recurse |
            Where-Object { (Get-Content $_.FullName -Raw) -match 'ContentModels\.|PublishedModels\.' } |
            ForEach-Object { (Resolve-Path $_.FullName -Relative) -replace '\\', '/' -replace '^\./', '' }
    )

    $unexpected = @($modelBound | Where-Object { $expectedExclusions -notcontains $_ })
    if ($unexpected.Count -gt 0) {
        Write-Host ""
        Write-Host "View check cannot cover these - they bind to Umbraco content models" -ForegroundColor Yellow
        Write-Host "generated in memory at runtime, so they cannot compile ahead of time:" -ForegroundColor Yellow
        $unexpected | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        Write-Host ""
        Write-Host "Add them to the CheckRazorViews ItemGroup in ProgrammePulse.csproj," -ForegroundColor Yellow
        Write-Host "and to `$expectedExclusions in this script, then re-run." -ForegroundColor Yellow
        Write-Host ""
        exit 2
    }

    $covered = (@(Get-ChildItem -Path (Join-Path $repo 'Views') -Filter '*.cshtml' -Recurse).Count) - $expectedExclusions.Count
    Write-Host ""
    Write-Host "Type-checking $covered Razor view(s) ($($expectedExclusions.Count) excluded: $($expectedExclusions -join ', '))..." -ForegroundColor Cyan
    Write-Host ""

    & dotnet build ProgrammePulse.csproj -p:CheckRazorViews=true --nologo -v quiet
    $exit = $LASTEXITCODE

    Write-Host ""
    if ($exit -eq 0) {
        Write-Host "All $covered view(s) compiled." -ForegroundColor Green
        Write-Host "Note: this is a check build. Run 'dotnet build' before publishing." -ForegroundColor DarkGray
    }
    else {
        Write-Host "View check FAILED - the errors above are real defects that a normal" -ForegroundColor Red
        Write-Host "build would not have reported, and would have shipped as runtime 500s." -ForegroundColor Red
    }
    Write-Host ""
    exit $exit
}
finally {
    Pop-Location
}
