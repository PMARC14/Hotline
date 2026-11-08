#!/usr/bin/env bash
# Captures the Store screenshots and trailer from the REAL installed Hotline, on this PC's screen. Windows only.
# Shows a full-screen backdrop (store/art/art.html?kind=bg in an Edge kiosk window, so no desktop is captured), plays
# the scripted demo scenes (hotline://demo?scene=…, no AI calls, no keystrokes) and records them with ffmpeg.
# Takes about a minute; the panel and the settings window appear on screen — don't use the PC meanwhile.
# Output in artifacts/store/: screenshot-1..4.png (1920x1080), trailer.mp4 (1920x1080) and trailer-thumbnail.png.
#   scripts/make-store-media.sh              (install the build to capture first: scripts/install.sh)
#   scripts/make-store-media.sh --no-video   screenshots and panel crops only (no trailer); the crops in artifacts/store/panel-*.png
#                                            feed scripts/make-store-localized.sh
#   scripts/make-store-media.sh --panels-only  only the panel crops (artifacts/store/panel-*.png, or $STORE_OUT/panel-*.png);
#                                            scripts/make-store-panels.sh runs this once per interface language
source "$(dirname "$0")/lib/common.sh"

video=1; panels_only=0
case "${1:-}" in --no-video) video=0 ;; --panels-only) video=0; panels_only=1 ;; esac

source "$(dirname "$0")/lib/store-capture.sh"

echo "Showing the backdrop and the demo scenes for about a minute: please don't use the PC." >&2
"$edge" --kiosk "$art?kind=bg" --edge-kiosk-type=fullscreen --user-data-dir="$profile" --no-first-run \
    --no-default-browser-check >/dev/null 2>&1 &
sleep 4
front_backdrop
grab "$work/backdrop.png"

# ---- screenshots ------------------------------------------------------------------------------------------------
# Seconds after opening each scene to take its screenshot (summary: while the selected-text chip is showing).
declare -A wait_for=([math]=8 [summary]=3 [slash]=3)
for scene in math summary slash; do
    open_link "hotline://demo?scene=$scene"; sleep "${wait_for[$scene]}"
    window_rect "Hotline" "$work/panel.rect"
    grab "$work/$scene.png"
    open_link "hotline://key?state=tap"; sleep 1.5 # hide the panel (same as pressing the key)
    crop_rect "$work/$scene.png" "$out/panel-$scene.png" "$work/panel.rect"
done
open_link "hotline://settings?page=Connections"; sleep 4 # the AI connections page: it matches the caption "Use the AI you already have"
place_settings 40 40; sleep 1.5
grab "$work/settings.png"; close_settings; sleep 1.5
crop_rect "$work/settings.png" "$out/panel-settings.png"
# Captions match store/listing.md.
if [ "$panels_only" = 0 ]; then
    shot screenshot-1 math "Press the Copilot key: ask anything, answers stream in."
    shot screenshot-2 summary "Selected text comes along with your question."
    shot screenshot-3 slash "Quick actions with /: translate, summarize, fix, explain."
    shot screenshot-4 settings "Use the AI you already have, and set everything your way."
fi

if [ "$video" = 1 ]; then
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
fi
