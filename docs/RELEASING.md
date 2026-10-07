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

- [ ] **Protect `main`** right after making the repository public (rulesets on a private repo need GitHub Pro):
      `powershell -File scriptspply-rulesets.ps1` applies `.github/rulesets/main.json` — changes only through a
      pull request whose CI check (`build-test`) passed, no force pushes, no deleting `main`; review threads must be
      resolved; no approval needed (you're the only maintainer). Edit the JSON and re-run to change it.

- [ ] **Trusted signing** (the one real blocker — today's self-signed build makes each user trust `Hotline.cer` in
      Local Machine → Trusted People first, fine for a few testers only). Pick one:
  - **Microsoft Store:** Microsoft signs it, Store installs and updates. Needs a Partner Center developer account and
    certification (expect questions about full trust and the Copilot key extension).
  - **Azure Artifact Signing** (formerly Trusted Signing; $9.99/month): automated in `release.yml`, see below.
  - A regular code-signing certificate (.pfx): the two `HOTLINE_SIGNING_PFX_*` secrets.

## Microsoft Store (by hand, until a submission workflow exists)

Partner Center › your app › Product management › Product identity gives four values. Build with them; the package is
unsigned because the Store signs it, and Store versions must end in `.0`:

```powershell
powershell -File scriptsuild-msix.ps1 -Store -Version 0.2.3.0 `
  -IdentityName "<Package/Identity/Name>" -Publisher "<Package/Identity/Publisher>" `
  -PublisherDisplayName "<Package/Properties/PublisherDisplayName>" -DisplayName "Hotline AI"
```

Upload the `.msix` it prints on the submission's **Packages** page. Each new submission needs a higher version than
the last one. `-DisplayName` must be a name reserved for the app; the panel and tray still say "Hotline".

## Azure Artifact Signing (automatic signing in GitHub Actions)

Individuals can sign up only in the **US and Canada** (identity checked with Microsoft Entra Verified ID: a government
ID and a selfie); elsewhere it takes an organization with a verifiable business history. One-time setup, in the
[Azure portal](https://portal.azure.com):

1. **Subscription:** a pay-as-you-go Azure subscription. Under *Resource providers*, register `Microsoft.CodeSigning`.
2. **Account:** create an *Artifact Signing* account (Basic tier) in a nearby region. Its *Account URI* is the endpoint
   (e.g. `https://eus.codesigning.azure.net/`).
3. **Identity validation:** give yourself the *Artifact Signing Identity Verifier* role on the account, then
   *Identity validations → New → Public → Individual* and complete the Verified ID check (can take a few days).
4. **Certificate profile:** *Certificate profiles → New → Public Trust*, using that validation. Copy the certificate
   **subject** exactly (`CN=Your Name, O=Your Name, L=City, S=Province, C=CA`) — it becomes the package `Publisher`.
5. **GitHub access without secrets:** *Microsoft Entra ID → App registrations → New* (e.g. "hotline-signing"); under
   *Certificates & secrets → Federated credentials* add *GitHub Actions*: organization `PMARC14`, repository
   `hotline`, entity *Branch* `main` (add *Tag* `v*` too if you release from tags). Then on the signing account (or the
   profile) give that app the **Artifact Signing Certificate Profile Signer** role.
6. **Repository variables** (GitHub → Settings → Secrets and variables → Actions → *Variables*; none are secret):
   `ARTIFACT_SIGNING_ENDPOINT`, `ARTIFACT_SIGNING_ACCOUNT`, `ARTIFACT_SIGNING_PROFILE`, `HOTLINE_PUBLISHER` (the
   subject from step 4), `AZURE_CLIENT_ID` (the app), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.

From then on every release is built unsigned with that `Publisher`, signed by Azure (`azure/artifact-signing-action`),
timestamped, and published as a normal release — no `Hotline.cer`, and `.appinstaller` updates work. Check a release
with `Get-AuthenticodeSignature Hotline_*.msix` (Status *Valid*, signer = your name).

**Identity change:** the `Publisher` is part of the package identity, so the first trusted build installs *next to* a
self-signed one rather than updating it. Uninstall the old Hotline first (settings in `%USERPROFILE%\.hotline` stay)
and pick Hotline for the Copilot key again. Local dev builds keep the dev publisher (`CN=pmarc14 Hotline Dev`).
- [ ] **winget** (after signing): submit the first version by hand (`wingetcreate new`), add `WINGET_TOKEN`, set
      `HOTLINE_WINGET_ENABLED=true` — later releases are submitted automatically.
- [ ] **Final icon** (the user's SVG of an upside-down phone hanging by its cord) → `scripts\make-assets.ps1 -Source`.
- [ ] **README:** fresh screenshots (`hotline://demo`, blur anything personal). Install steps from Releases are in.
- [x] **License check:** Apache-2.0 for Hotline; third-party components listed in `THIRD-PARTY-NOTICES.md` (licenses
      taken from the package metadata).
