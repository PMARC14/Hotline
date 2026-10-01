# Opt-in: makes Windows Error Reporting keep full crash dumps of Hotline.exe in
# %LOCALAPPDATA%\Hotline\CrashDumps (last 5). Needs admin (one UAC prompt). Use -Disable to undo.
param([switch]$Disable)
$ErrorActionPreference = 'Stop'
$key = 'HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Hotline.exe'
$dumps = Join-Path $env:LOCALAPPDATA 'Hotline\CrashDumps'

$command = if ($Disable) {
    "Remove-Item -Path '$key' -Recurse -ErrorAction SilentlyContinue"
} else {
    New-Item -ItemType Directory -Force $dumps | Out-Null
    "New-Item -Path '$key' -Force | Out-Null; " +
    "Set-ItemProperty -Path '$key' -Name DumpFolder -Value '$dumps' -Type ExpandString; " +
    "Set-ItemProperty -Path '$key' -Name DumpType -Value 2 -Type DWord; " +
    "Set-ItemProperty -Path '$key' -Name DumpCount -Value 5 -Type DWord"
}
Start-Process powershell -Verb RunAs -Wait -ArgumentList @('-NoProfile', '-Command', $command)
if ($Disable) { Write-Host 'Hotline crash dumps disabled.' } else { Write-Host "Hotline crash dumps will be written to $dumps" }
