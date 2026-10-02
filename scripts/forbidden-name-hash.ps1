<#
.SYNOPSIS
    Prints the hash to add to ForbiddenNames (ProgrammePulse.Tests/Architecture/ForbiddenNamesTests.cs)
    so a name can be kept out of the repository without writing it into the repository.
.EXAMPLE
    ./scripts/forbidden-name-hash.ps1 -Name "Some Company"
.NOTES
    The name is lower-cased and reduced to its words, as the test does. A hash of
    a well-known name can be guessed, so it keeps the name out of plain sight, not
    secret. For names that must not appear even as a hash, list them in
    ~/.programmepulse/forbidden-names.txt instead; the tests read that file too.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Name)

$words = [regex]::Matches($Name, '[\p{L}\p{N}]+') | ForEach-Object { $_.Value.ToLowerInvariant() }
$normalised = $words -join ' '
$bytes = [System.Text.Encoding]::UTF8.GetBytes($normalised)
$hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
($hash | ForEach-Object { $_.ToString('x2') }) -join ''
