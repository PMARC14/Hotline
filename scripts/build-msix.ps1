# Builds and signs the Hotline MSIX. Last output line = path to the .msix.
# Each dev build gets a unique version (0.1.<day-of-year>.<seconds-of-day/2>) so it can be installed
# over the previous one without uninstalling (which would wipe settings and the Copilot key choice).
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$pfx = Join-Path $root 'certs\hotline-dev.pfx'
if (-not (Test-Path $pfx)) { throw 'No dev cert. Run scripts\dev-cert.ps1 first.' }
$out = Join-Path $root 'artifacts\'
$manifest = Join-Path $root 'src\Hotline.App\Package.appxmanifest'

$now = Get-Date
$version = "0.1.$($now.DayOfYear).$([int]($now.TimeOfDay.TotalSeconds / 2))"
$original = [IO.File]::ReadAllText($manifest)
try {
    [IO.File]::WriteAllText($manifest, ($original -replace '(<Identity [^>]*Version=")[^"]+', "`${1}$version"))
    dotnet publish (Join-Path $root 'src\Hotline.App\Hotline.App.csproj') -c $Configuration -r win-x64 `
        -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true `
        -p:PackageCertificateKeyFile="$pfx" -p:PackageCertificatePassword="$CertificatePassword" `
        -p:AppxPackageDir="$out" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}
finally {
    [IO.File]::WriteAllText($manifest, $original)
}

Get-ChildItem $out -Recurse -Filter "*_$($version)_*.msix" | Select-Object -Last 1 -ExpandProperty FullName
