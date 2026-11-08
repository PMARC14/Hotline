#!/usr/bin/env bash
# Shared by scripts/make-store-media.sh and scripts/make-store-tour.sh (source it after lib/common.sh): finds Edge and ffmpeg,
# makes a work folder, and defines open_link (hotline:// links), grab (a screenshot of the first monitor), front_backdrop,
# window_rect, place_settings, crop_rect, close_settings, shot, and the cleanup that runs on exit (also on Ctrl+C and kill).
# Window work is done by scripts/lib/win-helper.py (ctypes: no PowerShell, no compiler). Output goes to $STORE_OUT, default
# artifacts/store/.
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
helper="$(winpath "$ROOT/scripts/lib/win-helper.py")"
recorder="" # set by a script that records in the background: the cleanup stops it

open_link() { MSYS_NO_PATHCONV=1 cmd.exe /c start "" "$1"; }
grab() { ffmpeg -loglevel error -y -f lavfi -i "ddagrab=output_idx=0:draw_mouse=0" -vf "hwdownload,format=bgra" -frames:v 1 "$(winpath "$1")"; }

# Brings the full-screen Edge backdrop (started with --user-data-dir under $work) to the foreground and checks that it IS the
# foreground window, so nothing of the desktop is captured and nobody has to click it. Then checks that the grabbed picture
# really is the dark backdrop (not a black screen, not another window). Stops with a message if a check fails. Remembers
# the backdrop's process id for the cleanup.
front_backdrop() {
    python "$helper" front "Hotline Store backdrop" "$(winpath "$work/edge.pid")" ||
        die "couldn't bring the backdrop window to the front (Edge window not found or Windows refused focus)"
    sleep 1
    grab "$work/backdrop-check.png"
    local luma
    luma=$(ffmpeg -loglevel error -i "$(winpath "$work/backdrop-check.png")" -vf "signalstats,metadata=print:file=-" -f null - 2>/dev/null |
        grep -o 'YAVG=[0-9.]*' | head -1 | cut -d= -f2)
    python -c "import sys; sys.exit(0 if 3 < float(sys.argv[1]) < 90 else 1)" "${luma:-255}" ||
        die "the screen doesn't show the dark backdrop (average brightness ${luma:-?}): another window is in front, or the display is off"
}

# The visible rectangle "x y width height" (physical pixels) of Hotline's window with this exact title, written to FILE. The panel
# is titled "Hotline"; Settings has its own (localized) title.
window_rect() { # TITLE FILE
    python "$helper" rect "$1" "$(winpath "$2")" || die "couldn't find a visible Hotline window titled \"$1\""
}
# Moves the Settings window to X,Y (physical pixels) without resizing: Windows opens it wherever it likes, which was partly
# below the screen. Writes its rectangle to $work/settings.rect for crop_rect.
place_settings() { # X Y
    python "$helper" place "$STORE_SETTINGS_TITLE" "$1" "$2" "$(winpath "$work/settings.rect")" ||
        die "couldn't find the Hotline settings window (title: $STORE_SETTINGS_TITLE)"
}
# Crops a screen grab to a window rectangle from window_rect / place_settings (default: the Settings window's).
crop_rect() { # SHOT OUT [RECT_FILE]
    local x y w h
    read -r x y w h < "${3:-$work/settings.rect}"
    ffmpeg -loglevel error -y -i "$(winpath "$1")" -vf "crop=$w:$h:$x:$y" "$(winpath "$2")"
}
# Closes Hotline's settings window with WM_CLOSE (no keystrokes) and checks it's gone.
close_settings() {
    python "$helper" close "$STORE_SETTINGS_TITLE" || die "couldn't close the Hotline settings window"
}

# Runs on every exit, Ctrl+C and kill: stops the recorder, closes Settings, closes the backdrop browser (by its process id, with
# its children), puts Hotline back in the tray and deletes the work folder.
cleanup() {
    trap - EXIT INT TERM
    [ -n "${recorder:-}" ] && kill "$recorder" 2>/dev/null
    python "$helper" close "$STORE_SETTINGS_TITLE" >/dev/null 2>&1 || true
    if [ -s "$work/edge.pid" ]; then
        MSYS_NO_PATHCONV=1 taskkill.exe /PID "$(cat "$work/edge.pid")" /T /F >/dev/null 2>&1 || true
    else # the backdrop window was never found (a failed start): find the browser by its private profile folder
        powershell.exe -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='msedge.exe'\" | Where-Object { \$_.CommandLine -like '*$(basename "$work")*' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force }" >/dev/null 2>&1 || true
    fi
    open_link "hotline://tray" 2>/dev/null || true
    local try
    for try in 1 2 3 4 5; do rm -rf "$work" 2>/dev/null && [ ! -d "$work" ] && break; sleep 1; done
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# shot NAME PANEL CAPTION — puts a cropped panel (artifacts/store/panel-PANEL.png) on a 1920x1080 card with a caption.
shot() {
    local file; file=$(winpath "$out/$1.png")
    "$edge" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --window-size=1920,1080 \
        --screenshot="$file" "$art?kind=shot&img=../../artifacts/store/panel-$2.png&caption=$(python -c 'import sys,urllib.parse;print(urllib.parse.quote(sys.argv[1]))' "$3")" >/dev/null 2>&1
    [ -s "$out/$1.png" ] || die "composing $1 failed"
    echo "$out/$1.png"
}
