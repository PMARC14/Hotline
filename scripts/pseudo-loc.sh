#!/usr/bin/env bash
# Pseudo-localization for checking the interface by eye: writes Strings.qps-ploc.resx (git-ignored) from the English
# Strings.resx with every letter accented and each text ~40% longer, in [brackets]. English that still shows plainly
# was missed by the resources; a cut-off "]" is text that doesn't fit. Placeholders ({0}, {1:g}) and XML entities
# are left alone.
#   scripts/pseudo-loc.sh            write it (then rebuild; set "general.language": "qps-ploc" and restart Hotline)
#   scripts/pseudo-loc.sh --remove   delete it again
source "$(dirname "$0")/lib/common.sh"

dir="$ROOT/src/Hotline.Core/Text/Localization"
out="$dir/Strings.qps-ploc.resx"
if [ "${1:-}" = --remove ]; then rm -f "$out"; echo "removed $out"; exit 0; fi

LC_ALL=C.UTF-8 awk '
function accent(s,    i, c, r, m) {
    r = ""
    for (i = 1; i <= length(s); i++) {
        c = substr(s, i, 1)
        m = index(plain, c)
        r = r (m ? substr(fancy, m, 1) : c)
    }
    return r
}
function pseudo(v,    out, rest, pos, len, n) {
    out = ""; rest = v
    # keep {placeholders} and &entities; as they are; accent everything between them
    while (match(rest, /\{[^}]*\}|&[a-zA-Z#0-9]+;/)) {
        out = out accent(substr(rest, 1, RSTART - 1)) substr(rest, RSTART, RLENGTH)
        rest = substr(rest, RSTART + RLENGTH)
    }
    out = out accent(rest)
    n = int(length(v) * 0.4) + 1
    pad = ""; while (length(pad) < n) pad = pad "~"
    return "[" out pad "]"
}
BEGIN { plain = "aeiouyAEIOUYcnsCNSz"; fancy = "àéîõüýÀÉÎÕÜÝçñšÇÑŠž" }
buffer != "" { buffer = buffer "\n" $0; if (index($0, "</value>")) { emit(buffer); buffer = "" } next }
/<data / && /<value>/ { if (index($0, "</value>")) emit($0); else buffer = $0; next } # texts only (not <resheader>); may span lines
{ print }
function emit(line,    pre, val, post) {
    pre = substr(line, 1, index(line, "<value>") + 6)
    val = substr(line, index(line, "<value>") + 7)
    post = substr(val, index(val, "</value>"))
    val = substr(val, 1, index(val, "</value>") - 1)
    print pre pseudo(val) post
}
' "$dir/Strings.resx" > "$out"
echo "$out"
