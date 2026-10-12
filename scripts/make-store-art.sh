#!/usr/bin/env bash
# Renders the Microsoft Store artwork from store/art/art.html with headless Microsoft Edge (or Chrome/Chromium on
# Linux) into artifacts/store/: poster 1440x2160 (9:16), box art 2160x2160 (1:1), logos 300/150/71 (transparent).
# Needs ffmpeg for the small logos.
#   scripts/make-store-art.sh [--browser PATH]
source "$(dirname "$0")/lib/common.sh"

browser=""
[ "${1:-}" = "--browser" ] && browser=$2
if [ -z "$browser" ]; then
    for b in "/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" "/c/Program Files/Microsoft/Edge/Application/msedge.exe" \
        microsoft-edge chromium google-chrome; do
        if [ -x "$b" ] || command -v "$b" >/dev/null 2>&1; then browser=$b; break; fi
    done
fi
[ -n "$browser" ] || die "no Edge/Chromium found; pass --browser PATH"

out="$ROOT/artifacts/store"
mkdir -p "$out"
page="file:///$(winpath "$ROOT/store/art/art.html" | tr '\\' '/')"

# render NAME WIDTH HEIGHT QUERY
render() {
    local file; file=$(winpath "$out/$1.png")
    "$browser" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
        --default-background-color=00000000 --window-size="$2,$3" --screenshot="$file" "$page?$4" >/dev/null 2>&1
    [ -s "$out/$1.png" ] || die "rendering $1 failed"
    echo "$out/$1.png"
}

render poster-1440x2160 1440 2160 "kind=poster"
render boxart-2160x2160 2160 2160 "kind=box"
# Logos: headless browsers won't make windows this small, so render one large tile and scale it down (keeps alpha).
command -v ffmpeg >/dev/null 2>&1 || die "ffmpeg is needed to scale the logos"
big=$(render logo-1200 1200 1200 "kind=tile")
for size in 300 150 71; do
    ffmpeg -loglevel error -y -i "$(winpath "$big")" -vf "scale=$size:$size:flags=lanczos" "$(winpath "$out/logo-${size}x${size}.png")"
    echo "$out/logo-${size}x${size}.png"
done
rm -f "$big"
