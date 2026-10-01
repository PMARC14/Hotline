# End-to-end smoke test against the INSTALLED Hotline package. Windows PowerShell 5.1.
#   powershell -File tests\smoke\smoke.ps1            # test what's installed
#   powershell -File tests\smoke\smoke.ps1 -Install   # build + install first (needs scripts\dev-cert.ps1 once)
# Drives the same inputs Windows uses (protocol URIs, Copilot fast-path window messages, fallback hotkey)
# and asserts on the app log. Exit code = number of failed checks. Restores settings.json afterwards.
param([switch]$Install, [switch]$WithAgy)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if ($Install) { & (Join-Path $root 'scripts\install.ps1') | Out-Host }

Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class SmokeWin {
    public delegate bool P(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    public static IntPtr Popup(uint pid) {
        IntPtr r = IntPtr.Zero;
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); var c = new StringBuilder(256); GetClassName(h, c, 256);
            if (p == pid && c.ToString() == "WinUIDesktopWin32WindowClass") r = h; return true; }, IntPtr.Zero);
        return r;
    }
    public static void CtrlAltH() {
        keybd_event(0x11,0,0,UIntPtr.Zero); keybd_event(0x12,0,0,UIntPtr.Zero); keybd_event(0x48,0,0,UIntPtr.Zero);
        keybd_event(0x48,0,2,UIntPtr.Zero); keybd_event(0x12,0,2,UIntPtr.Zero); keybd_event(0x11,0,2,UIntPtr.Zero);
    }
}
"@

$pkg = Get-AppxPackage pmarc14.Hotline
if (-not $pkg) { Write-Error 'Hotline is not installed. Run with -Install.'; exit 1 }
$pfn = $pkg.PackageFamilyName
$state = Join-Path $env:USERPROFILE '.hotline'
$logFile = "$state\logs\hotline.log"
$settingsFile = "$state\settings.json"
$script:failures = 0

function Restart-Hotline {
    Get-Process Hotline -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Start-Process "shell:AppsFolder\$pfn!App"
    for ($i = 0; $i -lt 40 -and -not (Get-Process Hotline -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 250 }
    Start-Sleep 3
}
function Get-NewLog([int]$from) { @(Get-Content $logFile -ErrorAction SilentlyContinue | Select-Object -Skip $from) }
function Get-LogCount { @(Get-Content $logFile -ErrorAction SilentlyContinue).Count }
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { Write-Host "PASS  $name" -ForegroundColor Green }
    else { Write-Host "FAIL  $name $detail" -ForegroundColor Red; $script:failures++ }
}
function Expect-Log([string]$name, [int]$from, [string]$pattern) {
    $lines = Get-NewLog $from
    Check $name ([bool]($lines | Select-String -SimpleMatch $pattern)) "(missing '$pattern')"
}
function Send-Fast([int]$w) { [SmokeWin]::PostMessage([SmokeWin]::Popup([uint32](Get-Process Hotline).Id), 0x8001, [IntPtr]$w, [IntPtr]0) | Out-Null }

$originalSettings = if (Test-Path $settingsFile) { [IO.File]::ReadAllText($settingsFile) } else { $null }
try {
    # 1. Launch
    Restart-Hotline
    Check 'launches' ([bool](Get-Process Hotline -ErrorAction SilentlyContinue))
    $hotlinePid = (Get-Process Hotline).Id

    # 2. Protocol activation is redirected to the running instance
    $n = Get-LogCount
    Start-Process 'hotline://key?state=Tap'; Start-Sleep 2
    Start-Process 'hotline://key?state=Down'; Start-Sleep 2
    Expect-Log 'protocol Tap is handled' $n 'key Tap via Protocol -> TogglePopup'
    Expect-Log 'protocol hold is handled' $n 'key HoldStart via Protocol -> NewChat'
    Check 'single instance' (@(Get-Process Hotline).Count -eq 1) "($(@(Get-Process Hotline).Count) processes)"

    # 3. Copilot fast path (as the shell sends it)
    $n = Get-LogCount
    Send-Fast 0; Start-Sleep -Milliseconds 400; Send-Fast 1; Start-Sleep -Milliseconds 400; Send-Fast 2; Start-Sleep -Milliseconds 400; Send-Fast 9; Start-Sleep 1
    Expect-Log 'fast path Tap' $n 'key Tap via FastPath -> TogglePopup'
    Expect-Log 'fast path HoldStart' $n 'key HoldStart via FastPath -> NewChat'
    Expect-Log 'fast path HoldStop' $n 'key HoldStop via FastPath -> None'
    Expect-Log 'fast path unknown wParam is ignored' $n 'fast path: unknown wParam 9'

    # 4. Rapid taps don't crash (regression: NRE in PlaceOnActiveMonitor)
    1..15 | ForEach-Object { Send-Fast 0; Start-Sleep -Milliseconds 120 }
    Start-Sleep 1
    Check 'survives rapid taps' ([bool](Get-Process -Id $hotlinePid -ErrorAction SilentlyContinue))

    # 4b. Chat view loads in the popup
    $n = Get-LogCount
    Restart-Hotline
    for ($i = 0; $i -lt 20 -and -not (Get-NewLog $n | Select-String -SimpleMatch 'chat view ready'); $i++) { Start-Sleep -Milliseconds 500 }
    Expect-Log 'chat view loads' $n 'chat view ready'

    # 4c. Optional: a real agy round trip (needs agy installed and signed in)
    if ($WithAgy) {
        $n = Get-LogCount
        Start-Process 'hotline://key?state=Down'; Start-Sleep 2
        $shell = New-Object -ComObject WScript.Shell
        $shell.SendKeys('Reply with exactly: pong{ENTER}')
        for ($i = 0; $i -lt 120 -and -not (Get-NewLog $n | Select-String -Pattern 'chat answer (completed|failed)'); $i++) { Start-Sleep -Milliseconds 500 }
        Expect-Log 'agy answers' $n 'chat answer completed'
        Check 'one agy process' (@(Get-Process agy -ErrorAction SilentlyContinue).Count -eq 1)
    }

    # 5. Fallback hotkey
    $json = [IO.File]::ReadAllText($settingsFile) -replace '"fallbackHotkey":\s*(null|"[^"]*")', '"fallbackHotkey": "Ctrl+Alt+H"'
    [IO.File]::WriteAllText($settingsFile, $json)
    Restart-Hotline
    $n = Get-LogCount
    [SmokeWin]::CtrlAltH(); Start-Sleep 1
    Expect-Log 'fallback hotkey toggles' $n 'key Tap via Hotkey -> TogglePopup'

    # 6. Invalid hotkey is logged, app keeps running
    $json = [IO.File]::ReadAllText($settingsFile) -replace '"fallbackHotkey":\s*"[^"]*"', '"fallbackHotkey": "Banana"'
    [IO.File]::WriteAllText($settingsFile, $json)
    $n = Get-LogCount
    Restart-Hotline
    Expect-Log 'invalid hotkey is reported' $n "fallback hotkey 'Banana' is not valid"
    Check 'runs with invalid hotkey' ([bool](Get-Process Hotline -ErrorAction SilentlyContinue))

    # 7. Corrupt settings: app starts on defaults and keeps the bad file
    [IO.File]::WriteAllText($settingsFile, '{ not json')
    Restart-Hotline
    Check 'starts with corrupt settings' ([bool](Get-Process Hotline -ErrorAction SilentlyContinue))
    Check 'corrupt settings backed up' (Test-Path "$settingsFile.bad")

    # 8. Tray quit command exits cleanly
    [SmokeWin]::PostMessage([SmokeWin]::Popup([uint32](Get-Process Hotline).Id), 0x0111, [IntPtr]4, [IntPtr]0) | Out-Null
    Start-Sleep 2
    Check 'tray Quit exits' (-not (Get-Process Hotline -ErrorAction SilentlyContinue))
    Start-Sleep 1
    Check 'agy exits with Hotline' (-not (Get-Process agy -ErrorAction SilentlyContinue))
}
finally {
    Get-Process Hotline -ErrorAction SilentlyContinue | Stop-Process -Force
    if ($null -ne $originalSettings) { [IO.File]::WriteAllText($settingsFile, $originalSettings) }
    Remove-Item "$settingsFile.bad" -ErrorAction SilentlyContinue
    Start-Process 'hotline://tray'   # leave Hotline running in the tray, panel closed
}

Write-Host ''
if ($script:failures -eq 0) { Write-Host 'Smoke test passed.' -ForegroundColor Green } else { Write-Host "$script:failures check(s) failed." -ForegroundColor Red }
exit $script:failures
