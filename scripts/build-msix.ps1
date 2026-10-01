# Builds and signs the Hotline MSIX. Last output line = path to the .msix.
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$pfx = Join-Path $root 'certs\hotline-dev.pfx'
if (-not (Test-Path $pfx)) { throw 'No dev cert. Run scripts\dev-cert.ps1 first.' }
$out = Join-Path $root 'artifacts\'

dotnet publish (Join-Path $root 'src\Hotline.App\Hotline.App.csproj') -c $Configuration -r win-x64 `
    -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true `
    -p:PackageCertificateKeyFile="$pfx" -p:PackageCertificatePassword="$CertificatePassword" `
    -p:AppxPackageDir="$out" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Get-ChildItem $out -Recurse -Filter *.msix | Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
