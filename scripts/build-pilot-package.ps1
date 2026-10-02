param(
    [ValidateSet('VendorPilot','CustomerControlled')]
    [string]$Mode = 'VendorPilot',
    [string]$OutputDirectory = 'artifacts/pilot-package'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = [System.IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
if (-not $output.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be inside artifacts/.'
}

dotnet publish (Join-Path $root 'ProgrammePulse.csproj') -c Release -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$sha = (Get-FileHash (Join-Path $output 'ProgrammePulse.dll') -Algorithm SHA256).Hash
$commit = (git -C $root rev-parse HEAD 2>$null)
$manifest = [ordered]@{
    product = 'ProgrammePulse'
    mode = $Mode
    createdUtc = (Get-Date).ToUniversalTime().ToString('o')
    gitCommit = $commit
    assemblySha256 = $sha
    status = 'Build artifact only; deployment and perimeter controls require separate verification.'
    requiredChecks = @(
        'Dedicated SQL database and storage scope; verify tenant isolation and backup restore.'
        'Persistent encrypted ASP.NET Data Protection key ring and certificate rotation.'
        'Private Umbraco backoffice, HTTPS edge, MFA, audit and access review.'
        'Explicit tenant-owned source connections; no shared credentials outside Development.'
        'Document outbound source API, logging, support and update paths before any zero-egress claim.'
    )
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'pilot-manifest.json') -Encoding utf8
Write-Output "Published $Mode package to $output"
