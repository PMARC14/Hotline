#!/usr/bin/env bash
# Builds, installs (or updates) and starts Hotline in the tray. Windows only.
#   scripts/install.sh [--configuration Debug]     install the debug-logging build instead of release
source "$(dirname "$0")/lib/common.sh"

msix=$("$(dirname "$0")/build-msix.sh" "$@" | tail -1)
MSYS_NO_PATHCONV=1 taskkill.exe /IM Hotline.exe /F >/dev/null 2>&1 || true
# Installing a package has no command-line tool besides PowerShell's Add-AppxPackage.
powershell.exe -NoProfile -Command "Add-AppxPackage -Path '$(winpath "$msix")' -ForceApplicationShutdown -ForceUpdateFromAnyVersion" ||
    die "install failed"
echo "Installed $(identity_attr Name) from $msix"
MSYS_NO_PATHCONV=1 cmd.exe /c start "" "hotline://tray" # start in the tray, panel closed
