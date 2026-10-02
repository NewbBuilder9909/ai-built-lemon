<#
.SYNOPSIS
    Drops finished per-stream test databases from LocalDB.

.DESCRIPTION
    Dry run by default: it prints what it would do and changes nothing. Pass
    -Apply to do it.

    Rule 2 of docs/parallel-agent-working.md gives every stream its own test
    database, and the test factory creates whatever PP_TEST_SQL_CONNECTION
    names. Nothing drops them afterwards, so on 30 September 21 had built up.
    This script is the other half of that rule.

    A database is dropped only if its name looks like one of ours
    (UmbracoBase_*, PP_IT_*, ProgrammePulse_*, *_IntegrationTests) and it is
    not protected. Protected are:

      - UmbracoBase, the development database;
      - UmbracoBase_Demo, the Northstar demo's default;
      - UmbracoBase_IntegrationTests, the test factory's default;
      - whatever PP_TEST_SQL_CONNECTION and PP_DEMO_SQL_CONNECTION name in
        this shell, so the stream you are in keeps its database;
      - anything named in -Keep.

    Any other database is listed and left alone, so an unfamiliar one is
    never dropped.

    It refuses to run against anything but a LocalDB instance. A shared or
    production server is never a target.

.PARAMETER Keep
    Databases to keep anyway, e.g. a stream that has not merged yet.

.PARAMETER Apply
    Drop the databases. Without it, nothing is changed.

.EXAMPLE
    ./scripts/prune-test-databases.ps1
    ./scripts/prune-test-databases.ps1 -Keep ProgrammePulse_SkillsAi_20260926 -Apply
#>
[CmdletBinding()]
param(
    [string[]] $Keep = @(),
    [string] $LocalDbInstance = '(localdb)\GardnerDB',
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# `pwsh -File` passes "a,b" as one string rather than an array; accept both.
function Split-List([string[]] $values) {
    @($values | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Get-DatabaseName([string] $connectionString) {
    if (-not $connectionString) { return $null }
    $match = [regex]::Match($connectionString, '(?i)(?:^|;)\s*(?:Database|Initial Catalog)\s*=\s*([^;]+)')
    if ($match.Success) { $match.Groups[1].Value.Trim() } else { $null }
}

if ($LocalDbInstance -notmatch '^\(localdb\)\\') {
    throw "Refusing to run against '$LocalDbInstance': this script only prunes LocalDB."
}

$protected = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($name in @('UmbracoBase', 'UmbracoBase_Demo', 'UmbracoBase_IntegrationTests') + (Split-List $Keep)) {
    [void] $protected.Add($name)
}
foreach ($variable in @('PP_TEST_SQL_CONNECTION', 'PP_DEMO_SQL_CONNECTION')) {
    $name = Get-DatabaseName ([Environment]::GetEnvironmentVariable($variable))
    if ($name) { [void] $protected.Add($name) }
}

$ours = '^(UmbracoBase_|PP_IT_|ProgrammePulse_)|_IntegrationTests$'

$connection = New-Object System.Data.SqlClient.SqlConnection "Server=$LocalDbInstance;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5;"
$connection.Open()
try {
    $command = $connection.CreateCommand()
    $command.CommandText = 'SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name'
    $reader = $command.ExecuteReader()
    $databases = @(while ($reader.Read()) { $reader.GetString(0) })
    $reader.Close()

    $drop = @($databases | Where-Object { -not $protected.Contains($_) -and $_ -match $ours })
    $kept = @($databases | Where-Object { $protected.Contains($_) })
    $unfamiliar = @($databases | Where-Object { -not $protected.Contains($_) -and $_ -notmatch $ours })

    foreach ($name in $kept) { Write-Host "keep      $name" }
    foreach ($name in $unfamiliar) { Write-Host "leave     $name (not a test database name; drop it by hand if it is finished)" }
    foreach ($name in $drop) { Write-Host "$(if ($Apply) { 'drop' } else { 'would drop' })  $name" }

    if (-not $drop) {
        Write-Host 'Nothing to drop.'
    } elseif (-not $Apply) {
        Write-Host "`nDry run: nothing changed. Re-run with -Apply to drop $($drop.Count) database(s)."
    } else {
        foreach ($name in $drop) {
            $quoted = '[' + $name.Replace(']', ']]') + ']'
            $command.CommandText = "ALTER DATABASE $quoted SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE $quoted;"
            [void] $command.ExecuteNonQuery()
        }
        Write-Host "`nDropped $($drop.Count) database(s)."
    }
} finally {
    $connection.Close()
}
