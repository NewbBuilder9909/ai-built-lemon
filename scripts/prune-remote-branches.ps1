<#
.SYNOPSIS
    Deletes finished branches from the remote, archiving any unmerged tip as
    a tag first so no commit is ever lost.

.DESCRIPTION
    Dry run by default: it prints what it would do and changes nothing. Pass
    -Apply to do it.

    A branch whose tip is already in main is deleted outright, because
    nothing on it is lost.

    A branch whose changes main already has, though not its commits, is
    archived and deleted without being named. That is a squash-merged pull
    request, a cherry-pick, or a fix that reached main another way. The test
    is that merging it into main would change nothing (git merge-tree, git
    2.38 or later; with an older git these are simply left alone).

    Any other branch that is NOT in main is touched only if you name it in
    -Archive. An archived branch's tip is first pushed as the annotated tag
    archive/<branch>, and the branch is deleted only after that push
    succeeds. To restore one:

        git push origin 'archive/<branch>^{commit}:refs/heads/<branch>'

    Unmerged branches you don't name are listed and left alone, so a typo or
    an unfamiliar branch never loses work.

    GitHub's "Automatically delete head branches" setting removes merged pull
    request branches as they merge. This script is for the rest: branches
    pushed without a pull request, and superseded ones.

.PARAMETER Archive
    Unmerged branches to archive as tags and then delete.

.PARAMETER Keep
    Merged branches to keep anyway. main is always kept.

.PARAMETER Apply
    Make the changes. Without it, nothing is pushed, tagged or deleted.

.EXAMPLE
    ./scripts/prune-remote-branches.ps1
    ./scripts/prune-remote-branches.ps1 -Archive claude/gifted-bohr-k3gutm -Apply
#>
[CmdletBinding()]
param(
    [string[]] $Archive = @(),
    [string[]] $Keep = @(),
    [string] $Remote = 'origin',
    [string] $Base = 'main',
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# `pwsh -File` passes "a,b" as one string rather than an array; accept both.
# The @() at the call site matters: a function's empty result arrives as
# $null, and piping $null runs the pipeline once, which read as one branch
# with a blank name ("Not a deletable branch on origin: ").
function Split-List([string[]] $values) {
    @($values | Where-Object { $_ } | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}
[string[]] $Archive = @(Split-List $Archive)
[string[]] $Keep = @(Split-List $Keep)

# git writes progress to stderr even on success; Windows PowerShell turns a
# redirected stderr line into a terminating error under 'Stop', so judge by
# exit code instead.
function Invoke-Git {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & git @args 2>&1 } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed:`n$($output -join "`n")" }
    $output | ForEach-Object { "$_" }
}

Invoke-Git fetch $Remote --prune --quiet | Out-Null

$baseRef = "$Remote/$Base"
$protected = @($Base) + $Keep
$prefix = '^' + [regex]::Escape("$Remote/")

# refs/remotes/<remote>/HEAD shortens to the bare remote name; skip it.
$branches = @(Invoke-Git for-each-ref '--format=%(refname:short)' "refs/remotes/$Remote" |
    Where-Object { $_ -match $prefix } |
    ForEach-Object { $_ -replace $prefix, '' } |
    Where-Object { $protected -notcontains $_ })

$missing = @($Archive | Where-Object { $branches -notcontains $_ })
if ($missing.Count -gt 0) {
    throw "Not a deletable branch on ${Remote}: $($missing -join ', ')"
}

# True when merging the branch into the base would leave the base's tree
# unchanged. Exit code 1 is a conflict; anything else is an error, including
# a git too old to know --write-tree. Neither counts as "already in main".
$baseTree = Invoke-Git rev-parse "$baseRef^{tree}"
function Test-ChangesInBase([string] $ref) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & git merge-tree --write-tree $baseRef $ref 2>$null } finally { $ErrorActionPreference = $previous }
    ($LASTEXITCODE -eq 0) -and (@($output)[0] -eq $baseTree)
}

$merged = @()
$changesMerged = @()
$toArchive = @()
$leftAlone = @()
foreach ($branch in $branches) {
    & git merge-base --is-ancestor "$Remote/$branch" $baseRef
    switch ($LASTEXITCODE) {
        0 { $merged += $branch }
        1 {
            if ($Archive -contains $branch) { $toArchive += $branch }
            elseif (Test-ChangesInBase "$Remote/$branch") { $changesMerged += $branch }
            else { $leftAlone += $branch }
        }
        default { throw "git merge-base failed for $branch" }
    }
}

function Write-Section([string] $title, [string[]] $names) {
    Write-Host ""
    Write-Host "$title ($($names.Count))"
    foreach ($name in $names) {
        $ahead = Invoke-Git rev-list --count "$baseRef..$Remote/$name"
        $date = Invoke-Git log -1 --format=%cs "$Remote/$name"
        Write-Host ("  {0}  +{1,-3} {2}" -f $date, $ahead, $name)
    }
}

Write-Section "Merged into $Base - delete" $merged
Write-Section "Changes already in $Base (squash-merged or duplicated) - tag archive/<branch>, then delete" $changesMerged
Write-Section "Not in $Base, named in -Archive - tag archive/<branch>, then delete" $toArchive
Write-Section "Not in $Base - left alone" $leftAlone

if (-not $Apply) {
    Write-Host ""
    Write-Host "Dry run: nothing changed. Re-run with -Apply to make these changes."
    return
}

foreach ($branch in @($changesMerged) + @($toArchive)) {
    $tag = "archive/$branch"
    $tip = Invoke-Git rev-parse "$Remote/$branch"
    & git rev-parse -q --verify "refs/tags/$tag" | Out-Null
    if ($LASTEXITCODE -eq 0) {
        if ((Invoke-Git rev-parse "$tag^{commit}") -ne $tip) {
            throw "Tag $tag already exists on a different commit; resolve it by hand."
        }
    }
    else {
        Invoke-Git tag -a $tag $tip -m "Archived $(Get-Date -Format yyyy-MM-dd): tip of $branch before the branch was deleted." | Out-Null
    }
    Invoke-Git push $Remote "refs/tags/$tag" | Out-Null
    Write-Host "Archived $branch as $tag"
}

$toDelete = @($merged) + @($changesMerged) + @($toArchive)
if ($toDelete.Count -gt 0) {
    Invoke-Git push $Remote --delete @toDelete | Out-Null
    Write-Host "Deleted $($toDelete.Count) branch(es) from $Remote."
}
else {
    Write-Host "Nothing to delete."
}
