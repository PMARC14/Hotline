#!/usr/bin/env bash
# Shared by scripts/make-store-media.sh and scripts/make-store-tour.sh (source it after lib/common.sh): finds Edge and ffmpeg,
# makes a work folder, and defines open_link (hotline:// links), grab (a screenshot of the first monitor), close_settings and
# place_settings (the Settings window, found by STORE_SETTINGS_TITLE), window_rect, crop_rect, shot, and the cleanup that runs on exit.
# Output goes to $STORE_OUT, default artifacts/store/.
command -v ffmpeg >/dev/null 2>&1 || die "ffmpeg is needed"
edge="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
[ -x "$edge" ] || edge="/c/Program Files/Microsoft/Edge/Application/msedge.exe"
[ -x "$edge" ] || die "Microsoft Edge not found"

out="${STORE_OUT:-$ROOT/artifacts/store}"
export STORE_SETTINGS_TITLE="${STORE_SETTINGS_TITLE:-Hotline settings}" # the Settings window's title in the interface language
work=$(mktemp -d)
mkdir -p "$out"
art="file:///$(cygpath -m "$ROOT/store/art/art.html")"
profile=$(winpath "$work/edge")

open_link() { MSYS_NO_PATHCONV=1 cmd.exe /c start "" "$1"; }
grab() { ffmpeg -loglevel error -y -f lavfi -i "ddagrab=output_idx=0:draw_mouse=0" -vf "hwdownload,format=bgra" -frames:v 1 "$(winpath "$1")"; }
# Brings the full-screen Edge backdrop (started with --user-data-dir under $work) to the foreground and checks that it IS the
# foreground window, so nothing of the desktop is captured and nobody has to click it. Windows only lets the foreground
# thread hand over focus, so this attaches to its input queue first (the same way Hotline takes focus for its own panel).
# Then checks that the grabbed picture really is the dark backdrop. Stops with a message if either check fails.
front_backdrop() {
    cat > "$work/front-backdrop.ps1" <<'PS'
Add-Type -Namespace W -Name F -MemberDefinition @"
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int cmd);
[DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
"@
foreach ($try in 1..30) {
    $ids = @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
        Where-Object { $_.CommandLine -like "*$($env:STORE_WORK_NAME)*" -and $_.CommandLine -notlike '*--type=*' } | ForEach-Object { $_.ProcessId })
    $p = $ids | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue } | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($p) {
        $h = $p.MainWindowHandle
        foreach ($attempt in 1..10) {
            [void][W.F]::ShowWindow($h, 9) # SW_RESTORE
            $fg = [W.F]::GetForegroundWindow()
            [uint32]$fgPid = 0
            $fgThread = [W.F]::GetWindowThreadProcessId($fg, [ref]$fgPid)
            $me = [W.F]::GetCurrentThreadId()
            [void][W.F]::AttachThreadInput($me, $fgThread, $true)
            [void][W.F]::SetForegroundWindow($h)
            [void][W.F]::BringWindowToTop($h)
            [void][W.F]::AttachThreadInput($me, $fgThread, $false)
            Start-Sleep -Milliseconds 500
            if ([W.F]::GetForegroundWindow() -eq $h) { exit 0 }
        }
        exit 2
    }
    Start-Sleep -Milliseconds 500
}
exit 1
PS
    STORE_WORK_NAME=$(basename "$work") powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(winpath "$work/front-backdrop.ps1")" ||
        die "couldn't bring the backdrop window to the front (Edge window not found or Windows refused focus)"
    sleep 1
    grab "$work/backdrop-check.png"
    local luma
    luma=$(ffmpeg -loglevel error -i "$(winpath "$work/backdrop-check.png")" -vf "signalstats,metadata=print:file=-" -f null - 2>/dev/null |
        grep -o 'YAVG=[0-9.]*' | head -1 | cut -d= -f2)
    python -c "import sys; sys.exit(0 if float(sys.argv[1]) < 90 else 1)" "${luma:-255}" ||
        die "the screen doesn't show the dark backdrop (average brightness ${luma:-?}): another window is in front"
}
# Closes Hotline's settings window with WM_CLOSE (no keystrokes) and checks it's gone.
close_settings() {
    cat > "$work/close-settings.ps1" <<'PS'
Add-Type -Namespace W -Name U -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr h, uint m, System.IntPtr w, System.IntPtr l);
"@
function Find-Settings { Get-Process Hotline -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq $env:STORE_SETTINGS_TITLE } }
foreach ($try in 1..10) {
    $p = Find-Settings
    if (-not $p) { exit 0 }
    [void][W.U]::PostMessage($p.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
}
exit 1
PS
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(winpath "$work/close-settings.ps1")" ||
        die "couldn't close the Hotline settings window"
}
# Moves the Settings window to X,Y (physical pixels) without resizing: Windows opens it wherever it likes, which was partly
# below the screen. Matches the window by its (localized) title, STORE_SETTINGS_TITLE. Writes the window's visible rectangle
# "x y width height" (physical pixels) to $work/settings.rect for crop_rect.
place_settings() { # X Y
    cat > "$work/place-settings.ps1" <<'PS'
Add-Type -Namespace W -Name P -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(System.IntPtr h, int attr, out RECT r, int size);
public struct RECT { public int L, T, R, B; }
"@
[void][W.P]::SetProcessDPIAware() # physical pixels, like the screen grab
foreach ($try in 1..20) {
    $p = Get-Process Hotline -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq $env:STORE_SETTINGS_TITLE }
    if ($p) {
        # SWP_NOSIZE 0x1 | SWP_NOZORDER 0x4 | SWP_NOACTIVATE 0x10
        [void][W.P]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, [int]$args[0], [int]$args[1], 0, 0, 0x15)
        Start-Sleep -Milliseconds 800
        $r = New-Object W.P+RECT
        # DWMWA_EXTENDED_FRAME_BOUNDS = 9: the visible frame, without the shadow
        [void][W.P]::DwmGetWindowAttribute($p.MainWindowHandle, 9, [ref]$r, 16)
        Set-Content -Path $args[2] -Value ("{0} {1} {2} {3}" -f $r.L, $r.T, ($r.R - $r.L), ($r.B - $r.T)) -Encoding ascii
        exit 0
    }
    Start-Sleep -Milliseconds 500
}
exit 1
PS
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(winpath "$work/place-settings.ps1")" "$1" "$2" "$(winpath "$work/settings.rect")" ||
        die "couldn't find the Hotline settings window (title: $STORE_SETTINGS_TITLE)"
}
# The visible rectangle "x y width height" (physical pixels) of Hotline's window with this exact title, written to FILE. The panel
# is titled "Hotline"; Settings has its own (localized) title. Windows that are hidden or parked off-screen don't count.
window_rect() { # TITLE FILE
    cat > "$work/window-rect.ps1" <<'PS'
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinFind {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
  struct RECT { public int L, T, R, B; }
  public static string Find(uint[] pids, string title) {
    string found = null;
    EnumWindows(delegate (IntPtr h, IntPtr l) {
      if (found != null || !IsWindowVisible(h)) return true;
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (Array.IndexOf(pids, pid) < 0) return true;
      StringBuilder sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (sb.ToString() != title) return true;
      RECT r; if (DwmGetWindowAttribute(h, 9, out r, 16) != 0) return true; // DWMWA_EXTENDED_FRAME_BOUNDS
      if (r.L < -10000 || r.R - r.L < 50 || r.B - r.T < 50) return true;
      found = r.L + " " + r.T + " " + (r.R - r.L) + " " + (r.B - r.T);
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@
[void][WinFind]::SetProcessDPIAware()
$pids = [uint32[]]@(Get-Process Hotline -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
foreach ($try in 1..10) {
    $r = [WinFind]::Find($pids, $args[0])
    if ($r) { Set-Content -Path $args[1] -Value $r -Encoding ascii; exit 0 }
    Start-Sleep -Milliseconds 400
}
exit 1
PS
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(winpath "$work/window-rect.ps1")" "$1" "$(winpath "$2")" ||
        die "couldn't find a visible Hotline window titled \"$1\""
}
# Crops a screen grab to a window rectangle from window_rect / place_settings (default: the Settings window's).
crop_rect() { # SHOT OUT [RECT_FILE]
    local x y w h
    read -r x y w h < "${3:-$work/settings.rect}"
    ffmpeg -loglevel error -y -i "$(winpath "$1")" -vf "crop=$w:$h:$x:$y" "$(winpath "$2")"
}

# shot NAME PANEL CAPTION — puts a cropped panel (artifacts/store/panel-PANEL.png) on a 1920x1080 card with a caption.
shot() {
    local file; file=$(winpath "$out/$1.png")
    "$edge" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --window-size=1920,1080 \
        --screenshot="$file" "$art?kind=shot&img=../../artifacts/store/panel-$2.png&caption=$(python -c 'import sys,urllib.parse;print(urllib.parse.quote(sys.argv[1]))' "$3")" >/dev/null 2>&1
    [ -s "$out/$1.png" ] || die "composing $1 failed"
    echo "$out/$1.png"
}
