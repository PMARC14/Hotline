#!/usr/bin/env bash
# Builds (and signs) the Hotline MSIX. Last output line = path to the .msix. Needs Windows (WinUI); runs in Git Bash.
#   --configuration Debug   debug build: verbose logging forced on, key-status line visible in the popup
#   --version 1.2.3.0       explicit package version (releases); default is a unique dev version
#                           0.1.<day-of-year>.<seconds-of-day/2> so dev builds install over each other
#                           without uninstalling (which would wipe settings and the Copilot key choice)
#   --certificate PATH      .pfx to sign with (default certs/hotline-dev.pfx from scripts/dev-cert.sh)
#   --certificate-password  default: $HOTLINE_CERT_PASSWORD, else the <certificate>.password file next to the .pfx
#   --unsigned              build without signing (CI signs afterwards with Azure Artifact Signing)
#   --publisher "CN=…"      package Publisher for this build; must equal the signing certificate's subject exactly
# Microsoft Store build: the identity values from Partner Center › your app › Product identity, unsigned (the Store
# signs it), version ending in .0:
#   scripts/build-msix.sh --store --version 0.2.3.0 --identity-name "PMARC14.HotlineAI" \
#     --publisher "CN=…" --publisher-display-name "PMARC14" --display-name "Hotline AI"
source "$(dirname "$0")/lib/common.sh"

configuration=Release version="" certificate="" password="${HOTLINE_CERT_PASSWORD:-}" unsigned=false
publisher="" store=false identity_name="" publisher_display_name="" display_name=""
while [ $# -gt 0 ]; do
    case $1 in
        --configuration) configuration=$2; shift 2 ;;
        --version) version=$2; shift 2 ;;
        --certificate) certificate=$2; shift 2 ;;
        --certificate-password) password=$2; shift 2 ;;
        --unsigned) unsigned=true; shift ;;
        --publisher) publisher=$2; shift 2 ;;
        --store) store=true; shift ;;
        --identity-name) identity_name=$2; shift 2 ;;
        --publisher-display-name) publisher_display_name=$2; shift 2 ;;
        --display-name) display_name=$2; shift 2 ;;
        *) die "unknown option $1 (see the top of $0)" ;;
    esac
done

if $store; then
    [ -n "$identity_name" ] && [ -n "$publisher" ] && [ -n "$publisher_display_name" ] && [ -n "$display_name" ] && [ -n "$version" ] ||
        die "--store needs --identity-name, --publisher, --publisher-display-name, --display-name and --version (Partner Center › Product identity)"
    [[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+\.0$ ]] || die "Store versions must end in .0 (got $version)"
    unsigned=true # the Store signs the package
fi

signing=(-p:AppxPackageSigningEnabled=false)
if ! $unsigned; then
    [ -n "$certificate" ] || certificate="$ROOT/certs/hotline-dev.pfx"
    [ -f "$certificate" ] || die "no signing certificate at $certificate; run scripts/dev-cert.sh first"
    password_file="${certificate%.*}.password"
    if [ -z "$password" ] && [ -f "$password_file" ]; then password=$(tr -d '\r\n' < "$password_file"); fi
    [ -n "$password" ] || die "no certificate password: pass --certificate-password, set HOTLINE_CERT_PASSWORD, or put it in $password_file"
    signing=(-p:AppxPackageSigningEnabled=true "-p:PackageCertificateKeyFile=$(winpath "$certificate")" "-p:PackageCertificatePassword=$password")
fi

if [ -z "$version" ]; then
    seconds=$(( 10#$(date +%H) * 3600 + 10#$(date +%M) * 60 + 10#$(date +%S) ))
    version="0.1.$(( 10#$(date +%j) )).$(( seconds / 2 ))"
fi

out="$ROOT/artifacts"
original=$(mktemp)
cp "$MANIFEST" "$original"
trap 'cp "$original" "$MANIFEST"; rm -f "$original"' EXIT # always put the manifest back

# Patch the manifest for this build (the repo copy is restored on exit).
sed_args=(-e "s|(<Identity [^>]*Version=\")[^\"]+|\1$(sed_escape "$version")|")
[ -z "$publisher" ] || sed_args+=(-e "s|(<Identity [^>]*Publisher=\")[^\"]+|\1$(sed_escape "$(xml_escape "$publisher")")|")
[ -z "$identity_name" ] || sed_args+=(-e "s|(<Identity [^>]*Name=\")[^\"]+|\1$(sed_escape "$(xml_escape "$identity_name")")|")
[ -z "$publisher_display_name" ] ||
    sed_args+=(-e "s|<PublisherDisplayName>[^<]*</PublisherDisplayName>|<PublisherDisplayName>$(sed_escape "$(xml_escape "$publisher_display_name")")</PublisherDisplayName>|")
if [ -n "$display_name" ]; then
    # The package name and the Start menu name must be a name reserved in Partner Center.
    name=$(sed_escape "$(xml_escape "$display_name")")
    sed_args+=(-e "/<Properties>/,/<\/Properties>/s|<DisplayName>[^<]*</DisplayName>|<DisplayName>$name</DisplayName>|")
    sed_args+=(-e "s|(<uap:VisualElements DisplayName=\")[^\"]+|\1$name|")
fi
sed -E "${sed_args[@]}" "$original" > "$MANIFEST"

dotnet publish "$(winpath "$ROOT/src/Hotline.App/Hotline.App.csproj")" -c "$configuration" -r win-x64 \
    -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true "${signing[@]}" "-p:AppxPackageDir=$(winpath "$out")\\" >&2

# Keep the three newest package folders; every dev build is ~90 MB and they piled up to gigabytes.
ls -dt "$out"/Hotline.App_*/ 2>/dev/null | tail -n +4 | while read -r old; do rm -rf "$old"; done

msix=$(find "$out" -name "*_${version}_*.msix" | tail -1)
[ -n "$msix" ] || die "no .msix for version $version was produced"
printf '%s\n' "$msix"
