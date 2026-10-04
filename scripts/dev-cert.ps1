# Creates a self-signed code-signing cert matching the manifest Publisher and trusts it
# for MSIX sideloading (LocalMachine\TrustedPeople needs one UAC prompt). Dev only.
# The .pfx gets a random password, saved next to it in certs\hotline-dev.password (certs\ is git-ignored);
# build-msix.ps1 reads it from there.
$ErrorActionPreference = 'Stop'
$subject = 'CN=pmarc14 Hotline Dev'
$root = Split-Path $PSScriptRoot -Parent
$certDir = Join-Path $root 'certs'
New-Item -ItemType Directory -Force $certDir | Out-Null
$pfx = Join-Path $certDir 'hotline-dev.pfx'
$cer = Join-Path $certDir 'hotline-dev.cer'

$cert = New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature `
    -FriendlyName 'Hotline Dev Signing' -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(3)
$bytes = New-Object byte[] 24
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes) # works in Windows PowerShell 5.1
$password = [Convert]::ToBase64String($bytes)
[IO.File]::WriteAllText((Join-Path $certDir 'hotline-dev.password'), $password)
$pw = ConvertTo-SecureString $password -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pw | Out-Null
Export-Certificate -Cert $cert -FilePath $cer | Out-Null

Start-Process powershell -Verb RunAs -Wait -ArgumentList @(
    '-NoProfile', '-Command',
    "Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null")
Write-Host "Created and trusted $subject ($($cert.Thumbprint))"
