#!/usr/bin/env bash
# Builds (and signs) the Hotline MSIX. Last output line = path to the .msix. Needs Windows (WinUI); runs in Git Bash.
#   --configuration Debug   debug build: verbose logging forced on, key-status line visible in the popup
#   --version 1.2.3.0       explicit package version (releases); default is a unique dev version
#                           0.1.<day-of-year>.<seconds-of-day/2> so dev builds install over each other
#                           without uninstalling (which would wipe settings and the Copilot key choice)
#   --certificate PATH      .pfx to sign with (default certs/hotline-dev.pfx from scripts/dev-cert.sh)
#   --certificate-password  default: $HOTLINE_CERT_PASSWORD, else the <certificate>.password file next to the .pfx
#                           prefer env/file: an explicit password argument is visible on the caller's own argv
#   --unsigned              build without signing (CI signs afterwards with Azure Artifact Signing)
#   --no-timestamp          sign without the default RFC 3161 timestamp (http://timestamp.digicert.com)
#   --arch x64|arm64|all    processor (default x64); "all" builds both and combines them into one .msixbundle,
#                           which Windows and the Store install as the right one for each PC
#   --publisher "CN=…"      package Publisher for this build; must equal the signing certificate's subject exactly
# Microsoft Store build: the identity values from Partner Center › your app › Product identity, unsigned (the Store
# signs it), version ending in .0:
#   scripts/build-msix.sh --store --version 0.2.3.0 --identity-name "PMARC14.HotlineAI" \
#     --publisher "CN=…" --publisher-display-name "PMARC14" --display-name "Hotline AI"
set +x # Passwords must not be echoed, including when invoked with bash -x.
source "$(dirname "$0")/lib/common.sh"

arch=x64 configuration=Release version="" certificate="" password="${HOTLINE_CERT_PASSWORD:-}" unsigned=false
publisher="" store=false identity_name="" publisher_display_name="" display_name=""
timestamp=true
# Keep the password shell-local until the signing helper needs it.
export -n password
unset HOTLINE_CERT_PASSWORD
while [ $# -gt 0 ]; do
    case $1 in
        --configuration) configuration=$2; shift 2 ;;
        --version) version=$2; shift 2 ;;
        --certificate) certificate=$2; shift 2 ;;
        --certificate-password) password=$2; shift 2 ;;
        --unsigned) unsigned=true; shift ;;
        --no-timestamp) timestamp=false; shift ;;
        --arch) arch=$2; shift 2 ;;
        --publisher) publisher=$2; shift 2 ;;
        --store) store=true; shift ;;
        --identity-name) identity_name=$2; shift 2 ;;
        --publisher-display-name) publisher_display_name=$2; shift 2 ;;
        --display-name) display_name=$2; shift 2 ;;
        *) die "unknown option $1 (see the top of $0)" ;;
    esac
done

case $arch in x64 | arm64 | all) ;; *) die "--arch must be x64, arm64 or all" ;; esac

if $store; then
    [ -n "$identity_name" ] && [ -n "$publisher" ] && [ -n "$publisher_display_name" ] && [ -n "$display_name" ] && [ -n "$version" ] ||
        die "--store needs --identity-name, --publisher, --publisher-display-name, --display-name and --version (Partner Center › Product identity)"
    [[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+\.0$ ]] || die "Store versions must end in .0 (got $version)"
    unsigned=true # the Store signs the package
fi

if ! $unsigned; then
    [ -n "$certificate" ] || certificate="$ROOT/certs/hotline-dev.pfx"
    [ -f "$certificate" ] || die "no signing certificate at $certificate; run scripts/dev-cert.sh first"
    certificate="$(cd "$(dirname "$certificate")" && pwd)/$(basename "$certificate")"
    password_file="${certificate%.*}.password"
    if [ -z "$password" ] && [ -f "$password_file" ]; then password=$(tr -d '\r\n' < "$password_file"); fi
    [ -n "$password" ] || die "no certificate password: pass --certificate-password, set HOTLINE_CERT_PASSWORD, or put it in $password_file"
fi

if [ -z "$version" ]; then
    seconds=$(( 10#$(date +%H) * 3600 + 10#$(date +%M) * 60 + 10#$(date +%S) ))
    version="0.1.$(( 10#$(date +%j) )).$(( seconds / 2 ))"
fi

stage=$(mktemp -d)
cleanup() { rm -rf -- "$stage"; }
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# Copy the working files, including uncommitted/untracked source edits. Never stage certificates,
# previous build products, or the shared obj/bin trees. Keep root-level MSBuild/NuGet/SDK settings.
stage_files=(src)
for file in "$ROOT"/*.props "$ROOT"/*.targets "$ROOT"/*.json "$ROOT"/*.config; do
    [ ! -f "$file" ] || stage_files+=("$(basename "$file")")
done
mkdir -p "$stage/source"
tar -C "$ROOT" --exclude=bin --exclude=obj --exclude=AppPackages --exclude=BundleArtifacts \
    --exclude=certs --exclude='*.pfx' --exclude='*.p12' --exclude='*.password' --exclude=__pycache__ \
    -cf - "${stage_files[@]}" | tar -C "$stage/source" -xf -
manifest="$stage/source/src/Hotline.App/Package.appxmanifest"
out="$stage/packages"
mkdir -p "$out"

# Patch only this build's staged manifest.
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
sed -E -i "${sed_args[@]}" "$manifest"

# The helper receives the password through its environment, never argv or a generated file.
# A mutex serializes temporary My-store imports across concurrent builds. Existing certificates
# and their keys are retained; newly imported certificates/keys are removed in finally.
cat > "$stage/sign.ps1" <<'POWERSHELL'
param([string]$Certificate, [string]$SignTool, [string]$Package, [switch]$NoTimestamp)
$ErrorActionPreference = 'Stop'
$securePassword = ConvertTo-SecureString $env:HOTLINE_SIGNING_PASSWORD -AsPlainText -Force
Remove-Item Env:HOTLINE_SIGNING_PASSWORD
$mutex = [System.Threading.Mutex]::new($false, 'Local\HotlineBuildSigning')
$locked = $false
$temporary = @()
try {
    try { $locked = $mutex.WaitOne() }
    catch [System.Threading.AbandonedMutexException] { $locked = $true }
    $pfx = Get-PfxData -FilePath $Certificate -Password $securePassword
    $leaf = @($pfx.EndEntityCertificates)
    if ($leaf.Count -ne 1) { throw 'The PFX must contain exactly one signing certificate.' }
    $thumbprint = $leaf[0].Thumbprint
    $certPath = "Cert:\CurrentUser\My\$thumbprint"
    if (Test-Path $certPath) {
        if (-not (Get-Item $certPath).HasPrivateKey) {
            throw 'The signing certificate already exists in CurrentUser\My without its private key.'
        }
    } else {
        $temporary = @(@($pfx.EndEntityCertificates) + @($pfx.OtherCertificates) |
            Where-Object { $_ -and -not (Test-Path "Cert:\CurrentUser\My\$($_.Thumbprint)") } |
            ForEach-Object { "Cert:\CurrentUser\My\$($_.Thumbprint)" })
        Import-PfxCertificate -FilePath $Certificate -Password $securePassword -CertStoreLocation Cert:\CurrentUser\My | Out-Null
    }
    $signArgs = @('sign', '/fd', 'SHA256', '/s', 'My', '/sha1', $thumbprint)
    if (-not $NoTimestamp) { $signArgs += @('/tr', 'http://timestamp.digicert.com', '/td', 'SHA256') }
    & $SignTool @signArgs $Package
    if ($LASTEXITCODE -ne 0) {
        throw "Signing/timestamping failed (signtool exit $LASTEXITCODE); no package will be published."
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $Package
    if (-not $signature.SignerCertificate) { throw 'The package has no signature.' }
    if (-not $NoTimestamp -and -not $signature.TimeStamperCertificate) {
        throw 'The timestamp server did not return a timestamp; no package will be published.'
    }
} finally {
    try {
        foreach ($path in $temporary) {
            if (Test-Path $path) { Remove-Item -Path $path -DeleteKey }
        }
    } finally {
        $securePassword.Dispose()
        if ($locked) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}
POWERSHELL

sign_package() {
    $unsigned && return 0
    local signtool timestamp_args=()
    signtool=$(sdk_tool signtool.exe)
    [ -n "$signtool" ] || die "signtool.exe not found"
    $timestamp || timestamp_args=(-NoTimestamp)
    HOTLINE_SIGNING_PASSWORD="$password" MSYS_NO_PATHCONV=1 powershell.exe -NoProfile -NonInteractive \
        -ExecutionPolicy Bypass -File "$(winpath "$stage/sign.ps1")" \
        -Certificate "$(winpath "$certificate")" -SignTool "$(winpath "$signtool")" \
        -Package "$(winpath "$1")" "${timestamp_args[@]}" >&2
}

# Builds one processor's .msix and prints its path.
build_one() {
    local rid=$1 platform=$2 msix
    (cd "$stage/source" && dotnet publish "$(winpath "$stage/source/src/Hotline.App/Hotline.App.csproj")" \
        -c "$configuration" -r "win-$rid" "-p:Platform=$platform" -p:GenerateAppxPackageOnBuild=true \
        -p:AppxPackageSigningEnabled=false "-p:AppxPackageDir=$(winpath "$out")\\") >&2 || return $?
    # Debug packages are named …_<rid>_Debug.msix; Release ones …_<rid>.msix.
    msix=$(find "$out" \( -name "*_${version}_${rid}.msix" -o -name "*_${version}_${rid}_Debug.msix" \) | tail -1)
    [ -n "$msix" ] || die "no $rid .msix for version $version was produced"
    sign_package "$msix" || return $?
    printf '%s\n' "$msix"
}

# The Windows SDK tools that come with the Microsoft.Windows.SDK.BuildTools package (makeappx, signtool).
sdk_tool() {
    local host=x64; [ "$(uname -m)" = aarch64 ] && host=arm64
    find "${NUGET_PACKAGES:-$HOME/.nuget/packages}/microsoft.windows.sdk.buildtools" -path "*/$host/$1" 2>/dev/null | sort -V | tail -1
}

case $arch in
    x64) result=$(build_one x64 x64) ;;
    arm64) result=$(build_one arm64 ARM64) ;;
    all)
        bundle_dir="$out/bundle-$version"
        mkdir -p "$bundle_dir"
        x64_package=$(build_one x64 x64)
        arm64_package=$(build_one arm64 ARM64)
        cp "$x64_package" "$arm64_package" "$bundle_dir/"
        makeappx=$(sdk_tool makeappx.exe); [ -n "$makeappx" ] || die "makeappx.exe not found (restore the app project first)"
        result="$out/Hotline_${version}.msixbundle"
        MSYS_NO_PATHCONV=1 "$makeappx" bundle /o /bv "$version" /d "$(winpath "$bundle_dir")" /p "$(winpath "$result")" >&2
        rm -rf "$bundle_dir"
        sign_package "$result"
        ;;
esac

# Publish only completed packages. A bundle keeps its documented path, artifacts/Hotline_<version>.msixbundle (release.yml
# reads it there); a single package goes to a unique folder, even for concurrent identical versions.
mkdir -p "$ROOT/artifacts"
if [ "$arch" = all ]; then
    cp "$result" "$ROOT/artifacts/"
    result="$ROOT/artifacts/$(basename "$result")"
else
    published=$(mktemp -d "$ROOT/artifacts/Hotline.App_${version}_${arch}_XXXXXXXX")
    cp "$result" "$published/"
    result="$published/$(basename "$result")"
fi
out="$ROOT/artifacts"

# Keep the newest package folders; every build is ~90 MB per processor and they piled up to gigabytes.
ls -dt "$out"/Hotline.App_*/ 2>/dev/null | tail -n +5 | while read -r old; do rm -rf "$old"; done || true # none yet: ls fails, which pipefail would turn into an exit
ls -t "$out"/Hotline_*.msixbundle 2>/dev/null | tail -n +3 | while read -r old; do rm -f "$old"; done || true

printf '%s\n' "$result"
