# Releasing Hotline

## Why MSIX (and no Inno/WiX installer)

Windows only lists **packaged** apps in the Copilot key picker, so Hotline needs package identity. The only ways to
get it are a full MSIX (what we ship) or a *sparse* MSIX registered by a classic installer — and both must be signed
with a certificate the PC trusts. A classic installer would add a wizard and a Program Files folder, not remove the
signing requirement, so we ship the MSIX directly. Double-clicking a signed `.msix` opens Windows' App Installer
(an Install button); an unpackaged build could only offer the fallback hotkey, not the Copilot key.

## How a release is built (GitHub Actions)

- **CI** (`.github/workflows/ci.yml`): unit tests + app build on every pull request and every push to `main`.
- **Release** (`.github/workflows/release.yml`): every push to `main` that changes code publishes a release
  `v<version.txt>.<run number>` (e.g. `v0.2.57`); a pushed tag `vX.Y.Z` or **Run workflow** with a version publishes
  that version. Bump `version.txt` (e.g. `0.3`, `1.0`) for a new minor/major line.
  - With the secrets `HOTLINE_SIGNING_PFX_BASE64` / `HOTLINE_SIGNING_PFX_PASSWORD` (subject must equal the manifest
    `Publisher`): a normal release.
  - Without them: signed with a throwaway self-signed certificate and published as a **pre-release** test build.
    Each test build has a new certificate, so testers trust that build's `Hotline.cer` again. `.appinstaller` updates
    don't apply to test builds, because GitHub's `releases/latest` link skips pre-releases. Automatic updates start
    with the first trusted-signed release (first one: `v0.2.1`, 2026-10-04).
  - Assets: the `.msix`, `Hotline.cer` and `Hotline.appinstaller` (`scripts/make-appinstaller.ps1`).
- **winget** (`.github/workflows/winget.yml`): disabled until the repository variable `HOTLINE_WINGET_ENABLED=true`;
  needs trusted signing, a first manual submission (`wingetcreate new`) and a `WINGET_TOKEN` secret (details in the file).

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
- [ ] **winget** (after signing): submit the first version by hand (`wingetcreate new`), add `WINGET_TOKEN`, set
      `HOTLINE_WINGET_ENABLED=true` — later releases are submitted automatically.
- [ ] **Final icon** (the user's SVG of an upside-down phone hanging by its cord) → `scripts\make-assets.ps1 -Source`.
- [ ] **README:** fresh screenshots (`hotline://demo`, blur anything personal). Install steps from Releases are in.
- [x] **License check:** Apache-2.0 for Hotline; third-party components listed in `THIRD-PARTY-NOTICES.md` (licenses
      taken from the package metadata).
