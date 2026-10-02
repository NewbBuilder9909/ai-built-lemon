<#
.SYNOPSIS
Replays the four-stage agent delivery assurance contract and writes a reviewable report.
.EXAMPLE
powershell -NoProfile -File scripts/agent-delivery-gate.ps1
.EXAMPLE
powershell -NoProfile -File scripts/agent-delivery-gate.ps1 -InputPath path/to/export.json -RunDotnetTests
#>
[CmdletBinding()]
param(
    [string]$InputPath,
    [string]$OutputDirectory,
    [switch]$RunDotnetTests
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $InputPath) { $InputPath = Join-Path $PSScriptRoot 'agent-delivery-fixture.json' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts/agent-delivery-gate' }
$schemaVersion = 1
$issues = New-Object System.Collections.Generic.List[object]
$stageResults = New-Object System.Collections.Generic.List[object]

function Add-Issue([string]$Code, [string]$Stage, [string]$Message, [string]$Reference = '') {
    $script:issues.Add([ordered]@{ code = $Code; stage = $Stage; message = $Message; reference = $Reference })
}
function Test-Value($Value) { return ($null -ne $Value -and "$Value".Trim().Length -gt 0) }
function Stage([string]$Name, [int]$Number, [bool]$Passed, [object]$Evidence) {
    $script:stageResults.Add([ordered]@{ number = $Number; name = $Name; passed = $Passed; evidence = $Evidence })
}
function Require([object]$Record, [string[]]$Fields, [string]$StageName, [string]$Reference) {
    $valid = $true
    foreach ($field in $Fields) {
        if (-not (Test-Value $Record.$field)) {
            Add-Issue 'REQUIRED_FIELD' $StageName "Missing $field" $Reference
            $valid = $false
        }
    }
    return $valid
}

if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) { throw "Input file missing: $InputPath" }
$inputData = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

# Stage 1: source-neutral contract, scoped identity, provenance, and minimal data.
$stage = 'contract'
$contractOk = Require $inputData @('schemaVersion','tenantId','asOfUtc') $stage 'manifest'
if ($inputData.schemaVersion -ne $schemaVersion) { Add-Issue 'SCHEMA_VERSION' $stage "Expected $schemaVersion" 'manifest'; $contractOk = $false }
$keys = @{}
$entities = @(
    @{ name = 'workItems'; fields = @('id','source','connectionId','externalId','title','tenantId') },
    @{ name = 'runs'; fields = @('id','source','connectionId','externalId','tenantId','workItemId','actorType','status','startedUtc') },
    @{ name = 'artifacts'; fields = @('id','runId','kind','url','tenantId') },
    @{ name = 'decisions'; fields = @('id','artifactId','kind','status','tenantId') }
)
foreach ($entity in $entities) {
    if ($null -eq $inputData.($entity.name)) { Add-Issue 'MISSING_COLLECTION' $stage "Missing $($entity.name)"; $contractOk = $false; continue }
    foreach ($record in @($inputData.($entity.name))) {
        $reference = "$($entity.name):$($record.id)"
        if (-not (Require $record $entity.fields $stage $reference)) { $contractOk = $false }
        if ($record.tenantId -ne $inputData.tenantId) { Add-Issue 'CROSS_TENANT' $stage 'Record belongs to another tenant' $reference; $contractOk = $false }
        if ($entity.name -eq 'runs' -or $entity.name -eq 'workItems') {
            $key = "$($record.tenantId)|$($record.source)|$($record.connectionId)|$($record.externalId)"
            if ($keys.ContainsKey("$($entity.name)|$key")) { Add-Issue 'DUPLICATE_SOURCE_KEY' $stage 'Source identity is duplicated' $reference; $contractOk = $false }
            $keys["$($entity.name)|$key"] = $true
        }
    }
}
Stage 'Source-neutral contract' 1 $contractOk @{ workItems = @($inputData.workItems).Count; runs = @($inputData.runs).Count; artifacts = @($inputData.artifacts).Count; decisions = @($inputData.decisions).Count }

# Stage 2: only accepted, reviewed, tested and released work counts as delivery.
$stage = 'pilot'
$pilotOk = $contractOk
$workRows = New-Object System.Collections.Generic.List[object]
$runIds = @{}
$artifactIds = @{}
foreach ($run in @($inputData.runs)) {
    $runIds["$($run.id)"] = $run
    if ($run.actorType -notin @('human','agent')) { Add-Issue 'ACTOR_TYPE' $stage 'Actor must be human or agent' $run.id; $pilotOk = $false }
    if ($run.status -notin @('queued','running','blocked','failed','completed')) { Add-Issue 'RUN_STATUS' $stage 'Unknown run status' $run.id; $pilotOk = $false }
    if (-not (@($inputData.workItems | Where-Object { $_.id -eq $run.workItemId }).Count)) { Add-Issue 'ORPHAN_RUN' $stage 'Run has no work item' $run.id; $pilotOk = $false }
}
foreach ($artifact in @($inputData.artifacts)) {
    $artifactIds["$($artifact.id)"] = $artifact
    if (-not $runIds.ContainsKey("$($artifact.runId)")) { Add-Issue 'ORPHAN_ARTIFACT' $stage 'Artifact has no run' $artifact.id; $pilotOk = $false }
}
foreach ($decision in @($inputData.decisions)) {
    if (-not $artifactIds.ContainsKey("$($decision.artifactId)")) { Add-Issue 'ORPHAN_DECISION' $stage 'Decision has no artifact' $decision.id; $pilotOk = $false }
}
foreach ($item in @($inputData.workItems)) {
    $itemRuns = @($inputData.runs | Where-Object { $_.workItemId -eq $item.id })
    $eligible = @($itemRuns | Where-Object { $_.status -eq 'completed' -and (Test-Value $_.completedUtc) })
    $accepted = $false
    foreach ($run in $eligible) {
        foreach ($artifact in @($inputData.artifacts | Where-Object { $_.runId -eq $run.id -and $_.kind -eq 'pullRequest' })) {
            $associated = @($inputData.decisions | Where-Object { $_.artifactId -eq $artifact.id })
            $review = @($associated | Where-Object { $_.kind -eq 'review' -and $_.status -eq 'approved' }).Count -gt 0
            $ci = @($associated | Where-Object { $_.kind -eq 'ci' -and $_.status -eq 'passed' }).Count -gt 0
            $release = @($associated | Where-Object { $_.kind -eq 'release' -and $_.status -eq 'deployed' }).Count -gt 0
            $business = @($associated | Where-Object { $_.kind -eq 'acceptance' -and $_.status -eq 'accepted' }).Count -gt 0
            if ($review -and $ci -and $release -and $business) { $accepted = $true }
        }
    }
    $workRows.Add([ordered]@{ workItemId = $item.id; title = $item.title; runCount = $itemRuns.Count; accepted = $accepted; state = $(if ($accepted) { 'accepted' } elseif ($eligible.Count) { 'awaiting-evidence' } elseif (@($itemRuns | Where-Object { $_.status -eq 'blocked' }).Count) { 'blocked' } else { 'in-progress-or-failed' }) })
}
Stage 'End-to-end pilot replay' 2 $pilotOk @{ work = $workRows.ToArray(); acceptedCount = @($workRows.ToArray() | Where-Object { $_.accepted }).Count }

# Stage 3: explicit adversarial assertions in the fixture, plus relationship checks above.
$stage = 'negative-cases'
$negativeOk = $pilotOk
if ($null -eq $inputData.expectations) { Add-Issue 'MISSING_EXPECTATIONS' $stage 'Expected accepted and rejected IDs are required'; $negativeOk = $false }
else {
    foreach ($id in @($inputData.expectations.acceptedWorkItemIds)) {
        if (-not @($workRows.ToArray() | Where-Object { $_.workItemId -eq $id -and $_.accepted }).Count) { Add-Issue 'EXPECTED_ACCEPTED' $stage 'Expected accepted work is not accepted' $id; $negativeOk = $false }
    }
    foreach ($id in @($inputData.expectations.rejectedWorkItemIds)) {
        if (-not @($workRows.ToArray() | Where-Object { $_.workItemId -eq $id -and -not $_.accepted }).Count) { Add-Issue 'FALSE_DELIVERY' $stage 'Unreviewed or incomplete work counted as accepted' $id; $negativeOk = $false }
    }
    if (@($inputData.expectations.rejectedWorkItemIds).Count -eq 0) { Add-Issue 'NO_NEGATIVE_CASES' $stage 'At least one rejected case is required'; $negativeOk = $false }
}
Stage 'Negative-case assertions' 3 $negativeOk @{ rejectedCount = @($inputData.expectations.rejectedWorkItemIds).Count; assertionIssues = @($issues.ToArray() | Where-Object { $_.stage -eq $stage }).Count }

# Stage 4: measure outcomes; never manufacture savings from absent pilot data.
$stage = 'gtm'
$measurements = @($inputData.measurements)
$measured = @($measurements | Where-Object { $null -ne $_.baselineMinutes -and $null -ne $_.pilotMinutes })
$validMeasurements = $true
foreach ($measurement in $measured) {
    if ($measurement.baselineMinutes -lt 0 -or $measurement.pilotMinutes -lt 0) { Add-Issue 'INVALID_MEASUREMENT' $stage 'Minutes cannot be negative' $measurement.id; $validMeasurements = $false }
}
$baseline = 0.0; $pilot = 0.0
foreach ($measurement in $measured) { $baseline += [double]$measurement.baselineMinutes; $pilot += [double]$measurement.pilotMinutes }
$savings = if ($measured.Count -gt 0) { [math]::Round($baseline - $pilot, 2) } else { $null }
$gtmOk = $validMeasurements -and $measured.Count -gt 0
if (-not $gtmOk -and $validMeasurements) { Add-Issue 'NO_LIVE_MEASUREMENTS' $stage 'Record baseline and pilot time from a design partner before claiming ROI' }
Stage 'GTM measurement' 4 $gtmOk @{ measuredCases = $measured.Count; baselineMinutes = $(if ($measured.Count) { $baseline } else { $null }); pilotMinutes = $(if ($measured.Count) { $pilot } else { $null }); savedMinutes = $savings; measurementComplete = $gtmOk }

if ($RunDotnetTests) {
    Push-Location $repo
    try {
        & dotnet test ProgrammePulse.slnx --configuration Release --nologo
        if ($LASTEXITCODE -ne 0) { Add-Issue 'DOTNET_TEST_FAILED' 'build' "dotnet test exited $LASTEXITCODE" }
    } finally { Pop-Location }
}
$passed = @($stageResults.ToArray() | Where-Object { $_.passed }).Count
$report = [ordered]@{
    schemaVersion = $schemaVersion
    tenantId = $inputData.tenantId
    asOfUtc = $inputData.asOfUtc
    stageResults = @($stageResults.ToArray())
    issues = @($issues.ToArray())
    gate = $(if ($passed -eq 4 -and -not @($issues.ToArray() | Where-Object { $_.stage -eq 'build' }).Count) { 'GREEN' } else { 'RED' })
}
$reportPath = Join-Path $OutputDirectory 'report.json'
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host "Agent delivery gate: $($report.gate) ($passed/4 stages passed). Report: $reportPath"
foreach ($issue in $issues) { Write-Host "[$($issue.stage)] $($issue.code): $($issue.message) $($issue.reference)" }
if ($report.gate -ne 'GREEN') { exit 1 }
