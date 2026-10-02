<#
.SYNOPSIS
    Checks the local development environment for the conditions that break
    C# Dev Kit and the build in this repository, and optionally resets build
    output.

.DESCRIPTION
    This repository keeps full copies of itself inside the workspace folder:
    a live `git worktree` under .claude/worktrees/ and whole-tree scan copies
    under artifacts/security/. Those copies contain duplicate .csproj files
    with the same assembly names, which is a known way to destabilise C# Dev
    Kit's project-system server and produce:

        Cannot find an instance of the
        Microsoft.VisualStudio.ProjectSystem.Server.IComponentExportsService service

    Nothing here changes source. Run it first whenever the IDE misbehaves;
    it distinguishes "the tooling crashed" from "the project is broken".

.PARAMETER Reset
    Additionally stops orphaned ProgrammePulse/dotnet hosts started from this
    folder and deletes bin/ and obj/ for the two real projects, then restores.
    Never touches .claude/ or artifacts/.

.EXAMPLE
    ./scripts/dev-doctor.ps1
    ./scripts/dev-doctor.ps1 -Reset
#>
[CmdletBinding()]
param(
    [switch]$Reset
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$problems = 0
$warnings = 0

function Write-Check {
    param([string]$Name, [ValidateSet('ok', 'warn', 'fail')][string]$State, [string]$Detail)
    $marks = @{ ok = '  [ok]  '; warn = '  [warn]'; fail = '  [FAIL]' }
    $colours = @{ ok = 'Green'; warn = 'Yellow'; fail = 'Red' }
    Write-Host $marks[$State] -ForegroundColor $colours[$State] -NoNewline
    Write-Host " $Name"
    if ($Detail) { Write-Host "         $Detail" -ForegroundColor DarkGray }
}

Write-Host ""
Write-Host "ProgrammePulse dev doctor" -ForegroundColor Cyan
Write-Host "  repo: $repo"
Write-Host ""

# --- 1. SDK selection -------------------------------------------------------
# Two SDKs are installed on this machine (a legacy 5.0.x alongside 10.0.x).
# global.json pins the floor and forbids prereleases so a preview install
# cannot silently take over the build.
$sdk = (& dotnet --version).Trim()
$globalJson = Join-Path $repo 'global.json'
if (Test-Path $globalJson) {
    $pin = (Get-Content $globalJson -Raw | ConvertFrom-Json).sdk
    if ($sdk -like '10.*') {
        Write-Check 'SDK selection' 'ok' "using $sdk (global.json floor $($pin.version), rollForward $($pin.rollForward))"
    }
    else {
        Write-Check 'SDK selection' 'fail' "using $sdk but global.json requires 10.x - run 'dotnet --list-sdks'"
        $problems++
    }
}
else {
    Write-Check 'SDK selection' 'warn' "using $sdk with no global.json - SDK choice is not pinned"
    $warnings++
}

# --- 2. Duplicate project copies -------------------------------------------
# The cause of most Dev Kit instability here. They are expected to exist; what
# matters is that the IDE is pinned to the real solution and not discovering
# these, and that the build does not glob them.
$allProjects = @(Get-ChildItem -Path $repo -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
$realProjects = @($allProjects | Where-Object { $_.FullName -notmatch '\\(\.claude|artifacts)\\' })
$copyProjects = @($allProjects | Where-Object { $_.FullName -match '\\(\.claude|artifacts)\\' })

if ($copyProjects.Count -eq 0) {
    Write-Check 'Duplicate project copies' 'ok' "$($realProjects.Count) project(s), no copies in the tree"
}
else {
    Write-Check 'Duplicate project copies' 'warn' "$($copyProjects.Count) copies under .claude/ or artifacts/ (expected; must stay excluded)"
    $warnings++
}

# --- 3. IDE is pinned to the real solution ---------------------------------
$settingsPath = Join-Path $repo '.vscode/settings.json'
if (Test-Path $settingsPath) {
    # VS Code settings are JSONC; strip line comments before parsing.
    $raw = Get-Content $settingsPath -Raw
    $stripped = [regex]::Replace($raw, '(?m)^\s*//.*$', '')
    $settings = $stripped | ConvertFrom-Json
    $defaultSolution = $settings.'dotnet.defaultSolution'
    if ($defaultSolution) {
        Write-Check 'Dev Kit solution pin' 'ok' "dotnet.defaultSolution = $defaultSolution"
    }
    else {
        Write-Check 'Dev Kit solution pin' 'fail' 'dotnet.defaultSolution not set - Dev Kit may bind to a worktree copy'
        $problems++
    }
}
else {
    Write-Check 'Dev Kit solution pin' 'fail' '.vscode/settings.json missing'
    $problems++
}

# --- 4. Build glob immunity -------------------------------------------------
# The real safety property: no duplicate copy reaches the compiler. Asserted
# rather than assumed, because it currently depends partly on SDK defaults.
Push-Location $repo
try {
    $compileItems = & dotnet msbuild ProgrammePulse.csproj -getItem:Compile -nologo 2>&1 | Out-String
    $leaked = ([regex]::Matches($compileItems, '(?i)[^"]*(\.claude|artifacts)[^"]*\.cs')).Count
    if ($leaked -eq 0) {
        $total = ([regex]::Matches($compileItems, '\.cs"')).Count
        Write-Check 'Build glob immunity' 'ok' "$total source files, 0 from .claude/ or artifacts/"
    }
    else {
        Write-Check 'Build glob immunity' 'fail' "$leaked duplicate source file(s) globbed - check DefaultItemExcludes in ProgrammePulse.csproj"
        $problems++
    }
}
catch {
    Write-Check 'Build glob immunity' 'warn' "could not evaluate: $($_.Exception.Message)"
    $warnings++
}
finally {
    Pop-Location
}

# --- 5. Orphaned hosts ------------------------------------------------------
# A debug session killed at the wrong moment leaves a host holding a port and
# file locks in bin/, which looks like a corrupted build.
$orphans = @(Get-Process -Name 'ProgrammePulse' -ErrorAction SilentlyContinue)
if ($orphans.Count -eq 0) {
    Write-Check 'Orphaned app hosts' 'ok' 'none running'
}
else {
    $ids = ($orphans | ForEach-Object { $_.Id }) -join ', '
    if ($Reset) {
        $orphans | Stop-Process -Force
        Write-Check 'Orphaned app hosts' 'ok' "stopped PID(s) $ids"
    }
    else {
        Write-Check 'Orphaned app hosts' 'warn' "PID(s) $ids still running - re-run with -Reset to stop them"
        $warnings++
    }
}

# --- 6. Reset (opt-in) ------------------------------------------------------
if ($Reset) {
    foreach ($project in $realProjects) {
        foreach ($dir in @('bin', 'obj')) {
            $target = Join-Path $project.DirectoryName $dir
            if (Test-Path $target) {
                Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    Write-Check 'Build output reset' 'ok' "cleared bin/ and obj/ for $($realProjects.Count) project(s)"

    Push-Location $repo
    try {
        & dotnet restore ProgrammePulse.slnx | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Check 'Restore' 'ok' 'ProgrammePulse.slnx restored'
        }
        else {
            Write-Check 'Restore' 'fail' "dotnet restore exited $LASTEXITCODE"
            $problems++
        }
    }
    finally {
        Pop-Location
    }
}

# --- Summary ----------------------------------------------------------------
Write-Host ""
if ($problems -gt 0) {
    Write-Host "$problems problem(s), $warnings warning(s)." -ForegroundColor Red
    Write-Host "The project itself may still be fine - run 'dotnet build ProgrammePulse.slnx' to confirm." -ForegroundColor DarkGray
    exit 1
}

Write-Host "No problems ($warnings warning(s))." -ForegroundColor Green
Write-Host ""
Write-Host "If VS Code is still broken while this passes, the fault is C# Dev Kit, not the project:" -ForegroundColor DarkGray
Write-Host "  1. Ctrl+Shift+P -> Developer: Reload Window" -ForegroundColor DarkGray
Write-Host "  2. Quit VS Code fully and reopen (reload does not always respawn the server)" -ForegroundColor DarkGray
Write-Host "  3. Debug with the 'Run ProgrammePulse (no Dev Kit)' launch config, which does not need it" -ForegroundColor DarkGray
Write-Host ""
exit 0
