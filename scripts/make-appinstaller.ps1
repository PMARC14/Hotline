# Writes Hotline.appinstaller: opening it installs Hotline from the GitHub Release, and Windows then checks the
# "latest" copy of this file for updates (once a day, in the background, without blocking launch).
# Identity (Name, Publisher) comes from the manifest so it always matches the signed package.
#   scripts\make-appinstaller.ps1 -Version 1.2.3.0 -Tag v1.2.3 -MsixName Hotline_1.2.3.0_x64.msix -Out artifacts\Hotline.appinstaller
param(
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$Tag,
    [Parameter(Mandatory)] [string]$MsixName,
    [Parameter(Mandatory)] [string]$Out,
    [string]$Repository = 'PMARC14/hotline'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$manifest = Get-Content (Join-Path $root 'src\Hotline.App\Package.appxmanifest') -Raw
$identity = $manifest.Package.Identity
if (-not $identity.Name -or -not $identity.Publisher) { throw 'Package.appxmanifest has no Identity Name/Publisher' }
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw "Version '$Version' must look like 1.2.3.0" }

$esc = [Security.SecurityElement]
$self = "https://github.com/$Repository/releases/latest/download/Hotline.appinstaller"
$msix = "https://github.com/$Repository/releases/download/$Tag/$MsixName"
$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018" Version="$Version" Uri="$($esc::Escape($self))">
  <MainPackage Name="$($esc::Escape($identity.Name))" Publisher="$($esc::Escape($identity.Publisher))" Version="$Version"
               ProcessorArchitecture="x64" Uri="$($esc::Escape($msix))" />
  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="24" ShowPrompt="false" UpdateBlocksActivation="false" />
    <AutomaticBackgroundTask />
  </UpdateSettings>
</AppInstaller>
"@
New-Item -ItemType Directory -Force (Split-Path $Out -Parent) | Out-Null
[IO.File]::WriteAllText($Out, $xml, (New-Object Text.UTF8Encoding $false))
$Out
