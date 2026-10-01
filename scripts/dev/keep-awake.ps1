# Dev helper: keeps this PC (and long-running terminal work) awake while you step away.
# Prefer "screen off, system on"? On Modern Standby laptops the reliable way is: plug in and set
# Power Options → "When I close the lid" → Do nothing (plugged in), then close the lid. This script is the
# lid-open fallback and keeps the screen ON (OLED: minimize windows / lower brightness).
# On Modern Standby laptops (S0 low power idle) Windows enters standby as soon as the display idles off,
# even with a "system required" request, and pauses desktop apps. So this holds BOTH system and display
# required (the screen stays on; turn brightness down if you like). Runs detached; auto-stops after -Hours.
#   powershell -File scripts\dev\keep-awake.ps1            # start (default 4 hours)
#   powershell -File scripts\dev\keep-awake.ps1 -Hours 8
#   powershell -File scripts\dev\keep-awake.ps1 -Stop
#   powershell -File scripts\dev\keep-awake.ps1 -Status
param([double]$Hours = 4, [switch]$Stop, [switch]$Status)
$ErrorActionPreference = 'Stop'
$pidFile = Join-Path $env:TEMP 'hotline-keep-awake.pid'

function Get-Holder {
    if (-not (Test-Path $pidFile)) { return $null }
    $holderPid = [int](Get-Content $pidFile -Raw)
    Get-Process -Id $holderPid -ErrorAction SilentlyContinue
}

if ($Status) {
    $p = Get-Holder
    if ($p) { "keep-awake running (pid $($p.Id), started $($p.StartTime))" } else { 'keep-awake not running' }
    return
}
if ($Stop) {
    $p = Get-Holder
    if ($p) { Stop-Process -Id $p.Id -Force; "keep-awake stopped (pid $($p.Id))" } else { 'keep-awake was not running' }
    Remove-Item $pidFile -ErrorAction SilentlyContinue
    return
}

$existing = Get-Holder
if ($existing) { Stop-Process -Id $existing.Id -Force }

$seconds = [int]($Hours * 3600)
$holder = @"
Add-Type -Namespace KeepAwake -Name Native -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);'
`$flags = [uint32]2147483648 -bor [uint32]1 -bor [uint32]2   # ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
`$end = (Get-Date).AddSeconds($seconds)
while ((Get-Date) -lt `$end) { [void][KeepAwake.Native]::SetThreadExecutionState(`$flags); Start-Sleep -Seconds 30 }
"@
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($holder))
$p = Start-Process powershell -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded)
Set-Content -Path $pidFile -Value $p.Id
"keep-awake started (pid $($p.Id)) until $((Get-Date).AddSeconds($seconds).ToString('HH:mm')); stop with -Stop"
