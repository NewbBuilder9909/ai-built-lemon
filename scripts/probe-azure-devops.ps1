<#
.SYNOPSIS
    Live acceptance probe for the Azure DevOps evidence connector.

.DESCRIPTION
    The connector (Services/Integrations/AzureDevOps) was built from the
    REST 7.1 reference and has never run against a real organisation. This
    script sends the same read-only requests the connector sends and checks
    each assumption it depends on, so the "not live-tested" gap in
    docs/azure-devops-evidence.md can be closed with evidence rather than
    reasoning.

    Read-only: GET requests only, to https://dev.azure.com/{organisation}.
    No application code, database or configuration is touched.

    Privacy: the results file records status codes, counts, field presence
    and pass/fail — never the token, commit messages, PR titles or personal
    email addresses. The only names recorded are those of identities that
    look like build/service accounts, so the bot rules can be checked.

    Checks (see "Live acceptance probe" in docs/azure-devops-evidence.md):
      C01 token accepted            C08 completed PRs readable
      C02 rejected token recognised C09 PR close-time filter honoured
      C03 unknown organisation      C10 PR paging ($skip) honoured
      C04 repository list shape     C11 identity shapes (commit vs PR)
      C05 repository lookup         C12 build/service identities
      C06 default-branch commits    C13 reviewer votes and containers
      C07 commit fromDate honoured  C14 commit paging ($skip) honoured

.PARAMETER Organisation
    The name after dev.azure.com/.

.PARAMETER Repository
    "Project/Repository" to probe. Defaults to the eligible repository with
    the most recent commit activity found first.

.PARAMETER WindowDays
    How far back the date-filter checks look. Choose a window that contains
    some, but not all, of the repository's commits and completed PRs.

.EXAMPLE
    $env:PP_ADO_PAT = '<Code (Read) token>'
    ./scripts/probe-azure-devops.ps1 -Organisation acme -Repository 'Web/portal'
    Remove-Item Env:PP_ADO_PAT
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Organisation,
    [string]$Repository,
    [ValidateRange(1, 3650)][int]$WindowDays = 30,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

if ($Organisation -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,48}[A-Za-z0-9])?$') {
    throw "'$Organisation' is not an organisation name the connector would accept."
}

$org = $Organisation.ToLowerInvariant()
$base = "https://dev.azure.com/$org"
$api = 'api-version=7.1'

# ---- token: environment first, otherwise a masked prompt. Never written anywhere. ----
$token = $env:PP_ADO_PAT
if ([string]::IsNullOrWhiteSpace($token)) {
    $secure = Read-Host -AsSecureString 'Personal access token (Code (Read) scope)'
    $token = [System.Net.NetworkCredential]::new('', $secure).Password
}
if ([string]::IsNullOrWhiteSpace($token)) { throw 'No token supplied.' }

# Same transport rules as the connector: no redirects, no cookies.
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$http = [System.Net.Http.HttpClient]::new($handler)
$http.Timeout = [TimeSpan]::FromSeconds(30)

function Invoke-Ado([string]$Url, [string]$Pat) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $Url)
    $basic = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(":$Pat"))
    $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Basic', $basic)
    $request.Headers.Accept.ParseAdd('application/json')
    $request.Headers.UserAgent.ParseAdd('ProgrammePulse-Evidence-Probe')

    $response = $http.SendAsync($request).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $mediaType = $null
    if ($response.Content.Headers.ContentType) { $mediaType = $response.Content.Headers.ContentType.MediaType }
    $location = $null
    if ($response.Headers.Location) { $location = $response.Headers.Location.GetLeftPart([UriPartial]::Path) }

    $json = $null
    if ($mediaType -and $mediaType -like '*json*' -and $body) {
        try { $json = $body | ConvertFrom-Json } catch { $json = $null }
    }

    [pscustomobject]@{
        Status      = [int]$response.StatusCode
        MediaType   = $mediaType
        Location    = $location
        Json        = $json
        BodyPreview = if ($json) { $null } else { ($body -replace '\s+', ' ').Substring(0, [Math]::Min(120, $body.Length)) }
    }
}

# What the connector's SendAsync would conclude from a response.
function Get-ClientVerdict($r) {
    if ($r.Status -eq 401 -or $r.Status -eq 203 -or ($r.Status -ge 300 -and $r.Status -lt 400)) { return 'TokenRejected' }
    if ($r.Status -eq 404 -or $r.Status -eq 403) { return 'NotFound' }
    if ($r.Status -ge 400) { return 'TransportError' }
    if (-not $r.MediaType -or $r.MediaType -notlike '*json*') { return 'TokenRejected' }
    return 'Ok'
}

function Esc([string]$s) { [Uri]::EscapeDataString($s) }
function Iso([datetime]$d) { $d.ToUniversalTime().ToString('o', [Globalization.CultureInfo]::InvariantCulture) }
function AsUtc($s) { if ($s) { [datetime]::Parse($s, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]'AdjustToUniversal, AssumeUniversal') } else { $null } }

$checks = [System.Collections.Generic.List[object]]::new()
function Add-Check([string]$Id, [string]$Assumption, [string]$Result, $Evidence) {
    $checks.Add([pscustomobject]@{ Id = $Id; Assumption = $Assumption; Result = $Result; Evidence = $Evidence })
    $colour = @{ PASS = 'Green'; FAIL = 'Red'; CHECK = 'Yellow'; SKIP = 'DarkGray' }[$Result]
    Write-Host ("{0} {1,-5} {2}" -f $Id, $Result, $Assumption) -ForegroundColor $colour
}

$servicePattern = '(?i)(build service|^build\\|azure pipelines|project collection|microsoft\.visualstudio\.services|^vstfs:)'
$serviceNames = [System.Collections.Generic.HashSet[string]]::new()
function Note-Service($identity) {
    if (-not $identity) { return }
    foreach ($n in @($identity.displayName, $identity.uniqueName, $identity.name)) {
        if ($n -and $n -match $servicePattern) { [void]$serviceNames.Add([string]$n) }
    }
}

Write-Host "Probing $base (read-only)`n"

# ---- C01-C03: token and organisation ----
$projects = Invoke-Ado "$base/_apis/projects?`$top=1&$api" $token
$verdict = Get-ClientVerdict $projects
Add-Check 'C01' 'A valid token reads the organisation' ($(if ($verdict -eq 'Ok') { 'PASS' } else { 'FAIL' })) @{ status = $projects.Status; verdict = $verdict }
if ($verdict -ne 'Ok') {
    Write-Host "`nThe token was not accepted ($verdict, HTTP $($projects.Status)). Nothing further can be checked." -ForegroundColor Red
}

$bogus = 'probe-invalid-' + [guid]::NewGuid().ToString('N')
$rejected = Invoke-Ado "$base/_apis/projects?`$top=1&$api" $bogus
$rejectedVerdict = Get-ClientVerdict $rejected
Add-Check 'C02' 'A refused token is recognised as refused, never as an empty result' `
    ($(if ($rejectedVerdict -eq 'TokenRejected') { 'PASS' } elseif ($rejectedVerdict -eq 'Ok') { 'FAIL' } else { 'CHECK' })) `
    @{ status = $rejected.Status; mediaType = $rejected.MediaType; redirectTo = $rejected.Location; verdict = $rejectedVerdict }

$unknownOrg = 'pp-probe-' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$unknown = Invoke-Ado "https://dev.azure.com/$unknownOrg/_apis/projects?`$top=1&$api" $token
$unknownVerdict = Get-ClientVerdict $unknown
Add-Check 'C03' 'An unknown organisation reads as not found (else the connect message says "token refused")' `
    ($(if ($unknownVerdict -eq 'NotFound') { 'PASS' } else { 'CHECK' })) @{ status = $unknown.Status; verdict = $unknownVerdict }

if ($verdict -eq 'Ok') {
    # ---- C04: repository list ----
    $repos = Invoke-Ado "$base/_apis/git/repositories?$api" $token
    $list = @()
    if ((Get-ClientVerdict $repos) -eq 'Ok' -and $repos.Json.value) { $list = @($repos.Json.value) }
    $eligible = @($list | Where-Object { -not $_.isDisabled -and $_.defaultBranch -and $_.project.name })
    $shapeOk = $list.Count -gt 0 -and @($list | Where-Object { -not $_.id -or -not $_.name -or -not $_.project.name }).Count -eq 0
    Add-Check 'C04' 'The org-level repository list carries id, name, project.name, defaultBranch, isDisabled' `
        ($(if ($shapeOk) { 'PASS' } elseif ($list.Count -eq 0) { 'CHECK' } else { 'FAIL' })) `
        @{ status = $repos.Status; total = $list.Count; eligible = $eligible.Count
           disabled = @($list | Where-Object { $_.isDisabled }).Count
           empty = @($list | Where-Object { -not $_.defaultBranch }).Count
           isDisabledFieldPresent = @($list | Where-Object { $null -ne $_.PSObject.Properties['isDisabled'] }).Count }

    if (-not $Repository -and $eligible.Count -gt 0) { $Repository = "$($eligible[0].project.name)/$($eligible[0].name)" }

    if (-not $Repository) {
        Add-Check 'C05' 'Repository lookup by project and name' 'SKIP' 'no eligible repository'
    }
    else {
        $project, $repoName = $Repository.Split('/', 2)
        $repoBase = "$base/$(Esc $project)/_apis/git/repositories/$(Esc $repoName)"
        Write-Host "  using repository '$Repository'"

        # ---- C05: repository lookup ----
        $repo = Invoke-Ado "$repoBase`?$api" $token
        $branch = $null
        if ((Get-ClientVerdict $repo) -eq 'Ok' -and $repo.Json.defaultBranch) { $branch = $repo.Json.defaultBranch -replace '^refs/heads/', '' }
        Add-Check 'C05' 'Repository lookup by escaped project/name returns its default branch' `
            ($(if ($branch) { 'PASS' } else { 'FAIL' })) @{ status = $repo.Status; hasDefaultBranch = [bool]$branch }

        if ($branch) {
            $commitQuery = "searchCriteria.itemVersion.version=$(Esc $branch)&searchCriteria.itemVersion.versionType=branch"

            # ---- C06: commits ----
            $commits = Invoke-Ado "$repoBase/commits?$commitQuery&searchCriteria.`$top=100&$api" $token
            $all = @()
            if ((Get-ClientVerdict $commits) -eq 'Ok') { $all = @($commits.Json.value) }
            Add-Check 'C06' 'Default-branch commits are readable' ($(if ((Get-ClientVerdict $commits) -eq 'Ok') { 'PASS' } else { 'FAIL' })) `
                @{ status = $commits.Status; returned = $all.Count }

            # ---- C07: fromDate in the ISO form the client sends ----
            $from = (Get-Date).ToUniversalTime().AddDays(-$WindowDays)
            $windowed = Invoke-Ado "$repoBase/commits?$commitQuery&searchCriteria.fromDate=$(Esc (Iso $from))&searchCriteria.`$top=100&$api" $token
            $wv = Get-ClientVerdict $windowed
            if ($wv -ne 'Ok') {
                Add-Check 'C07' 'searchCriteria.fromDate accepts an ISO-8601 UTC timestamp' 'FAIL' @{ status = $windowed.Status; body = $windowed.BodyPreview }
            }
            else {
                $w = @($windowed.Json.value)
                $olderByCommitter = @($w | Where-Object { (AsUtc $_.committer.date) -lt $from.AddMinutes(-1) }).Count
                $olderByAuthor = @($w | Where-Object { (AsUtc $_.author.date) -lt $from.AddMinutes(-1) }).Count
                $olderInFull = @($all | Where-Object { (AsUtc $_.committer.date) -lt $from }).Count
                $result = 'PASS'
                if ($olderByCommitter -gt 0) { $result = 'FAIL' }
                elseif ($olderInFull -eq 0) { $result = 'CHECK' }   # window excluded nothing, so it proves nothing
                Add-Check 'C07' 'searchCriteria.fromDate (ISO) filters by committer date' $result `
                    @{ windowDays = $WindowDays; returned = $w.Count; olderThanWindowByCommitterDate = $olderByCommitter
                       olderThanWindowByAuthorDate = $olderByAuthor; olderCommitsExistOutsideWindow = $olderInFull }
            }

            # ---- C14: commit paging ----
            $c0 = Invoke-Ado "$repoBase/commits?$commitQuery&searchCriteria.`$top=1&searchCriteria.`$skip=0&$api" $token
            $c1 = Invoke-Ado "$repoBase/commits?$commitQuery&searchCriteria.`$top=1&searchCriteria.`$skip=1&$api" $token
            if ($all.Count -lt 2) { Add-Check 'C14' 'Commit paging with searchCriteria.$skip' 'SKIP' 'fewer than two commits' }
            else {
                $moved = @($c0.Json.value)[0].commitId -ne @($c1.Json.value)[0].commitId
                Add-Check 'C14' 'Commit paging with searchCriteria.$top/$skip moves forward' ($(if ($moved) { 'PASS' } else { 'FAIL' })) @{ distinct = $moved }
            }

            # ---- C11 part 1 / C12: commit identities ----
            $commitAuthorHasId = @($all | Where-Object { $_.author.PSObject.Properties['id'] }).Count
            foreach ($c in $all) { Note-Service $c.author; Note-Service $c.committer }
        }

        # ---- C08: completed pull requests ----
        $prs = Invoke-Ado "$repoBase/pullrequests?searchCriteria.status=completed&`$top=100&$api" $token
        $prList = @()
        if ((Get-ClientVerdict $prs) -eq 'Ok') { $prList = @($prs.Json.value) }
        Add-Check 'C08' 'Completed pull requests are readable, with closedDate' `
            ($(if ((Get-ClientVerdict $prs) -ne 'Ok') { 'FAIL' } elseif ($prList.Count -eq 0) { 'CHECK' } elseif (@($prList | Where-Object { -not $_.closedDate }).Count -eq 0) { 'PASS' } else { 'FAIL' })) `
            @{ status = $prs.Status; returned = $prList.Count }

        # ---- C09: close-time window ----
        $minTime = (Get-Date).ToUniversalTime().AddDays(-$WindowDays)
        $prWindow = Invoke-Ado "$repoBase/pullrequests?searchCriteria.status=completed&`$top=100&searchCriteria.queryTimeRangeType=closed&searchCriteria.minTime=$(Esc (Iso $minTime))&$api" $token
        $pv = Get-ClientVerdict $prWindow
        if ($pv -ne 'Ok') {
            # Serious: the client would record every resumed run as partial.
            Add-Check 'C09' 'queryTimeRangeType=closed + minTime is accepted and filters by close time' 'FAIL' @{ status = $prWindow.Status; body = $prWindow.BodyPreview }
        }
        else {
            $pw = @($prWindow.Json.value)
            $olderClosed = @($pw | Where-Object { (AsUtc $_.closedDate) -lt $minTime.AddMinutes(-1) }).Count
            $olderExist = @($prList | Where-Object { (AsUtc $_.closedDate) -lt $minTime }).Count
            $result = 'PASS'
            if ($olderClosed -gt 0) { $result = 'CHECK' }       # ignored: client-side filter still protects, but over-reads
            elseif ($olderExist -eq 0) { $result = 'CHECK' }    # window excluded nothing, so it proves nothing
            Add-Check 'C09' 'queryTimeRangeType=closed + minTime is accepted and filters by close time' $result `
                @{ windowDays = $WindowDays; returned = $pw.Count; olderThanWindowReturned = $olderClosed; olderPullRequestsExist = $olderExist }
        }

        # ---- C10: PR paging ----
        if ($prList.Count -lt 2) { Add-Check 'C10' 'Pull-request paging with $skip' 'SKIP' 'fewer than two completed pull requests' }
        else {
            $p0 = Invoke-Ado "$repoBase/pullrequests?searchCriteria.status=completed&`$top=1&`$skip=0&$api" $token
            $p1 = Invoke-Ado "$repoBase/pullrequests?searchCriteria.status=completed&`$top=1&`$skip=1&$api" $token
            $moved = @($p0.Json.value)[0].pullRequestId -ne @($p1.Json.value)[0].pullRequestId
            Add-Check 'C10' 'Pull-request paging with $top/$skip moves forward' ($(if ($moved) { 'PASS' } else { 'FAIL' })) @{ distinct = $moved }
        }

        # ---- C11: identity shapes ----
        $reviewers = @($prList | ForEach-Object { @($_.reviewers) } | Where-Object { $_ })
        $creators = @($prList | ForEach-Object { $_.createdBy } | Where-Object { $_ })
        foreach ($i in $creators + $reviewers) { Note-Service $i }
        $creatorWithId = @($creators | Where-Object { $_.id }).Count
        $creatorEmailLike = @($creators | Where-Object { $_.uniqueName -and $_.uniqueName -like '*@*' -and $_.uniqueName -notlike '*\*' }).Count
        $identityOk = ($creators.Count -eq 0 -or $creatorWithId -eq $creators.Count) -and (-not $all -or $commitAuthorHasId -eq 0)
        Add-Check 'C11' 'Commit actors carry no account id; PR actors carry a stable id and a uniqueName' `
            ($(if ($creators.Count -eq 0 -and -not $all) { 'SKIP' } elseif ($identityOk) { 'PASS' } else { 'CHECK' })) `
            @{ commitsWithAuthorId = $commitAuthorHasId; prCreators = $creators.Count; prCreatorsWithId = $creatorWithId
               prCreatorsWithEmailLikeUniqueName = $creatorEmailLike }

        # ---- C12: service identities (names only — they are accounts, not people) ----
        Add-Check 'C12' 'Build/service identities match the connector''s bot rules — review the names listed' `
            ($(if ($serviceNames.Count -gt 0) { 'CHECK' } else { 'SKIP' })) @{ serviceIdentityNames = @($serviceNames) }

        # ---- C13: votes ----
        $votes = @{}
        foreach ($r in $reviewers) { $k = [string]$r.vote; if (-not $votes.ContainsKey($k)) { $votes[$k] = 0 }; $votes[$k]++ }
        $containers = @($reviewers | Where-Object { $_.isContainer }).Count
        $known = @($reviewers | Where-Object { @(10, 5, 0, -5, -10) -notcontains [int]$_.vote }).Count -eq 0
        Add-Check 'C13' 'Reviewer votes use the documented scale; group reviewers are flagged isContainer' `
            ($(if ($reviewers.Count -eq 0) { 'SKIP' } elseif ($known) { 'PASS' } else { 'FAIL' })) `
            @{ reviewers = $reviewers.Count; voteDistribution = $votes; containerReviewers = $containers }
    }
}

$http.Dispose()
$token = $null

# ---- results ----
if (-not $OutputPath) {
    $root = Split-Path -Parent $PSScriptRoot
    $OutputPath = Join-Path $root ("artifacts/azure-devops-probe/probe-{0}-{1}.json" -f $org, (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'))
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
[pscustomobject]@{
    probedAtUtc  = (Get-Date).ToUniversalTime().ToString('o')
    organisation = $org
    repository   = $Repository
    windowDays   = $WindowDays
    checks       = $checks
} | ConvertTo-Json -Depth 6 | Out-File -FilePath $OutputPath -Encoding utf8

$fails = @($checks | Where-Object { $_.Result -eq 'FAIL' }).Count
$reviews = @($checks | Where-Object { $_.Result -eq 'CHECK' }).Count
Write-Host ("`n{0} checks: {1} fail, {2} to review. Results: {3}" -f $checks.Count, $fails, $reviews, $OutputPath)
if ($fails -gt 0) { exit 1 }
