<#
.SYNOPSIS
Runs the persona end-to-end authorization tests and writes a reviewable report.
.DESCRIPTION
Unlike scripts/agent-delivery-gate.ps1, this gate does not re-implement any
authorization rule. Restating "who may do what" in PowerShell would create a
second definition of the product's access control, and a green gate would
then prove only that the two copies agree. The rules live in the application;
this script runs the tests that exercise them and shapes the result.
.EXAMPLE
powershell -NoProfile -File scripts/persona-gate.ps1
.EXAMPLE
powershell -NoProfile -File scripts/persona-gate.ps1 -OutputDirectory artifacts/persona-gate
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$LocalDbInstance = '(localdb)\GardnerDB'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts/persona-gate' }
$schemaVersion = 1
$issues = New-Object System.Collections.Generic.List[object]
$stageResults = New-Object System.Collections.Generic.List[object]

function Add-Issue([string]$Code, [string]$Stage, [string]$Message, [string]$Reference = '') {
    $script:issues.Add([ordered]@{ code = $Code; stage = $Stage; message = $Message; reference = $Reference })
}
function Stage([string]$Name, [int]$Number, [string]$Status, [object]$Evidence) {
    $script:stageResults.Add([ordered]@{ number = $Number; name = $Name; status = $Status; evidence = $Evidence })
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

# Stage 1: environment. A persona suite that silently no-ops without LocalDB
# would report success having proved nothing, so absence of a database is
# INCONCLUSIVE, never GREEN.
$stage = 'environment'
$localDbReachable = $false
try {
    $connection = New-Object System.Data.SqlClient.SqlConnection "Server=$LocalDbInstance;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5;"
    $connection.Open()
    $localDbReachable = $true
    $connection.Close()
} catch {
    Add-Issue 'NO_LOCALDB' $stage "LocalDB '$LocalDbInstance' is unreachable. Persona tests cannot exercise the real application."
}
Stage 'Environment' 1 $(if ($localDbReachable) { 'PASS' } else { 'INCONCLUSIVE' }) @{ localDb = $localDbReachable; instance = $LocalDbInstance }

# Stage 2: run the persona tests against the real composition.
$stage = 'persona-tests'
$trxName = 'persona-tests.trx'
$trxPath = Join-Path $OutputDirectory $trxName
if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath -Force }

$executed = 0; $passed = 0; $failed = 0
$testStatus = 'FAIL'
if ($localDbReachable) {
    Push-Location $repo
    try {
        & dotnet test ProgrammePulse.slnx --nologo `
            --filter 'FullyQualifiedName~ProgrammePulse.Tests.Personas' `
            --logger "trx;LogFileName=$trxName" `
            --results-directory $OutputDirectory | Out-Host
    } finally { Pop-Location }

    if (Test-Path -LiteralPath $trxPath) {
        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
        $counters = $trx.TestRun.ResultSummary.Counters
        $executed = [int]$counters.executed
        $passed = [int]$counters.passed
        $failed = [int]$counters.failed

        foreach ($result in @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' })) {
            Add-Issue 'PERSONA_TEST_FAILED' $stage $result.testName
        }

        # A filter that matches nothing exits zero. Zero executed tests is a
        # red gate, not a quiet pass.
        if ($executed -eq 0) { Add-Issue 'NO_TESTS_EXECUTED' $stage 'The persona filter matched no tests.' }
        if ($executed -gt 0 -and $failed -eq 0) { $testStatus = 'PASS' }
    } else {
        Add-Issue 'NO_TRX' $stage "dotnet test produced no result file at $trxPath"
    }
} else {
    $testStatus = 'INCONCLUSIVE'
}
Stage 'Persona journeys and boundaries' 2 $testStatus @{ executed = $executed; passed = $passed; failed = $failed }

# Stage 3: evidence. The measured denial shape must exist and must record a
# real refusal, so a suite that passed without issuing requests is caught.
$stage = 'evidence'
$observationPath = Join-Path $repo 'ProgrammePulse.Tests/bin/Debug/net10.0/artifacts/persona-gate/phase0-observations.json'
$evidenceStatus = 'FAIL'
$denialCount = 0
$deniedShapes = @()
if ($localDbReachable) {
    if (Test-Path -LiteralPath $observationPath) {
        $observations = Get-Content -LiteralPath $observationPath -Raw | ConvertFrom-Json
        $deniedShapes = @($observations.denialShapes | Where-Object { $_.outcome -eq 'Denied' })
        $denialCount = $deniedShapes.Count
        if ($denialCount -gt 0 -and $observations.allowedShape.outcome -eq 'Allowed') {
            $evidenceStatus = 'PASS'
        } else {
            Add-Issue 'WEAK_EVIDENCE' $stage 'Observations record no denial, or no allowed journey to contrast it with.' $observationPath
        }
    } else {
        Add-Issue 'NO_OBSERVATIONS' $stage 'No measured persona observations were written.' $observationPath
    }
} else {
    $evidenceStatus = 'INCONCLUSIVE'
}
Stage 'Measured evidence' 3 $evidenceStatus @{ observationsPath = $observationPath; denialsRecorded = $denialCount }

# Stage 4: deliberately RED until the outstanding gate items are built.
# Mirrors scripts/agent-delivery-gate.ps1 stage 4: an unbuilt capability must
# never be able to show green.
$stage = 'outstanding'
$outstanding = @(
    @{ id = 'CROSS-TENANT-SENTINEL'; description = 'Only cost leakage is proven over HTTP. No second tenant with its own sentinel data is seeded, so cross-tenant body leakage is unproven.' },
    @{ id = 'UNROUTED-CAPABILITIES'; description = 'ViewTeamCapacity, ViewEstimatorHistory, ManageEstimateCalibration and ManageCustomers have no distinct route and are asserted only by the pure capability tests, not over HTTP.' },
    @{ id = 'WRITE-ALLOWANCES'; description = 'The matrix asserts write denials over HTTP but not write successes; proving a write succeeds needs seeded programme data the fixture does not create yet.' }
)
foreach ($item in $outstanding) { Add-Issue 'OUTSTANDING' $stage $item.description $item.id }
Stage 'Outstanding gate items' 4 'RED' @{ count = $outstanding.Count; items = $outstanding }

$statuses = @($stageResults.ToArray() | ForEach-Object { $_.status })
$gate = if ($statuses -contains 'INCONCLUSIVE') { 'INCONCLUSIVE' }
        elseif ($statuses -contains 'FAIL' -or $statuses -contains 'RED') { 'RED' }
        else { 'GREEN' }

$report = [ordered]@{
    schemaVersion = $schemaVersion
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    gate = $gate
    stageResults = @($stageResults.ToArray())
    issues = @($issues.ToArray())
}
$reportPath = Join-Path $OutputDirectory 'report.json'
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding UTF8

Write-Host "Persona gate: $gate. Report: $reportPath"
foreach ($result in $stageResults) { Write-Host ("  {0}. {1}: {2}" -f $result.number, $result.name, $result.status) }
foreach ($issue in $issues) { Write-Host "[$($issue.stage)] $($issue.code): $($issue.message) $($issue.reference)" }

if ($gate -ne 'GREEN') { exit 1 }
