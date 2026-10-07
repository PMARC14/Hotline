# Builds and signs the Hotline MSIX. Last output line = path to the .msix.
#   -Configuration Debug   debug build: verbose logging forced on, key-status line visible in the popup
#   -Version 1.2.3.0       explicit package version (releases); default is a unique dev version
#                          0.1.<day-of-year>.<seconds-of-day/2> so dev builds install over each other
#                          without uninstalling (which would wipe settings and the Copilot key choice)
#   -Unsigned              build without signing (CI signs afterwards with Azure Artifact Signing)
#   -Publisher "CN=…"      package Publisher for this build; must equal the signing certificate's subject exactly
param(
    [string]$Configuration = 'Release',
    [string]$Version,
    [string]$CertificatePath,
    # Default: $env:HOTLINE_CERT_PASSWORD, else the <certificate>.password file next to the .pfx (dev-cert.ps1 writes it).
    [string]$CertificatePassword,
    [switch]$Unsigned,
    [string]$Publisher
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$signing = @('-p:AppxPackageSigningEnabled=false')
if (-not $Unsigned) {
    if (-not $CertificatePath) { $CertificatePath = Join-Path $root 'certs\hotline-dev.pfx' }
    if (-not (Test-Path $CertificatePath)) { throw "No signing cert at $CertificatePath. Run scripts\dev-cert.ps1 first." }
    if (-not $CertificatePassword) { $CertificatePassword = $env:HOTLINE_CERT_PASSWORD }
    $passwordFile = [IO.Path]::ChangeExtension($CertificatePath, '.password')
    if (-not $CertificatePassword -and (Test-Path $passwordFile)) { $CertificatePassword = [IO.File]::ReadAllText($passwordFile).Trim() }
    if (-not $CertificatePassword) { throw "No certificate password: pass -CertificatePassword, set HOTLINE_CERT_PASSWORD, or put it in $passwordFile." }
    $signing = @('-p:AppxPackageSigningEnabled=true', "-p:PackageCertificateKeyFile=$CertificatePath", "-p:PackageCertificatePassword=$CertificatePassword")
}
$out = Join-Path $root 'artifacts\'
$manifest = Join-Path $root 'src\Hotline.App\Package.appxmanifest'

if (-not $Version) {
    $now = Get-Date
    $Version = "0.1.$($now.DayOfYear).$([int]($now.TimeOfDay.TotalSeconds / 2))"
}
$original = [IO.File]::ReadAllText($manifest)
try {
    $patched = $original -replace '(<Identity [^>]*Version=")[^"]+', "`${1}$Version"
    if ($Publisher) { $patched = $patched -replace '(<Identity [^>]*Publisher=")[^"]+', "`${1}$([Security.SecurityElement]::Escape($Publisher))" }
    [IO.File]::WriteAllText($manifest, $patched)
    dotnet publish (Join-Path $root 'src\Hotline.App\Hotline.App.csproj') -c $Configuration -r win-x64 `
        -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true @signing -p:AppxPackageDir="$out" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}
finally {
    [IO.File]::WriteAllText($manifest, $original)
}

# Keep the three newest package folders; every dev build is ~90 MB and they piled up to gigabytes.
Get-ChildItem $out -Directory -Filter 'Hotline.App_*' | Sort-Object LastWriteTime -Descending | Select-Object -Skip 3 |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

Get-ChildItem $out -Recurse -Filter "*_$($Version)_*.msix" | Select-Object -Last 1 -ExpandProperty FullName
