<#
.SYNOPSIS
Captures revision and source hashes without collecting file contents or secrets.
.DESCRIPTION
Run after checks, in a frozen checkout. This is provenance, NOT a passing test
report, SBOM, signature, penetration test or release approval. Store reports in
the same restricted evidence system and record their hashes separately.
#>
[CmdletBinding()]
param([switch]$RequireClean)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    function Read-Git([string[]]$Arguments) {
        $result = @(& git @Arguments)
        if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments[0])" }
        return $result
    }
    function Get-SourceInventory {
        $paths = @(Read-Git @('-c', 'core.quotepath=false', 'ls-files', '--cached', '--others', '--exclude-standard') | Sort-Object -Unique)
        foreach ($path in $paths) {
            # Evidence covers source present in the checkout, including untracked
            # non-ignored files. Git status separately records deleted files.
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                [pscustomobject]@{ path = $path; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
            }
        }
    }
    $started = [DateTimeOffset]::UtcNow.ToString('o')
    $head = (Read-Git @('rev-parse', 'HEAD')) -join ''
    $status = @(Read-Git @('status', '--porcelain=v1', '--untracked-files=all'))
    $inventory = @(Get-SourceInventory)
    $second = @(Get-SourceInventory)
    $endHead = (Read-Git @('rev-parse', 'HEAD')) -join ''
    $endStatus = @(Read-Git @('status', '--porcelain=v1', '--untracked-files=all'))
    $stable = ($head -eq $endHead) -and (($status -join "`n") -eq ($endStatus -join "`n")) -and
        (($inventory | ConvertTo-Json -Depth 4 -Compress) -eq ($second | ConvertTo-Json -Depth 4 -Compress))
    $dirty = $status.Count -gt 0
    $scope = if ($dirty) { 'working-copy-diagnostic' } else { 'clean-revision-provenance' }
    $output = Join-Path $repo ('artifacts/security/ithc-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
    # Do not accidentally add evidence to the source set on a future run.
    & git check-ignore --quiet -- ($output + '/manifest.json')
    if ($LASTEXITCODE -ne 0) { throw 'Evidence output must be ignored by Git.' }
    New-Item -ItemType Directory -Path $output | Out-Null
    $inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'source-hashes.json') -Encoding utf8
    $manifest = [ordered]@{
        schemaVersion = 1; startedUtc = $started; finishedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        commit = $head; branch = (Read-Git @('branch', '--show-current')) -join ''
        dirty = $dirty; stableDuringCollection = $stable; scope = $scope
        sourceFileCount = $inventory.Count; gitStatus = $status
        sourceInventorySha256 = (Get-FileHash -LiteralPath (Join-Path $output 'source-hashes.json') -Algorithm SHA256).Hash
        checksExecuted = @(); releaseApproved = $false
        limitations = 'Hashes are not signatures. No build, test, scan or deployment validation is performed. Concurrent editing can invalidate evidence; use a frozen checkout. No environment variables or source contents collected.'
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding utf8
    Write-Output "Evidence: $output ($scope; $($inventory.Count) files; stable=$stable)"
    if (-not $stable) { throw 'Source changed during collection. Freeze the checkout and collect again.' }
    if ($RequireClean -and $dirty) { throw 'Release evidence rejected: working tree is dirty. Diagnostic manifest retained.' }
}
finally { Pop-Location }
