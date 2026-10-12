#!/usr/bin/env bash
# Writes Hotline.appinstaller: opening it installs Hotline from the GitHub Release, and Windows then checks the
# "latest" copy of this file for updates (once a day, in the background, without blocking launch).
# Identity (Name, Publisher) comes from the manifest so it always matches the signed package.
#   scripts/make-appinstaller.sh --version 1.2.3.0 --tag v1.2.3 --msix-name Hotline_1.2.3.0.msixbundle --out artifacts/Hotline.appinstaller
#   (--msix-name: the release's .msixbundle, or a single x64 .msix)
#   --repository OWNER/REPO   default PMARC14/Hotline
#   --publisher "CN=…"        the signing certificate's subject when it differs from the manifest (build-msix.sh --publisher)
source "$(dirname "$0")/lib/common.sh"

version="" tag="" msix_name="" out="" repository=PMARC14/Hotline publisher=""
while [ $# -gt 0 ]; do
    case $1 in
        --version) version=$2; shift 2 ;;
        --tag) tag=$2; shift 2 ;;
        --msix-name) msix_name=$2; shift 2 ;;
        --out) out=$2; shift 2 ;;
        --repository) repository=$2; shift 2 ;;
        --publisher) publisher=$2; shift 2 ;;
        *) die "unknown option $1 (see the top of $0)" ;;
    esac
done
[ -n "$version" ] && [ -n "$tag" ] && [ -n "$msix_name" ] && [ -n "$out" ] || die "--version, --tag, --msix-name and --out are required"
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "version '$version' must look like 1.2.3.0"

name=$(identity_attr Name)
[ -n "$publisher" ] || publisher=$(identity_attr Publisher)
[ -n "$name" ] && [ -n "$publisher" ] || die "Package.appxmanifest has no Identity Name/Publisher"

self="https://github.com/$repository/releases/latest/download/Hotline.appinstaller"
msix="https://github.com/$repository/releases/download/$tag/$msix_name"
# A .msixbundle (x64 + ARM64) is a MainBundle; a single .msix is an x64 MainPackage.
if [[ $msix_name == *.msixbundle ]]; then
    main="<MainBundle Name=\"$(xml_escape "$name")\" Publisher=\"$(xml_escape "$publisher")\" Version=\"$version\" Uri=\"$(xml_escape "$msix")\" />"
else
    main="<MainPackage Name=\"$(xml_escape "$name")\" Publisher=\"$(xml_escape "$publisher")\" Version=\"$version\" ProcessorArchitecture=\"x64\" Uri=\"$(xml_escape "$msix")\" />"
fi
mkdir -p "$(dirname "$out")"
cat > "$out" <<XML
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018" Version="$version" Uri="$(xml_escape "$self")">
  $main
  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="24" ShowPrompt="false" UpdateBlocksActivation="false" />
    <AutomaticBackgroundTask />
  </UpdateSettings>
</AppInstaller>
XML
printf '%s\n' "$out"
