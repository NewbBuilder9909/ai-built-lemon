param(
    [string]$SqlConnection = $env:PP_TEST_SQL_CONNECTION,
    [switch]$FocusedOnly
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($SqlConnection)) {
    throw 'Set PP_TEST_SQL_CONNECTION to a disposable database ending in _IntegrationTests.'
}
$connection = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $SqlConnection
if (-not $connection.InitialCatalog.EndsWith('_IntegrationTests', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to run migrations or destructive test scenarios against a non-test database.'
}
$previous = @{}
foreach ($name in @('PP_TEST_SQL_CONNECTION', 'PP_REQUIRE_SQL_TESTS', 'PP_SKIP_SQL_TESTS')) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    $env:PP_TEST_SQL_CONNECTION = $SqlConnection
    $env:PP_REQUIRE_SQL_TESTS = '1'
    Remove-Item Env:PP_SKIP_SQL_TESTS -ErrorAction SilentlyContinue
    $output = Join-Path $root 'artifacts/executive-data-lifecycle'
    $arguments = @('test', (Join-Path $root 'ProgrammePulse.Tests/ProgrammePulse.Tests.csproj'),
        '--configuration', 'Release', '--artifacts-path', (Join-Path $output 'build'),
        '--results-directory', (Join-Path $output 'results'), '--collect', 'XPlat Code Coverage')
    if ($FocusedOnly) {
        $arguments += @('--filter', 'FullyQualifiedName~Executive', '--logger', 'trx;LogFileName=executive-focused.trx')
    } else {
        $arguments += @('--logger', 'trx;LogFileName=full-suite.trx')
    }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Executive data lifecycle test gate failed.' }
    & dotnet build (Join-Path $root 'ProgrammePulse.csproj') --configuration Release `
        --artifacts-path (Join-Path $output 'razor') -p:CheckRazorViews=true
    if ($LASTEXITCODE -ne 0) { throw 'Razor compilation failed.' }
} finally {
    foreach ($name in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}
