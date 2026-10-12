#!/usr/bin/env bash
# Creates a self-signed code-signing certificate matching the manifest Publisher and trusts it for MSIX sideloading
# (Local Machine › Trusted People needs one UAC prompt). Dev only. Uses openssl (included with Git for Windows).
# The .pfx gets a random password, saved next to it in certs/hotline-dev.password (certs/ is git-ignored);
# build-msix.sh reads it from there.
#   --dir PATH     write the files somewhere else (default certs/)
#   --no-trust     don't add it to Trusted People (e.g. CI, or a quick test)
source "$(dirname "$0")/lib/common.sh"

dir="$ROOT/certs" trust=true
while [ $# -gt 0 ]; do
    case $1 in
        --dir) dir=$2; shift 2 ;;
        --no-trust) trust=false; shift ;;
        *) die "unknown option $1 (see the top of $0)" ;;
    esac
done

subject=$(identity_attr Publisher) # e.g. CN=pmarc14 Hotline Dev
[ -n "$subject" ] || die "Package.appxmanifest has no Identity Publisher"
mkdir -p "$dir"
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT

# MSYS would rewrite "/CN=…" as a Windows path; "//CN=…" is how Git Bash passes a literal leading slash.
slash=/; [ -n "${MSYSTEM:-}" ] && slash=//
openssl req -x509 -newkey rsa:3072 -sha256 -days 1095 -nodes -keyout "$work/key.pem" -out "$work/cert.pem" \
    -subj "$slash${subject//, /\/}" -addext "keyUsage=critical,digitalSignature" \
    -addext "extendedKeyUsage=codeSigning" -addext "basicConstraints=CA:FALSE" 2>/dev/null
password=$(openssl rand -base64 24)
printf '%s' "$password" > "$dir/hotline-dev.password"
openssl pkcs12 -export -inkey "$work/key.pem" -in "$work/cert.pem" -name "Hotline Dev Signing" \
    -out "$dir/hotline-dev.pfx" -passout "pass:$password"
openssl x509 -in "$work/cert.pem" -outform der -out "$dir/hotline-dev.cer"

if $trust; then
    run_elevated certutil.exe -addstore TrustedPeople "$(winpath "$dir/hotline-dev.cer")"
    echo "Created and trusted $subject"
else
    echo "Created $subject in $dir (not trusted)"
fi
