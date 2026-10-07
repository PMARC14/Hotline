# Applies .github/rulesets/*.json to the GitHub repository (creates each ruleset, or updates the one with that name).
# Rulesets on a private repository need GitHub Pro; on a public one they're free. Needs the GitHub CLI (gh auth login).
#   powershell -File scripts\apply-rulesets.ps1 [-Repository PMARC14/hotline]
param([string]$Repository = 'PMARC14/hotline')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$existing = gh api "repos/$Repository/rulesets" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Could not read rulesets (a private repository needs GitHub Pro, or make it public first).' }
foreach ($file in Get-ChildItem (Join-Path $root '.github\rulesets') -Filter *.json) {
    $name = (Get-Content $file.FullName -Raw | ConvertFrom-Json).name
    $match = $existing | Where-Object { $_.name -eq $name } | Select-Object -First 1
    if ($match) { gh api -X PUT "repos/$Repository/rulesets/$($match.id)" --input $file.FullName | Out-Null; "updated ruleset '$name'" }
    else { gh api -X POST "repos/$Repository/rulesets" --input $file.FullName | Out-Null; "created ruleset '$name'" }
    if ($LASTEXITCODE -ne 0) { throw "Applying $($file.Name) failed" }
}
