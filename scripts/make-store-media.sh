#!/usr/bin/env bash
# Captures the Store screenshots and trailer from the REAL installed Hotline, on this PC's screen. Windows only.
# Shows a full-screen backdrop (store/art/art.html?kind=bg in an Edge kiosk window, so no desktop is captured), plays
# the scripted demo scenes (hotline://demo?scene=…, no AI calls, no keystrokes) and records them with ffmpeg.
# Takes about a minute; the panel and the settings window appear on screen — don't use the PC meanwhile.
# Output in artifacts/store/: screenshot-1..4.png (1920x1080), trailer.mp4 (1920x1080) and trailer-thumbnail.png.
#   scripts/make-store-media.sh        (install the build to capture first: scripts/install.sh)
source "$(dirname "$0")/lib/common.sh"

command -v ffmpeg >/dev/null 2>&1 || die "ffmpeg is needed"
edge="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
[ -x "$edge" ] || edge="/c/Program Files/Microsoft/Edge/Application/msedge.exe"
[ -x "$edge" ] || die "Microsoft Edge not found"

out="$ROOT/artifacts/store"
work=$(mktemp -d)
mkdir -p "$out"
art="file:///$(cygpath -m "$ROOT/store/art/art.html")"
profile=$(winpath "$work/edge")

open_link() { MSYS_NO_PATHCONV=1 cmd.exe /c start "" "$1"; }
grab() { ffmpeg -loglevel error -y -f lavfi -i "ddagrab=output_idx=0:draw_mouse=0" -vf "hwdownload,format=bgra" -frames:v 1 "$(winpath "$1")"; }
# Closes Hotline's settings window with WM_CLOSE (no keystrokes) and checks it's gone.
close_settings() {
    cat > "$work/close-settings.ps1" <<'PS'
Add-Type -Namespace W -Name U -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr h, uint m, System.IntPtr w, System.IntPtr l);
"@
function Find-Settings { Get-Process Hotline -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq 'Hotline settings' } }
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
cleanup() {
    ( close_settings ) 2>/dev/null || true # never leave the settings window open
    open_link "hotline://tray" 2>/dev/null || true
    powershell.exe -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='msedge.exe'\" | Where-Object { \$_.CommandLine -like '*$(basename "$work")*' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force -ErrorAction SilentlyContinue }" || true
    sleep 1; rm -rf "$work"
}
trap cleanup EXIT

# Where the panel is in a capture: compare it with the backdrop-only frame (at 1/8 size), pad, and crop it out.
crop_panel() { # SHOT BACKDROP OUT
    local box
    box=$(ffmpeg -loglevel error -i "$(winpath "$1")" -i "$(winpath "$2")" -filter_complex \
        "[0][1]blend=all_mode=difference,scale=iw/8:ih/8,format=gray" -f rawvideo - |
        python -c "
import sys
w, h = (int(v) // 8 for v in sys.argv[1].split(',')); d = sys.stdin.buffer.read()
pts = [(i % w, i // w) for i, v in enumerate(d) if v > 24]
xs, ys = [p[0] for p in pts], [p[1] for p in pts]
x0, y0, x1, y1 = max(min(xs) - 1, 0) * 8, max(min(ys) - 1, 0) * 8, min(max(xs) + 2, w) * 8, min(max(ys) + 2, h) * 8
print(f'{x1 - x0}:{y1 - y0}:{x0}:{y0}')" "$(ffprobe -v error -show_entries stream=width,height -of csv=p=0 "$(winpath "$1")" | tr -d '\r')")
    ffmpeg -loglevel error -y -i "$(winpath "$1")" -vf "crop=$box" "$(winpath "$3")"
}

# shot NAME PANEL CAPTION — puts a cropped panel (artifacts/store/panel-PANEL.png) on a 1920x1080 card with a caption.
shot() {
    local file; file=$(winpath "$out/$1.png")
    "$edge" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --window-size=1920,1080 \
        --screenshot="$file" "$art?kind=shot&img=../../artifacts/store/panel-$2.png&caption=$(python -c 'import sys,urllib.parse;print(urllib.parse.quote(sys.argv[1]))' "$3")" >/dev/null 2>&1
    [ -s "$out/$1.png" ] || die "composing $1 failed"
    echo "$out/$1.png"
}

echo "Showing the backdrop and the demo scenes for about a minute: please don't use the PC." >&2
"$edge" --kiosk "$art?kind=bg" --edge-kiosk-type=fullscreen --user-data-dir="$profile" --no-first-run \
    --no-default-browser-check >/dev/null 2>&1 &
sleep 5
grab "$work/backdrop.png"

# ---- screenshots ------------------------------------------------------------------------------------------------
# Seconds after opening each scene to take its screenshot (summary: while the selected-text chip is showing).
declare -A wait_for=([math]=8 [summary]=3 [slash]=3)
for scene in math summary slash; do
    open_link "hotline://demo?scene=$scene"; sleep "${wait_for[$scene]}"
    grab "$work/$scene.png"
    open_link "hotline://key?state=tap"; sleep 1.5 # hide the panel (same as pressing the key)
    crop_panel "$work/$scene.png" "$work/backdrop.png" "$out/panel-$scene.png"
done
open_link "hotline://settings"; sleep 4
grab "$work/settings.png"; close_settings; sleep 1.5
crop_panel "$work/settings.png" "$work/backdrop.png" "$out/panel-settings.png"
# Captions match store/listing.md.
shot screenshot-1 math "Press the Copilot key: ask anything, answers stream in."
shot screenshot-2 summary "Selected text comes along with your question."
shot screenshot-3 slash "Quick actions with /: translate, summarize, fix, explain."
shot screenshot-4 settings "Use the AI you already have, and set everything your way."

# ---- trailer: a real recording of the same scenes -----------------------------------------------------------------
# 16:9 area around the panel (the screen is usually 16:10), recorded at full resolution, scaled to 1920x1080 later.
IFS=, read -r sw sh < <(ffprobe -v error -show_entries stream=width,height -of csv=p=0 "$(winpath "$work/backdrop.png")" | tr -d '\r')
rh=$(( sw * 9 / 16 )); [ "$rh" -le "$sh" ] || rh=$sh
rw=$(( rh * 16 / 9 / 2 * 2 )); rh=$(( rh / 2 * 2 ))
ox=$(( (sw - rw) / 2 )); oy=$(( (sh - rh) / 4 )) # a little above centre: the panel opens in the upper part
ffmpeg -loglevel error -y -f lavfi -i "ddagrab=output_idx=0:draw_mouse=0:framerate=30:offset_x=$ox:offset_y=$oy:video_size=${rw}x${rh}" \
    -vf "hwdownload,format=bgra,scale=1920:1080:flags=lanczos,format=yuv420p" -c:v libx264 -preset veryfast -crf 16 -t 32 \
    "$(winpath "$work/recording.mp4")" &
recorder=$!
sleep 1.5
open_link "hotline://demo?scene=math"; sleep 9
open_link "hotline://key?state=tap"; sleep 0.8
open_link "hotline://demo?scene=summary"; sleep 7.5
open_link "hotline://key?state=tap"; sleep 0.8
open_link "hotline://demo?scene=slash"; sleep 3
open_link "hotline://key?state=tap"; sleep 0.5
open_link "hotline://settings" # slow to appear the first time: the recording runs on until -t ends
wait "$recorder"
close_settings

# Title and end cards (3 s each, same look as the Store art), then the recording; plus the thumbnail.
for card in title end; do
    case $card in
        title) q="kind=card" ;;
        end) q="kind=card&title=Hotline%20AI&sub=Free%20and%20open%20source%20%C2%B7%20Microsoft%20Store" ;;
    esac
    "$edge" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --window-size=1920,1080 \
        --screenshot="$(winpath "$work/$card.png")" "$art?$q" >/dev/null 2>&1
done
cp "$work/title.png" "$out/trailer-thumbnail.png"
ffmpeg -loglevel error -y \
    -loop 1 -t 3 -framerate 30 -i "$(winpath "$work/title.png")" \
    -i "$(winpath "$work/recording.mp4")" \
    -loop 1 -t 3 -framerate 30 -i "$(winpath "$work/end.png")" \
    -filter_complex "[0]scale=1920:1080,format=yuv420p,fade=out:st=2.5:d=0.5[a];[1]fade=in:d=0.5[b];[2]scale=1920:1080,format=yuv420p,fade=in:d=0.5[c];[a][b][c]concat=n=3:v=1:a=0" \
    -c:v libx264 -preset slow -crf 18 -pix_fmt yuv420p -movflags +faststart "$(winpath "$out/trailer.mp4")"
echo "$out/trailer.mp4"
