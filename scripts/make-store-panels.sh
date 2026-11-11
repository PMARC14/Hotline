#!/usr/bin/env bash
# Captures the panel pictures for the Store screenshots once per interface language, from the REAL installed Hotline.
# For each language it sets general.language in %USERPROFILE%\.hotline\settings.json, restarts Hotline (the language applies at
# start), checks the log says that language is active, and runs scripts/make-store-media.sh --panels-only into
# artifacts/store/panels/<lang>/. The demo conversation is interface text (Strings.Demo_*), so it is translated too.
# Windows only. About 45 seconds per language: the panel and Settings window appear on screen, don't use the PC meanwhile.
#
# Your settings.json is copied to artifacts/store/settings.before-panels.json (and compared) first, and put back byte for byte
# at the end, also when something fails or you press Ctrl+C. Nothing else under .hotline is touched.
#   scripts/make-store-panels.sh [lang ...]     default: en and the 16 languages in store/listings
source "$(dirname "$0")/lib/common.sh"

settings="$HOME/.hotline/settings.json"
log="$HOME/.hotline/logs/hotline.log"
[ -f "$settings" ] || die "no $settings: start Hotline once first"
out="$ROOT/artifacts/store"
mkdir -p "$out/panels"
backup="$out/settings.before-panels.json"
if [ -f "$backup" ]; then # a run that didn't finish: the backup is still the original, never overwrite it with a changed file
    if ! cmp -s "$settings" "$backup"; then
        echo "A previous run didn't finish: putting your original settings.json back from $backup first." >&2
        cp -p "$backup" "$settings" && cmp -s "$settings" "$backup" || die "couldn't restore settings.json from $backup"
    fi
else
    cp -p "$settings" "$backup" && cmp -s "$settings" "$backup" || die "couldn't back up settings.json; nothing was changed"
fi

if [ $# -gt 0 ]; then
    langs=("$@")
else
    langs=(en)
    for f in "$ROOT"/store/listings/*.md; do langs+=("$(basename "$f" .md)"); done
fi

stop_hotline() { # the language applies at start; Hotline has no quit link, so it is ended (its tray icon vanishes at the next mouse-over)
    MSYS_NO_PATHCONV=1 taskkill.exe /IM Hotline.exe /F >/dev/null 2>&1 || true
    sleep 2
}
start_hotline() { # starts it in the tray and waits (up to 40 s) until its log says which interface language it came up in
    local before try
    before=$(grep -c "interface language:" "$log" 2>/dev/null || true)
    MSYS_NO_PATHCONV=1 cmd.exe /c start "" "hotline://tray"
    for try in $(seq 1 80); do
        [ "$(grep -c "interface language:" "$log" 2>/dev/null || true)" -gt "${before:-0}" ] && { sleep 3; return 0; }
        sleep 0.5
    done
    return 1
}
restart_hotline() { stop_hotline; start_hotline || true; }
restored=0
restore() {
    [ "$restored" = 1 ] && return
    restored=1
    cp -p "$backup" "$settings"
    if cmp -s "$settings" "$backup"; then echo "settings.json restored (identical to the backup)" >&2; rm -f "$backup"
    else echo "WARNING: settings.json differs from the backup; the original is $backup" >&2; fi
    restart_hotline
}
trap restore EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

set_language() { # TAG: change only general.language, keep the rest of the file as it is
    python - "$(winpath "$settings")" "$1" <<'PY'
import re, sys
path, tag = sys.argv[1], sys.argv[2]
text = open(path, encoding="utf-8", newline="").read()
new, n = re.subn(r'("general"\s*:\s*\{[^}]*?"language"\s*:\s*)"[^"]*"', lambda m: m.group(1) + '"' + tag + '"', text, count=1, flags=re.S)
if n != 1:
    sys.exit("general.language not found in settings.json")
open(path, "w", encoding="utf-8", newline="").write(new)
PY
}

for lang in "${langs[@]}"; do
    tag=$lang; [ "$lang" = en ] && tag=en-US
    echo "== $lang ==" >&2
    stop_hotline # first, so the app can't save settings.json while it is being edited
    set_language "$tag" || die "couldn't set the language"
    start_hotline || die "Hotline didn't start (no 'interface language' line in $log within 40 s); settings.json will be restored"
    grep "interface language:" "$log" | tail -1 | grep -q "interface language: $tag" ||
        die "Hotline didn't start in $tag (see $log); stopped, settings.json will be restored"
    mkdir -p "$out/panels/$lang"
    # The Settings window's title in that language (Settings_Title), so the capture can find, move and close it.
    title=$(python - "$(winpath "$ROOT")" "$lang" <<'PY'
import re, sys
root, lang = sys.argv[1], sys.argv[2]
name = "Strings.resx" if lang == "en" else f"Strings.{lang}.resx"
text = open(f"{root}/src/Hotline.Core/Text/Localization/{name}", encoding="utf-8").read()
m = re.search(r'name="Settings_Title"[^>]*><value>(.*?)</value>', text, flags=re.S)
sys.exit("Settings_Title not found") if not m else print(m.group(1))
PY
    ) || die "no Settings_Title for $lang"
    STORE_SETTINGS_TITLE=$(printf '%s' "$title" | tr -d '\r') STORE_OUT="$out/panels/$lang" "$(dirname "$0")/make-store-media.sh" --panels-only ||
        die "capture failed for $lang"
    for p in math summary slash settings; do
        [ -s "$out/panels/$lang/panel-$p.png" ] || die "panels/$lang/panel-$p.png is missing"
    done
done
echo "panels in $out/panels/<lang>/; now: scripts/make-store-localized.sh" >&2
