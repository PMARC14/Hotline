# Builds, installs (or updates) and launches Hotline. Run with Windows PowerShell 5.1.
$ErrorActionPreference = 'Stop'
$msix = & (Join-Path $PSScriptRoot 'build-msix.ps1') | Select-Object -Last 1
Get-Process Hotline -ErrorAction SilentlyContinue | Stop-Process -Force
Add-AppxPackage -Path $msix -ForceApplicationShutdown -ForceUpdateFromAnyVersion
$pfn = (Get-AppxPackage pmarc14.Hotline).PackageFamilyName
Write-Host "Installed $pfn from $msix"
Start-Process "shell:AppsFolder\$pfn!App"
