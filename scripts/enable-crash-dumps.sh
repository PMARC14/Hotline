#!/usr/bin/env bash
# Opt-in: makes Windows Error Reporting keep full crash dumps of Hotline.exe in %LOCALAPPDATA%\Hotline\CrashDumps
# (last 5). Needs admin (one UAC prompt). Windows only.
#   scripts/enable-crash-dumps.sh [--disable]
source "$(dirname "$0")/lib/common.sh"

key='HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Hotline.exe'
if [ "${1:-}" = "--disable" ]; then
    run_elevated reg.exe delete "$key" /f
    echo "Hotline crash dumps disabled."
    exit
fi
dumps="$(cygpath -w "$LOCALAPPDATA")\\Hotline\\CrashDumps"
mkdir -p "$(cygpath -u "$dumps")"
# One elevated cmd so there's only one UAC prompt.
run_elevated cmd.exe /c "reg add \"$key\" /v DumpFolder /t REG_EXPAND_SZ /d \"$dumps\" /f && reg add \"$key\" /v DumpType /t REG_DWORD /d 2 /f && reg add \"$key\" /v DumpCount /t REG_DWORD /d 5 /f"
echo "Hotline crash dumps will be written to $dumps"
