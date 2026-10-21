# Drives Settings > Window > Button rows by UI Automation: moves one button with the editor's arrow buttons and checks that
# toolbar.json follows. Windows only; a Settings window is visible for a few seconds. Your toolbar.json is saved first and
# put back at the end (also when a check fails); the Settings window is closed.
#   powershell -File tests\smoke\toolbar-editor.ps1
# Needs Hotline installed and running in the tray (scripts/install.sh) with an English interface (the nav item is "Window").
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$path = Join-Path $env:USERPROFILE '.hotline\toolbar.json'
$backup = Join-Path ([IO.Path]::GetTempPath()) ('toolbar-editor-' + [guid]::NewGuid().ToString('N') + '.json')
$failures = @()

function Arr($x) { if ($null -eq $x) { ,@() } else { ,@($x) } } # a missing group (the centre is optional) is an empty list, not a list of one $null

function Read-Bar {
    # toolbar.json has // comments; strip them, then read the four groups.
    $text = (Get-Content $path -Raw) -replace '(?m)^\s*//.*$', ''
    $bar = $text | ConvertFrom-Json
    [pscustomobject]@{
        TL = Arr $bar.top.left; TC = Arr $bar.top.center; TR = Arr $bar.top.right; BL = Arr $bar.items.left; BC = Arr $bar.items.center; BR = Arr $bar.items.right
    }
}

function Find-Element([System.Windows.Automation.AutomationElement]$root, [string]$automationId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $automationId)
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Wait-For($what, [scriptblock]$test, [int]$seconds = 5) {
    $until = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $until) { if (& $test) { return $true }; Start-Sleep -Milliseconds 150 }
    $script:failures += "timed out: $what"
    return $false
}

function Press($window, [string]$automationId) {
    $el = $null
    if (-not (Wait-For "element $automationId" { $script:el = Find-Element $window $automationId; $null -ne $script:el })) { return $false }
    $el = $script:el
    if (-not $el.Current.IsEnabled) { $script:failures += "$automationId is disabled"; return $false }
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
}

function Check($what, [bool]$ok) { if (-not $ok) { $script:failures += "wrong: $what" } else { Write-Host "ok: $what" } }

$settingsWindow = $null
try {
    if (-not (Get-Process Hotline -ErrorAction SilentlyContinue)) { throw 'Hotline is not running (scripts/install.sh starts it in the tray)' }
    if (Test-Path $path) { Copy-Item $path $backup -Force }
    # A known bar, so the moves below are checked against something fixed.
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    @'
{ "version": 2,
  "top": { "left": ["effort", "model", "provider"], "right": [] },
  "items": { "left": ["pin", "captureWindow", "prompt"], "right": ["settings"] } }
'@ | Set-Content -Path $path -Encoding utf8
    Start-Sleep -Seconds 1 # the running Hotline applies it

    Start-Process 'hotline://settings'
    $pids = @(Get-Process Hotline | ForEach-Object Id)
    Wait-For 'the Settings window' {
        foreach ($p in $pids) {
            $cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p)
            foreach ($w in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
                $nav = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Window')))
                if ($nav) { $script:settingsWindow = $w; return $true }
            }
        }
        $false
    } 10 | Out-Null
    if (-not $settingsWindow) { throw 'the Settings window did not open' }

    $nav = $settingsWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Window')))
    $nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Wait-For 'the quadrant editor' { $null -ne (Find-Element $settingsWindow 'quadrant-bottomleft') } 8 | Out-Null

    $bar = Read-Bar
    Check 'start: bottom left is pin, captureWindow, prompt' (($bar.BL -join ',') -eq 'pin,captureWindow,prompt')

    if (Press $settingsWindow 'bar-move-right-pin') {
        Wait-For 'pin one place to the right' { (Read-Bar).BL -join ',' -eq 'captureWindow,pin,prompt' } | Out-Null
        Check 'move right: captureWindow, pin, prompt' (((Read-Bar).BL -join ',') -eq 'captureWindow,pin,prompt')
    }
    if (Press $settingsWindow 'bar-shift-right-pin') {
        Wait-For 'pin in the centre group' { (Read-Bar).BC -contains 'pin' } | Out-Null
        $bar = Read-Bar
        Check 'shift right: bottom centre is pin' (($bar.BC -join ',') -eq 'pin')
        Check 'shift right: bottom left keeps captureWindow, prompt' (($bar.BL -join ',') -eq 'captureWindow,prompt')
    }
    if (Press $settingsWindow 'bar-shift-right-pin') {
        Wait-For 'pin on the right side' { (Read-Bar).BR -contains 'pin' } | Out-Null
        $bar = Read-Bar
        Check 'shift right again: bottom right is settings, pin' (($bar.BR -join ',') -eq 'settings,pin')
        Check 'shift right again: bottom centre is empty' ($bar.BC.Count -eq 0)
    }
    if (Press $settingsWindow 'bar-shift-up-pin') {
        Wait-For 'pin in the top row' { (Read-Bar).TR -contains 'pin' } | Out-Null
        $bar = Read-Bar
        Check 'shift up: top right is pin' (($bar.TR -join ',') -eq 'pin')
        Check 'shift up: bottom right is settings' (($bar.BR -join ',') -eq 'settings')
    }
    if (Press $settingsWindow 'bar-hide-pin') {
        Wait-For 'pin hidden' { $b = Read-Bar; -not (($b.TL + $b.TC + $b.TR + $b.BL + $b.BC + $b.BR) -contains 'pin') } | Out-Null
        $bar = Read-Bar
        Check 'hide: pin is in no group' (-not (($bar.TL + $bar.TC + $bar.TR + $bar.BL + $bar.BC + $bar.BR) -contains 'pin'))
        Check 'hide: the pin chip is in "Not shown"' ($null -ne (Find-Element $settingsWindow 'bar-item-pin'))
    }
}
catch { $failures += "script error: $($_.Exception.Message)" }
finally {
    if ($settingsWindow) {
        try { $settingsWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
    }
    if (Test-Path $backup) { Copy-Item $backup $path -Force; Remove-Item $backup -Force }
    elseif (Test-Path $path) { Remove-Item $path -Force } # there was none: Hotline writes the defaults again on its next read
}
if ($failures.Count -gt 0) { Write-Host ''; $failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }; exit 1 }
Write-Host 'toolbar-editor: all checks passed' -ForegroundColor Green
