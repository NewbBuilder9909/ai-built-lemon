param(
    [string]$OutputPath = 'artifacts/security/nuget-vulnerabilities.json',
    [string]$ExceptionsPath = 'scripts/nuget-audit-exceptions.json'
)

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
# A reviewed exception lets one advisory through for one exact package
# version until its expiry date, so a known risk that has a written record
# (and no fix yet upstream) doesn't fail every build, while anything new,
# a different version or a lapsed review still does. Each accepted advisory
# is printed as a warning on every run; it is never silent.
$today = [DateTime]::UtcNow.Date
$exceptions = @()
if (Test-Path -LiteralPath $ExceptionsPath) {
    $exceptions = @(Get-Content -Raw -LiteralPath $ExceptionsPath | ConvertFrom-Json)
}
function Get-Expiry($exception) {
    # ConvertFrom-Json may already have turned the date into a DateTime.
    if ($exception.expires -is [DateTime]) { return $exception.expires.Date }
    [DateTime]::ParseExact([string]$exception.expires, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
}

$accepted = [System.Collections.Generic.HashSet[string]]::new()
$unaccepted = [System.Collections.Generic.HashSet[string]]::new()
$matched = [System.Collections.Generic.HashSet[object]]::new()
foreach ($project in @($report.projects)) {
    foreach ($framework in @($project.frameworks)) {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            if (-not $package -or -not $package.vulnerabilities) { continue }
            foreach ($vulnerability in @($package.vulnerabilities)) {
                $label = "$($package.id) $($package.resolvedVersion) $($vulnerability.severity) $($vulnerability.advisoryurl)"
                $exception = $exceptions | Where-Object {
                    $_.package -eq $package.id -and $_.version -eq $package.resolvedVersion -and $_.advisory -eq $vulnerability.advisoryurl
                } | Select-Object -First 1
                if (-not $exception) { [void]$unaccepted.Add($label); continue }
                [void]$matched.Add($exception)
                $expires = Get-Expiry $exception
                if ($expires -lt $today) {
                    [void]$unaccepted.Add("$label (exception expired $($expires.ToString('yyyy-MM-dd')); review $($exception.record))")
                }
                else {
                    [void]$accepted.Add("$label accepted until $($expires.ToString('yyyy-MM-dd')), owner $($exception.owner): $($exception.record)")
                }
            }
        }
    }
}

$accepted | Sort-Object | ForEach-Object { Write-Output "::warning title=Accepted NuGet advisory::$_" }
$exceptions | Where-Object { -not $matched.Contains($_) } | ForEach-Object {
    Write-Output "::warning title=Unused NuGet audit exception::$($_.package) $($_.version) $($_.advisory) is no longer reported; remove it from $ExceptionsPath."
}
if ($unaccepted.Count -gt 0) {
    $unaccepted | Sort-Object | ForEach-Object { Write-Output "::error title=Vulnerable NuGet package::$_" }
    throw "Vulnerable dependencies found. See $OutputPath."
}
Write-Output "NuGet audit passed; evidence: $OutputPath"
