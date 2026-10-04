# Releasing Hotline

## Why MSIX (and no Inno/WiX installer)

Windows only lists **packaged** apps in the Copilot key picker, so Hotline needs package identity. The only ways to
get it are a full MSIX (what we ship) or a *sparse* MSIX registered by a classic installer — and both must be signed
with a certificate the PC trusts. A classic installer would add a wizard and a Program Files folder, not remove the
signing requirement, so we ship the MSIX directly. Double-clicking a signed `.msix` opens Windows' App Installer
(an Install button); an unpackaged build could only offer the fallback hotkey, not the Copilot key.

## How a release is built

1. Merge to `main` through a PR; CI (`.github/workflows/ci.yml`) builds and runs the unit tests (manual runs any time;
   automatic once the repository variable `HOTLINE_ACTIONS_ENABLED=true`).
2. Signing certificate as secrets: `HOTLINE_SIGNING_PFX_BASE64`, `HOTLINE_SIGNING_PFX_PASSWORD`. Its subject must
   equal the manifest `Publisher` in `src/Hotline.App/Package.appxmanifest`.
3. Push a tag `vX.Y.Z` (or run **Release** manually). The workflow tests, builds the signed `.msix`
   (`scripts/build-msix.ps1`), writes `Hotline.appinstaller` (`scripts/make-appinstaller.ps1`) and publishes a GitHub
   Release with the `.msix`, `Hotline.cer` and `Hotline.appinstaller`.

Users then either double-click the `.msix`, or open `Hotline.appinstaller` (downloaded) — same install, plus Windows
checks `releases/latest/download/Hotline.appinstaller` once a day and updates in the background. Updates keep
settings (`%USERPROFILE%\.hotline`) and the Copilot key choice. After installing: Settings → Personalization → Text
input → Customize Copilot key → Custom → Hotline.

Notes: release downloads of a **private** repository need a GitHub login, so the `.appinstaller` update check only
works once the repository is public. Windows disabled the `ms-appinstaller:` web link by default, so link to the file
itself and let users open it after downloading. Package version = `MAJOR.MINOR.PATCH.0` and must increase every
release (the update check compares versions).

## Before the first public release

- [ ] **Trusted signing** (the one real blocker — today's self-signed build makes each user trust `Hotline.cer` in
      Local Machine → Trusted People first, fine for a few testers only). Pick one:
  - **Microsoft Store:** Microsoft signs it, Store installs and updates. Needs a Partner Center developer account and
    certification (expect questions about full trust and the Copilot key extension).
  - **Azure Trusted Signing** (or a regular code-signing certificate): set the manifest `Publisher` to the
    certificate subject, update the two secrets; ship via GitHub Releases + `.appinstaller`.
- [ ] **winget** (optional, after signing): a manifest PR to `microsoft/winget-pkgs` pointing at the release `.msix`.
- [ ] **Final icon** (the user's SVG of an upside-down phone hanging by its cord) → `scripts\make-assets.ps1 -Source`.
- [ ] **README:** fresh screenshots (`hotline://demo`, blur anything personal), install steps for signed releases.
- [x] **License check:** Apache-2.0 for Hotline; third-party components listed in `THIRD-PARTY-NOTICES.md` (licenses
      taken from the package metadata).
- [ ] Turn on `HOTLINE_ACTIONS_ENABLED` so every PR is built and tested.
