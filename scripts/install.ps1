# Builds, installs (or updates) and launches Hotline. Run with Windows PowerShell 5.1.
#   -Configuration Debug   install the debug-logging build instead of release
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$msix = & (Join-Path $PSScriptRoot 'build-msix.ps1') -Configuration $Configuration | Select-Object -Last 1
Get-Process Hotline -ErrorAction SilentlyContinue | Stop-Process -Force
Add-AppxPackage -Path $msix -ForceApplicationShutdown -ForceUpdateFromAnyVersion
$pfn = (Get-AppxPackage pmarc14.Hotline).PackageFamilyName
Write-Host "Installed $pfn ($Configuration) from $msix"
Start-Process "shell:AppsFolder\$pfn!App"
