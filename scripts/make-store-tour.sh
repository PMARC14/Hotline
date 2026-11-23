#!/usr/bin/env bash
# Records an in-depth tour video (about 1:40, 1920x1080, no audio, English captions burned in) of the REAL installed Hotline
# on this PC's screen: the demo conversations, then Settings page by page. Windows only. Not part of the Store submission by
# default (the 38 s trailer from make-store-media.sh is); ready for when you want the longer one.
# The panel and the Settings window appear on screen for about two minutes: don't use the PC meanwhile.
# It calls no AI and sends no keystrokes (hotline:// links only). What links can't show (region capture, voice, Continue in,
# shortcuts, moving and resizing with Alt, pinning) is listed in store/video/storyboard.md to record by hand.
# Output: artifacts/store/tour.mp4 and tour-thumbnail.png.
#   scripts/make-store-tour.sh              (install the build first: scripts/install.sh)
#   scripts/make-store-tour.sh --dry-run    checks the caption/card pipeline on a synthetic picture: no screen, no Hotline needed
source "$(dirname "$0")/lib/common.sh"

dry=0
[ "${1:-}" = "--dry-run" ] && dry=1

source "$(dirname "$0")/lib/store-capture.sh"

# The tour: SECONDS|ACTION|CAPTION. ACTION: demo:math|summary|slash (a demo conversation), tap (the key press that hides the
# panel), page:TAG (Settings on that page; the first one moves the window onto the screen), close (closes Settings).
steps=(
    "9|demo:math|Press the Copilot key: ask anything, answers stream in."
    "1|tap|"
    "8|demo:summary|Text you selected in another app comes along with your question."
    "1|tap|"
    "4|demo:slash|Type / for quick actions. Write your own as simple text files."
    "1|tap|"
    "7|page:Connections|Use Claude Code, Codex or Gemini sign-ins, your own API key, or a local model."
    "8|page:Window|Arrange the bar: drag buttons between rows and add your own."
    "5|page:Appearance|Light, dark or high contrast, with your accent colour."
    "5|page:Chat|Recent chats and memory stay on your PC."
    "5|page:Actions|Quick actions are plain text files."
    "6|page:Tools|Tools from your MCP servers; anything that changes something asks first."
    "6|page:Backup|Move your setup to another PC. API keys are never exported."
    "5|page:General|Use the Copilot key, or set any shortcut you like."
)
lead=1.5 # seconds of recording before the first step

total=0
for s in "${steps[@]}"; do total=$((total + ${s%%|*})); done
len=$(python -c "print($total + $lead + 2)")

# ffmpeg drawtext filters for the captions: one per step with a caption, shown from its start to its end.
caption_filters() {
    local t=$lead i=0 filters="" sec action text start end file
    for s in "${steps[@]}"; do
        sec=${s%%|*}; rest=${s#*|}; action=${rest%%|*}; text=${rest#*|}
        if [ -n "$text" ]; then
            i=$((i + 1))
            file="$work/caption-$i.txt"
            printf '%s' "$text" > "$file"
            start=$t; end=$(python -c "print($t + $sec)")
            filters="$filters,drawtext=fontfile='C\\:/Windows/Fonts/segoeui.ttf':textfile='$(cygpath -m "$file" | sed 's/:/\\:/')':fontsize=46:fontcolor=white:box=1:boxcolor=black@0.6:boxborderw=26:x=(w-text_w)/2:y=h-text_h-90:enable='between(t,$start,$end)'"
        fi
        t=$(python -c "print($t + $sec)")
    done
    printf '%s' "$filters"
}

run_step() { # ACTION
    case "$1" in
        demo:*) open_link "hotline://demo?scene=${1#demo:}" ;;
        tap) open_link "hotline://key?state=tap" ;;
        page:*)
            open_link "hotline://settings?page=${1#page:}"
            if [ "$placed" = 0 ]; then sleep 4; place_settings 440 120; placed=1; fi ;;
        close) close_settings ;;
    esac
}

captions=$(caption_filters)
rec="$work/recording.mp4"
if [ "$dry" = 1 ]; then
    echo "dry run: synthetic picture, $len s" >&2
    ffmpeg -loglevel error -y -f lavfi -i "color=c=0x1c1830:s=1920x1080:r=30:d=$len" \
        -vf "format=yuv420p$captions" -c:v libx264 -preset veryfast -crf 20 "$(winpath "$rec")" || die "caption pipeline failed"
else
    echo "Recording the tour for about two minutes: please don't use the PC." >&2
    "$edge" --kiosk "$art?kind=bg" --edge-kiosk-type=fullscreen --user-data-dir="$profile" --no-first-run \
        --no-default-browser-check >/dev/null 2>&1 &
    sleep 4
    front_backdrop
    grab "$work/backdrop.png"
    IFS=, read -r sw sh < <(ffprobe -v error -show_entries stream=width,height -of csv=p=0 "$(winpath "$work/backdrop.png")" | tr -d '\r')
    rh=$(( sw * 9 / 16 )); [ "$rh" -le "$sh" ] || rh=$sh
    rw=$(( rh * 16 / 9 / 2 * 2 )); rh=$(( rh / 2 * 2 ))
    ox=$(( (sw - rw) / 2 )); oy=$(( (sh - rh) / 4 ))
    ffmpeg -loglevel error -y -f lavfi -i "ddagrab=output_idx=0:draw_mouse=0:framerate=30:offset_x=$ox:offset_y=$oy:video_size=${rw}x${rh}" \
        -vf "hwdownload,format=bgra,scale=1920:1080:flags=lanczos,format=yuv420p$captions" -c:v libx264 -preset veryfast -crf 16 -t "$len" \
        "$(winpath "$rec")" &
    recorder=$!
    sleep "$lead"
    placed=0
    for s in "${steps[@]}"; do
        sec=${s%%|*}; rest=${s#*|}; action=${rest%%|*}
        run_step "$action"
        sleep "$sec"
    done
    close_settings
    wait "$recorder"
fi

# Title and end cards (3 s each, same look as the Store art), then the recording.
for card in title end; do
    case $card in
        title) q="kind=card" ;;
        end) q="kind=card&title=Hotline%20AI&sub=Free%20and%20open%20source%20%C2%B7%20Microsoft%20Store" ;;
    esac
    "$edge" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --window-size=1920,1080 \
        --screenshot="$(winpath "$work/$card.png")" "$art?$q" >/dev/null 2>&1
done
cp "$work/title.png" "$out/tour-thumbnail.png"
ffmpeg -loglevel error -y \
    -loop 1 -t 3 -framerate 30 -i "$(winpath "$work/title.png")" \
    -i "$(winpath "$rec")" \
    -loop 1 -t 3 -framerate 30 -i "$(winpath "$work/end.png")" \
    -filter_complex "[0]scale=1920:1080,format=yuv420p,fade=out:st=2.5:d=0.5[a];[1]fade=in:d=0.5[b];[2]scale=1920:1080,format=yuv420p,fade=in:d=0.5[c];[a][b][c]concat=n=3:v=1:a=0" \
    -c:v libx264 -preset slow -crf 18 -pix_fmt yuv420p -movflags +faststart "$(winpath "$out/tour.mp4")" || die "joining the tour failed"
echo "$out/tour.mp4"
