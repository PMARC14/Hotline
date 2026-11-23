# Releasing Hotline

## Why MSIX

Windows only lists packaged apps in the Copilot key picker, so Hotline ships as a signed MSIX (`x64` + `ARM64` `.msixbundle`).

## Versions, CI, and GitHub releases

- **CI** (`.github/workflows/ci.yml`): builds and runs unit tests on every pull request and every push to `main`.
- **Release** (`.github/workflows/release.yml`): every code-changing push to `main` builds and publishes a GitHub prerelease `v<major.minor>.<patch>`:
  - `major.minor` comes from `version.txt`, and `scripts/next-version.sh` picks the next patch from existing tags.
  - Pushing a `vX.Y.Z` tag or running **Actions › Release › Run workflow** with a version publishes that exact version.
  - Assets: `Hotline_<version>.0_*.msixbundle`, `Hotline.cer`, and `Hotline.appinstaller` (`scripts/make-appinstaller.sh`).
  - Updates keep `%USERPROFILE%\.hotline` and the Copilot key choice ([Copilot key setup](../README.md#copilot-key-setup)).
- **Promote** (`.github/workflows/promote.yml`): run **Actions › Promote › Run workflow** with a prerelease tag (e.g. `v0.7.0`) to mark it stable (`gh release edit <tag> --prerelease=false --latest`) without rebuilding.
  - `Hotline.appinstaller` checks `releases/latest/download/Hotline.appinstaller` once a day; GitHub excludes prereleases, so only promotion advances stable.
- **winget**: handled automatically by the Microsoft Store `msstore` source (`winget install 9PMW5CR6G3BC` / `winget upgrade`). `.github/workflows/winget.yml` stays disabled because `winget-pkgs` rejects self-signed packages.

> **Versioning & links:** Package versions (`MAJOR.MINOR.PATCH.0`) must strictly increase on every release. Windows disables `ms-appinstaller:` web links by default, so link directly to the `.appinstaller` file for download.

## Signing

`release.yml` uses the first configured signing method (otherwise publishing fails; throwaway certs are allowed only on `workflow_dispatch` with `publish` off):

1. **Self-signed `.pfx` (current):** repository secrets `HOTLINE_SIGNING_PFX_BASE64` and `HOTLINE_SIGNING_PFX_PASSWORD` (subject matching the manifest `Publisher`, `CN=pmarc14 Hotline Dev`, valid until 2036-10-12). Users trust `Hotline.cer` once; replacing the certificate requires users to trust the new `Hotline.cer`.
2. **Azure Artifact Signing (optional):** for US/Canada individuals (Entra Verified ID) or organizations.
   - Register `Microsoft.CodeSigning`, create a Basic *Artifact Signing* account, complete identity validation, and create a *Public Trust* certificate profile.
   - Create an Entra app with OIDC federated credentials (`main` branch and `v*` tags) and grant it the *Artifact Signing Certificate Profile Signer* role.
   - Set repository variables: `ARTIFACT_SIGNING_ENDPOINT`, `ARTIFACT_SIGNING_ACCOUNT`, `ARTIFACT_SIGNING_PROFILE`, `HOTLINE_PUBLISHER` (exact certificate subject), `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.
   - Verify signed bundles with `Get-AuthenticodeSignature Hotline_*.msixbundle`.

**Identity change:** changing `Publisher` changes package identity, so the first build under a new publisher installs alongside a self-signed build. Uninstall the old build first (`.hotline` files stay) and re-select Hotline in Windows' Copilot key settings.

## Microsoft Store (manual submission)

Store updates are built in GitHub Actions and uploaded to Partner Center by hand (Store ID `9PMW5CR6G3BC`).

1. **Build the bundle:** run **Actions › Store › Run workflow** (`.github/workflows/store.yml`) with a three-part version (e.g. `0.7.0` → Store package `0.7.0.0`) and **Submit** off, or push a `vX.Y.Z` tag. Download the `hotline-store-<version>.0` artifact (`HotlineAI_<version>.0_Store.msixbundle`).
   - Local equivalent (unsigned; the Store signs it):
     ```bash
     scripts/build-msix.sh --store --arch all --version 0.7.0.0 \
       --identity-name "<Package/Identity/Name>" --publisher "<Package/Identity/Publisher>" \
       --publisher-display-name "<Package/Properties/PublisherDisplayName>" --display-name "Hotline AI"
     ```
2. **Optional localized Store images:** install the build (`scripts/install.sh`), run `scripts/make-store-panels.sh`, then `scripts/make-store-localized.sh` (outputs to `artifacts/store/<lang>/`).
3. **Submit in Partner Center:** follow [store/listing.md](../store/listing.md) to upload the `.msixbundle`, update listing copy (`store/listings/<tag>.md`), and submit.
