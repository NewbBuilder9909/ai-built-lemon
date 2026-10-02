param([string]$OutputPath = 'artifacts/security/nuget-vulnerabilities.json')

$ErrorActionPreference = 'Stop'
$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
$auditOutput = & dotnet list package --vulnerable --include-transitive --format json --output-version 1 2>&1
$auditExitCode = $LASTEXITCODE
$auditOutput | Set-Content -LiteralPath $OutputPath -Encoding utf8
if ($auditExitCode -ne 0) { throw "NuGet audit failed (exit $auditExitCode). See $OutputPath." }
$report = ($auditOutput -join "`n") | ConvertFrom-Json
if ($report.version -ne 1 -or @($report.projects).Count -lt 2) {
    throw 'NuGet audit returned incomplete evidence. Run from the solution root.'
}
if (@($report.logs | Where-Object { $_.level -in @('error', 'warning') }).Count -gt 0) {
    throw "NuGet reported audit diagnostics; review $OutputPath before accepting this evidence."
}
$vulnerable = @($report.projects | ForEach-Object {
    foreach ($framework in $_.frameworks) {
        @($framework.topLevelPackages) + @($framework.transitivePackages) |
            Where-Object { $_ -and $_.vulnerabilities -and @($_.vulnerabilities).Count -gt 0 }
    }
})
if ($vulnerable.Count -gt 0) { throw "Vulnerable dependencies found. See $OutputPath." }
Write-Output "NuGet audit passed; evidence: $OutputPath"
