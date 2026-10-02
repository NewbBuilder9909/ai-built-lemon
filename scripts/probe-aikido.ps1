<#
.SYNOPSIS
    Live acceptance probe for the planned Aikido findings connector (step 3 of
    docs/delivery-evidence-and-contract-assurance.md).

.DESCRIPTION
    Step 3 will read security findings, each repository's PR-check
    configuration, and PR-check outcomes from Aikido's public API. This script
    checks every assumption that design depends on against a real workspace
    before any connector code is written, so the design follows the API that
    exists rather than the documentation's reading of it.

    Read-only: it obtains a client-credentials token and sends GET requests to
    the Aikido public API. No application code, database or configuration is
    touched, and nothing is written to Aikido.

    Privacy: the results file records status codes, counts, field presence and
    pass/fail, plus the workspace's own repository names and URLs, because
    matching them to ProgrammePulse's repository links is one of the things
    being checked. It never records the client secret, the access token,
    issue titles, file paths, PR titles or code.

    Rate limit: Aikido allows 20 calls per minute per workspace. The probe
    makes about 12 calls, spaces them out, and honours Retry-After on a 429.

    Checks:
      K01 token issued              K07 closed issues carry closed_at
      K02 repositories listed       K08 severity/status filters honoured
      K03 GitHub owner/name derivable K09 PR-check configuration readable
      K04 issues export readable    K10 blocking threshold expressible
      K05 issue carries repository  K11 PR-check runs readable
      K06 first_detected_at present K12 gate outcomes include bypassed
                                    K13 workspace default readable
                                    K14 PR-check run fields complete

.PARAMETER Region
    eu (default), us, au or me: the Aikido region your workspace lives in.

.EXAMPLE
    $env:PP_AIKIDO_CLIENT_ID = '<client id>'
    $env:PP_AIKIDO_CLIENT_SECRET = '<client secret>'
    ./scripts/probe-aikido.ps1 -Region eu
    Remove-Item Env:PP_AIKIDO_CLIENT_ID, Env:PP_AIKIDO_CLIENT_SECRET
#>
[CmdletBinding()]
param(
    [ValidateSet('eu', 'us', 'au', 'me')][string]$Region = 'eu',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
# Windows PowerShell 5.1 runs on .NET Framework, which may default to TLS 1.0/1.1.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$hostName = @{ eu = 'app.aikido.dev'; us = 'app.us.aikido.dev'; au = 'app.au.aikido.dev'; me = 'app.me.aikido.dev' }[$Region]
$api = "https://$hostName/api/public/v1"

# ---- credentials: environment first, otherwise masked prompts. Never written anywhere. ----
$clientId = $env:PP_AIKIDO_CLIENT_ID
if ([string]::IsNullOrWhiteSpace($clientId)) { $clientId = Read-Host 'Aikido client ID' }
$clientSecret = $env:PP_AIKIDO_CLIENT_SECRET
if ([string]::IsNullOrWhiteSpace($clientSecret)) {
    $secure = Read-Host -AsSecureString 'Aikido client secret'
    $clientSecret = [System.Net.NetworkCredential]::new('', $secure).Password
}
if ([string]::IsNullOrWhiteSpace($clientId) -or [string]::IsNullOrWhiteSpace($clientSecret)) { throw 'No client credentials supplied.' }

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$http = [System.Net.Http.HttpClient]::new($handler)
$http.Timeout = [TimeSpan]::FromSeconds(60)
$script:lastCall = [DateTime]::MinValue

function Wait-RateLimit {
    # 20 calls/minute: keep at least 3.2 s between calls.
    $gap = (Get-Date) - $script:lastCall
    if ($gap.TotalMilliseconds -lt 3200) { Start-Sleep -Milliseconds (3200 - [int]$gap.TotalMilliseconds) }
    $script:lastCall = Get-Date
}

function Send([System.Net.Http.HttpRequestMessage]$Request) {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Wait-RateLimit
        $clone = [System.Net.Http.HttpRequestMessage]::new($Request.Method, $Request.RequestUri)
        foreach ($h in $Request.Headers) { [void]$clone.Headers.TryAddWithoutValidation($h.Key, $h.Value) }
        if ($Request.Content) { $clone.Content = $Request.Content }
        $response = $http.SendAsync($clone).GetAwaiter().GetResult()
        if ([int]$response.StatusCode -ne 429) { return $response }
        $retry = 20
        if ($response.Headers.RetryAfter -and $response.Headers.RetryAfter.Delta) { $retry = [int]$response.Headers.RetryAfter.Delta.Value.TotalSeconds + 1 }
        Write-Host "  rate limited; waiting $retry s" -ForegroundColor DarkYellow
        Start-Sleep -Seconds $retry
    }
    return $response
}

function Get-Json([string]$Path, [string]$Token) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, "$api$Path")
    $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Token)
    $request.Headers.Accept.ParseAdd('application/json')
    $response = Send $request
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $json = $null
    try { if ($body) { $json = $body | ConvertFrom-Json } } catch { $json = $null }
    [pscustomobject]@{ Status = [int]$response.StatusCode; Json = $json }
}

# Aikido list endpoints return either a bare array or an object wrapping one.
function Get-Items($json) {
    if ($null -eq $json) { return @() }
    if ($json -is [array]) { return @($json) }
    foreach ($p in 'data', 'items', 'issues', 'results', 'repositories', 'checks', 'scans') {
        if ($json.PSObject.Properties[$p] -and $json.$p -is [array]) { return @($json.$p) }
    }
    return @($json)
}

$checks = [System.Collections.Generic.List[object]]::new()
function Add-Check([string]$Id, [string]$Assumption, [string]$Result, $Evidence) {
    $checks.Add([pscustomobject]@{ Id = $Id; Assumption = $Assumption; Result = $Result; Evidence = $Evidence })
    $colour = @{ PASS = 'Green'; FAIL = 'Red'; CHECK = 'Yellow'; SKIP = 'DarkGray' }[$Result]
    Write-Host ("{0} {1,-5} {2}" -f $Id, $Result, $Assumption) -ForegroundColor $colour
}

Write-Host "Probing $api (read-only)`n"

# ---- K01: token ----
$tokenRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "https://$hostName/api/oauth/token")
$basic = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$($clientId):$($clientSecret)"))
$tokenRequest.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Basic', $basic)
$form = [System.Collections.Generic.Dictionary[string, string]]::new()
$form.Add('grant_type', 'client_credentials')
$tokenRequest.Content = [System.Net.Http.FormUrlEncodedContent]::new($form)
$tokenResponse = Send $tokenRequest
$tokenBody = $tokenResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
$clientSecret = $null
$basic = $null
$token = $null
$expiresIn = $null
try { $parsed = $tokenBody | ConvertFrom-Json; $token = $parsed.access_token; $expiresIn = $parsed.expires_in } catch { }
Add-Check 'K01' 'Client credentials yield an access token' ($(if ($token) { 'PASS' } else { 'FAIL' })) `
    @{ status = [int]$tokenResponse.StatusCode; expiresInSeconds = $expiresIn }

if ($token) {
    # ---- K02-K03: repositories ----
    $repos = Get-Json '/repositories/code?per_page=200' $token
    $repoList = Get-Items $repos.Json
    Add-Check 'K02' 'Active code repositories are listed with id, provider, url' `
        ($(if ($repos.Status -eq 200 -and $repoList.Count -gt 0 -and @($repoList | Where-Object { -not $_.id -or -not $_.provider }).Count -eq 0) { 'PASS' } elseif ($repos.Status -eq 200) { 'CHECK' } else { 'FAIL' })) `
        @{ status = $repos.Status; repositories = $repoList.Count
           providers = @($repoList | ForEach-Object { $_.provider } | Sort-Object -Unique)
           connectivity = @($repoList | Group-Object connectivity | ForEach-Object { "$($_.Name)=$($_.Count)" }) }

    # ProgrammePulse links repositories as (GitHub, owner, owner/name); step 3
    # must derive that from Aikido's record without guessing.
    # Aikido returns GitHub's API form (https://api.github.com/repos/{owner}/{name});
    # the web form is accepted too. Anchored, so "repos" can never be read as an owner.
    $github = @($repoList | Where-Object { $_.provider -eq 'github' })
    $derived = @($github | ForEach-Object {
        $url = "$($_.url)"
        if ($url -match '^https://api\.github\.com/repos/([A-Za-z0-9._-]+)/([A-Za-z0-9._-]+?)/?$') { "$($Matches[1])/$($Matches[2])".ToLowerInvariant() }
        elseif ($url -match '^https://(?:www\.)?github\.com/([A-Za-z0-9._-]+)/([A-Za-z0-9._-]+?)(?:\.git)?/?$') { "$($Matches[1])/$($Matches[2])".ToLowerInvariant() }
    })
    # Two repositories deriving the same key would file one's findings under the other.
    $distinct = @($derived | Sort-Object -Unique).Count -eq $derived.Count
    # The shapes Aikido actually returns, so the join rule is written from fact.
    # These are the workspace's own repository identifiers, not secrets.
    $shapes = @($github | ForEach-Object { [pscustomobject]@{ name = $_.name; url = $_.url; external_repo_id = $_.external_repo_id; branch = $_.branch } })
    Add-Check 'K03' 'Each GitHub repository gives owner/name (from url, else name) for matching repository links' `
        ($(if ($github.Count -eq 0) { 'SKIP' } elseif ($derived.Count -eq $github.Count -and $distinct) { 'PASS' } else { 'FAIL' })) `
        @{ githubRepositories = $github.Count; derived = $derived.Count; distinct = $distinct; names = $derived; shapes = $shapes }

    # ---- K04-K08: issues export ----
    $all = Get-Json '/issues/export?format=json&filter_status=all&per_page=500' $token
    $issues = Get-Items $all.Json
    Add-Check 'K04' 'The issues export is readable as JSON' ($(if ($all.Status -eq 200) { 'PASS' } else { 'FAIL' })) `
        @{ status = $all.Status; returned = $issues.Count
           bySeverity = @($issues | Group-Object severity | ForEach-Object { "$($_.Name)=$($_.Count)" })
           byStatus = @($issues | Group-Object status | ForEach-Object { "$($_.Name)=$($_.Count)" })
           byType = @($issues | Group-Object type | ForEach-Object { "$($_.Name)=$($_.Count)" }) }

    $codeIssues = @($issues | Where-Object { $_.type -in 'open_source', 'sast', 'leaked_secret', 'iac', 'malware' })
    $withRepo = @($codeIssues | Where-Object { $_.code_repo_id })
    Add-Check 'K05' 'Code issues name their repository (code_repo_id), so they join to repository links' `
        ($(if ($codeIssues.Count -eq 0) { 'CHECK' } elseif ($withRepo.Count -eq $codeIssues.Count) { 'PASS' } else { 'CHECK' })) `
        @{ codeIssues = $codeIssues.Count; withCodeRepoId = $withRepo.Count }

    Add-Check 'K06' 'Every issue has first_detected_at (the "introduced while in scope" date)' `
        ($(if ($issues.Count -eq 0) { 'SKIP' } elseif (@($issues | Where-Object { -not $_.first_detected_at }).Count -eq 0) { 'PASS' } else { 'FAIL' })) `
        @{ missing = @($issues | Where-Object { -not $_.first_detected_at }).Count }

    $closed = @($issues | Where-Object { $_.status -eq 'closed' })
    Add-Check 'K07' 'Closed issues carry closed_at (for remediation deadlines)' `
        ($(if ($closed.Count -eq 0) { 'CHECK' } elseif (@($closed | Where-Object { -not $_.closed_at }).Count -eq 0) { 'PASS' } else { 'FAIL' })) `
        @{ closed = $closed.Count; closedWithoutClosedAt = @($closed | Where-Object { -not $_.closed_at }).Count }

    $filtered = Get-Json '/issues/export?format=json&filter_status=open&filter_severities=critical,high&per_page=500' $token
    $filteredIssues = Get-Items $filtered.Json
    $leaks = @($filteredIssues | Where-Object { $_.status -ne 'open' -or $_.severity -notin 'critical', 'high' }).Count
    $expected = @($issues | Where-Object { $_.status -eq 'open' -and $_.severity -in 'critical', 'high' }).Count
    Add-Check 'K08' 'Status and severity filters are honoured server-side' `
        ($(if ($filtered.Status -ne 200) { 'FAIL' } elseif ($leaks -gt 0) { 'FAIL' } elseif ($issues.Count -ge 500) { 'CHECK' } elseif ($filteredIssues.Count -eq $expected) { 'PASS' } else { 'CHECK' })) `
        @{ status = $filtered.Status; returned = $filteredIssues.Count; expectedFromFullExport = $expected; outsideFilter = $leaks }

    # ---- K09-K10: PR-check configuration ----
    $config = Get-Json '/repositories/code/continuous_integration/checks?per_page=100' $token
    $configs = Get-Items $config.Json
    Add-Check 'K09' 'PR-check configuration is readable per repository' `
        ($(if ($config.Status -ne 200) { 'FAIL' } elseif ($configs.Count -gt 0) { 'PASS' } else { 'CHECK' })) `
        @{ status = $config.Status; configurations = $configs.Count; repositoriesListed = $repoList.Count }

    # "Block High before merge" is met when new dependency/SAST/secret issues fail
    # the check at minimum_severity high or lower-rank. Count repos that would pass.
    $blocksHigh = @($configs | Where-Object {
        $_.minimum_severity -in 'low', 'medium', 'high' -and $_.fail_on_dependency_scan -and $_.fail_on_sast_scan -and $_.fail_on_secrets_scan })
    Add-Check 'K10' 'A "block High and Critical" obligation can be read from minimum_severity and fail_on_* flags' `
        ($(if ($configs.Count -eq 0) { 'SKIP' } elseif (@($configs | Where-Object { $null -eq $_.PSObject.Properties['minimum_severity'] }).Count -eq 0) { 'PASS' } else { 'FAIL' })) `
        @{ configurations = $configs.Count; wouldBlockHigh = $blocksHigh.Count
           thresholds = @($configs | Group-Object minimum_severity | ForEach-Object { "$($_.Name)=$($_.Count)" }) }

    # ---- K13: workspace default. It applies only to repositories activated
    # after it was set, so it cannot be assumed for existing repositories. ----
    $default = Get-Json '/repositories/code/continuous_integration/checks/default' $token
    $d = $default.Json
    Add-Check 'K13' 'The workspace default PR-check configuration is readable (applies to newly activated repositories only)' `
        ($(if ($default.Status -eq 200 -and $d -and $null -ne $d.PSObject.Properties['is_enabled']) { 'PASS' } elseif ($default.Status -eq 200) { 'CHECK' } else { 'FAIL' })) `
        @{ status = $default.Status; isEnabled = $d.is_enabled; minimumSeverity = $d.minimum_severity
           failOnDependency = $d.fail_on_dependency_scan; failOnSast = $d.fail_on_sast_scan; failOnSecrets = $d.fail_on_secrets_scan }

    # ---- K11-K12: PR-check runs ----
    $scans = Get-Json '/report/ciScans?per_page=50&filter_gate_status=all' $token
    $scanList = Get-Items $scans.Json
    Add-Check 'K11' 'PR-check runs are readable with gate_status, repository and commit' `
        ($(if ($scans.Status -ne 200) { 'FAIL' } elseif ($scanList.Count -eq 0) { 'CHECK' } elseif (@($scanList | Where-Object { -not $_.gate_status -or -not $_.code_repo_id }).Count -eq 0) { 'PASS' } else { 'FAIL' })) `
        @{ status = $scans.Status; returned = $scanList.Count
           outcomes = @($scanList | Group-Object gate_status | ForEach-Object { "$($_.Name)=$($_.Count)" }) }

    $bypassed = Get-Json '/report/ciScans?per_page=50&filter_gate_status=bypassed' $token
    Add-Check 'K12' 'Bypassed PR checks can be listed (the breach a "block" clause cares about)' `
        ($(if ($bypassed.Status -eq 200) { 'PASS' } else { 'FAIL' })) `
        @{ status = $bypassed.Status; bypassed = (Get-Items $bypassed.Json).Count }

    # ---- K14: every field the connector maps from a PR-check run. When this
    # passes on a real gated pull request, SecurityAssuranceSnapshot.CheckRunsVerified
    # can be switched on and the "Unverified" label removed. ----
    $mapped = 'scan_id', 'code_repo_id', 'gate_status', 'started_at'
    $missingFields = @($scanList | ForEach-Object { $run = $_; $mapped | Where-Object { $null -eq $run.PSObject.Properties[$_] } } | Sort-Object -Unique)
    $numericStart = @($scanList | Where-Object { "$($_.started_at)" -match '^\d+$' }).Count
    Add-Check 'K14' 'PR-check runs carry every mapped field (scan_id, code_repo_id, gate_status, started_at as unix seconds)' `
        ($(if ($scans.Status -ne 200) { 'FAIL' } elseif ($scanList.Count -eq 0) { 'CHECK' } elseif ($missingFields.Count -eq 0 -and $numericStart -eq $scanList.Count) { 'PASS' } else { 'FAIL' })) `
        @{ runs = $scanList.Count; missingFields = $missingFields; startedAtNumeric = $numericStart
           hasCommitSha = @($scanList | Where-Object { $_.related_commit_sha }).Count
           hasPullRequestUrl = @($scanList | Where-Object { $_.pull_request_url }).Count }

    # ---- What ProgrammePulse should show on /staffops/security after a sync.
    # Computed with the connector's rules, so a difference is a defect to report.
    # Repository names only; no titles, paths or code. ----
    $configByRepo = @{}
    foreach ($c in $configs) { if ($null -ne $c.code_repo_id) { $configByRepo["$($c.code_repo_id)"] = $c } }
    $expected = foreach ($r in $github) {
        $url = "$($r.url)"
        $key = $null
        if ($url -match '^https://api\.github\.com/repos/([A-Za-z0-9._-]+)/([A-Za-z0-9._-]+?)/?$') { $key = "$($Matches[1])/$($Matches[2])".ToLowerInvariant() }
        elseif ($url -match '^https://(?:www\.)?github\.com/([A-Za-z0-9._-]+)/([A-Za-z0-9._-]+?)(?:\.git)?/?$') { $key = "$($Matches[1])/$($Matches[2])".ToLowerInvariant() }
        if (-not $key) { continue }
        $c = $configByRepo["$($r.id)"]
        $gate = if ($null -eq $c -or $c.is_enabled -eq $false) { 'No pull-request gate configured' }
            elseif ($c.minimum_severity -notin 'low', 'medium', 'high', 'critical') { 'Gate configured, but it never fails' }
            else {
                $kinds = @(); if ($c.fail_on_dependency_scan) { $kinds += 'dependencies' }; if ($c.fail_on_sast_scan) { $kinds += 'code' }; if ($c.fail_on_secrets_scan) { $kinds += 'secrets' }
                $sev = (Get-Culture).TextInfo.ToTitleCase("$($c.minimum_severity)")
                if ($kinds.Count -eq 0) { "Gate at $sev and above, but no scan type fails it" } else { "Fails at $sev and above on $($kinds -join ', ')" }
            }
        $blocksHigh = $null -ne $c -and $c.is_enabled -ne $false -and $c.minimum_severity -in 'low', 'medium', 'high' -and $c.fail_on_dependency_scan -and $c.fail_on_sast_scan -and $c.fail_on_secrets_scan
        $mine = @($issues | Where-Object { "$($_.code_repo_id)" -eq "$($r.id)" -and $_.severity -in 'high', 'critical' -and $_.status -ne 'closed' })
        [pscustomobject]@{
            Repository  = "GitHub: $key"
            Gate        = $gate
            BlocksHigh  = if ($blocksHigh) { 'yes' } else { 'no' }
            HighCritNotClosed = $mine.Count
            Open        = @($mine | Where-Object { $_.status -eq 'open' }).Count
            Ignored     = @($mine | Where-Object { $_.status -eq 'ignored' }).Count
            Snoozed     = @($mine | Where-Object { $_.status -eq 'snoozed' }).Count
        }
    }
    Write-Host "`nExpected on /staffops/security after a sync (compare line by line):"
    $expected | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    if ($issues.Count -ge 500) { Write-Host 'Note: the issues export returned 500 rows, the probe page size; counts above may be low.' -ForegroundColor Yellow }
}

$http.Dispose()
$token = $null

# ---- results ----
if (-not $OutputPath) {
    $root = Split-Path -Parent $PSScriptRoot
    $OutputPath = Join-Path $root ("artifacts/aikido-probe/probe-{0}-{1}.json" -f $Region, (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'))
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
[pscustomobject]@{
    probedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    region      = $Region
    apiBase     = $api
    checks      = $checks
} | ConvertTo-Json -Depth 6 | Out-File -FilePath $OutputPath -Encoding utf8

$fails = @($checks | Where-Object { $_.Result -eq 'FAIL' }).Count
$reviews = @($checks | Where-Object { $_.Result -eq 'CHECK' }).Count
Write-Host ("`n{0} checks: {1} fail, {2} to review. Results: {3}" -f $checks.Count, $fails, $reviews, $OutputPath)
if ($fails -gt 0) { exit 1 }
