#!/usr/bin/env bash
# Composes the Store screenshots, poster and box art for every listing language (English and the 16 in store/listings),
# from the panel crops that scripts/make-store-media.sh captured. Nothing is shown on screen: it only runs headless Edge.
# Per language: artifacts/store/<lang>/screenshot-1..4.png (1920x1080), poster-1440x2160.png, boxart-2160x2160.png.
# Captions come from each listing file, the poster/box text from store/art/taglines.json. The panel is in that language
# when scripts/make-store-panels.sh captured it, else the English one; the logos have no text, so one set serves all.
#   scripts/make-store-panels.sh               once, to capture the panels in every interface language
#   (or scripts/make-store-media.sh --no-video for the English panels only, used for every language)
#   scripts/make-store-localized.sh [lang ...]  all languages, or just the ones named
source "$(dirname "$0")/lib/common.sh"

edge="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
[ -x "$edge" ] || edge="/c/Program Files/Microsoft/Edge/Application/msedge.exe"
[ -x "$edge" ] || die "Microsoft Edge not found"

out="$ROOT/artifacts/store"
[ -s "$out/panel-math.png" ] || [ -d "$out/panels" ] ||
    die "no panel pictures: run scripts/make-store-panels.sh (one set per language) or scripts/make-store-media.sh --no-video (English) first"
art="file:///$(cygpath -m "$ROOT/store/art/art.html")"

if [ $# -gt 0 ]; then
    langs=("$@")
else
    langs=(en)
    for f in "$ROOT"/store/listings/*.md; do langs+=("$(basename "$f" .md)"); done
fi

count=0
for lang in "${langs[@]}"; do
    mkdir -p "$out/$lang"
    while IFS=$'\t' read -r name width height query; do
        file=$(winpath "$out/$lang/$name.png")
        "$edge" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
            --window-size="$width,$height" --screenshot="$file" "$art?$query" >/dev/null 2>&1
        [ -s "$out/$lang/$name.png" ] || die "rendering $lang/$name failed"
        count=$((count + 1))
    done < <(python "$(winpath "$ROOT/scripts/lib/store-text.py")" "$(winpath "$ROOT")" "$lang" | tr -d '\r')
    echo "$lang done"
done
echo "$count images in $out/<lang>/"
