# Shared helpers for the bash scripts (source it; don't run it).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MANIFEST="$ROOT/src/Hotline.App/Package.appxmanifest"

die() { echo "error: $*" >&2; exit 1; }

# A path Windows programs (dotnet, certutil) understand; unchanged on Linux.
winpath() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s\n' "$1"; fi; }

# Escapes & < > " for XML attributes and text.
xml_escape() { local s=$1; s=${s//&/&amp;}; s=${s//</&lt;}; s=${s//>/&gt;}; s=${s//\"/&quot;}; printf '%s' "$s"; }

# Escapes a replacement string for sed (& \ and the | delimiter).
sed_escape() { printf '%s' "$1" | sed -e 's/[&\|]/\&/g'; }

# Reads an <Identity> attribute (Name, Publisher, Version) from the manifest.
identity_attr() { grep -o "<Identity [^>]*" "$MANIFEST" | grep -o " $1=\"[^\"]*\"" | head -1 | sed -E 's/^ [A-Za-z]+="(.*)"$/\1/'; }

# Runs a Windows command as administrator (one UAC prompt): gsudo if installed, else PowerShell's RunAs.
# Arguments are passed as Windows wants them (Git Bash would otherwise turn "/f" into a drive path).
run_elevated() {
    export MSYS_NO_PATHCONV=1
    if command -v gsudo >/dev/null 2>&1; then gsudo "$@"; return; fi
    local cmd=$1; shift
    local args=""; for a in "$@"; do args+="'${a//\'/\'\'}',"; done
    powershell.exe -NoProfile -Command "Start-Process -Verb RunAs -Wait -FilePath '$cmd' -ArgumentList @(${args%,})"
}
