#!/usr/bin/env bash
# Runs the CI workflow's steps (.github/workflows/ci.yml) on this PC, for when Actions minutes run out. Windows only.
# ARM64 unit tests need an ARM64 machine: on x64 the ARM64 job only builds the app.
#   scripts/ci-local.sh              unit tests (Release), unsigned MSIX, x64 app, ARM64 app
#   scripts/ci-local.sh --repeat N   run the unit tests N times (timing-sensitive tests show up on a busy PC)
source "$(dirname "$0")/lib/common.sh"

repeat=1
while [ $# -gt 0 ]; do
    case $1 in
        --repeat) repeat=$2; shift 2 ;;
        *) die "unknown option $1 (see the top of $0)" ;;
    esac
done
cd "$ROOT"

step() { printf '\n== %s\n' "$*"; }
for i in $(seq 1 "$repeat"); do
    step "Unit tests (Release) $i/$repeat"
    dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj -c Release
done
step "Build unsigned MSIX"
bash scripts/build-msix.sh --unsigned >/dev/null
step "Build app (x64)"
dotnet build src/Hotline.App/Hotline.App.csproj -c Release -p:Platform=x64 -nologo -v:q
step "Build app (ARM64)"
dotnet build src/Hotline.App/Hotline.App.csproj -c Release -p:Platform=ARM64 -r win-arm64 -nologo -v:q
if [ "$(powershell.exe -NoProfile -Command '$env:PROCESSOR_ARCHITECTURE' | tr -d '\r')" = ARM64 ]; then
    step "Unit tests (ARM64, native)"
    dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj -c Release
else
    echo "(ARM64 unit tests skipped: not an ARM64 machine)"
fi
step "CI passed: $(git rev-parse --short HEAD)"
