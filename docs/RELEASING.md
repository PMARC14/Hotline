# Releasing Hotline

## Why MSIX (and no Inno/WiX installer)

Windows only lists **packaged** apps in the Copilot key picker, so Hotline needs package identity: a full MSIX (what we ship) or a
sparse MSIX from a classic installer, both signed with a certificate the PC trusts. A classic installer would add a wizard and no relief.

## How a release is built (GitHub Actions)

- **CI** (`.github/workflows/ci.yml`): unit tests + app build on every pull request and every push to `main`.
- **Release** (`.github/workflows/release.yml`): every push to `main` that changes code publishes a release
  `v<major.minor>.<patch>`: major.minor from `version.txt`, patch the next number in that line from the existing tags
  (`scripts/next-version.sh`; after `v0.2.25` comes `v0.2.26`, and bumping `version.txt` to `0.3` starts at
  `v0.3.0`). A pushed tag `vX.Y.Z` or **Run workflow** with a version publishes that version instead. The Store package
  is the same version plus `.0`; the Store only needs each submission higher than the last (compared as a whole) with
  the fourth part `0`. To start a new release line (0.6 to 0.7), bump `version.txt`; **1.0** comes when the Road to 1.0
  ([HANDOFF.md](HANDOFF.md)) is done and a Store update with all 17 languages is certified.
  - With the secrets `HOTLINE_SIGNING_PFX_BASE64` / `HOTLINE_SIGNING_PFX_PASSWORD` (subject must equal the manifest
    `Publisher`): a normal release. **Set since 2026-10-15** with a self-signed certificate valid until 2036-10-12
    (`CN=pmarc14 Hotline Dev`; the owner keeps `certs/release/hotline-release.pfx` and its password, git-ignored, and a
    backup). Users trust `Hotline.cer` once; `.appinstaller` updates then work across releases. To replace it, set the
    two secrets again (every user then trusts the new `Hotline.cer`).
  - Without them: signed with a throwaway self-signed certificate, still published as a normal release (since
    v0.2.24; earlier ones were pre-releases) with notes on trusting `Hotline.cer`. Each such release has a new
    certificate, so users trust each one's `Hotline.cer` and `.appinstaller` updates don't carry across releases;
    automatic updates need one stable certificate (a `.pfx` above, or Azure Artifact Signing).
  - Assets: the `.msixbundle` (x64 and ARM64; Windows installs the right one), `Hotline.cer` and `Hotline.appinstaller` (`scripts/make-appinstaller.sh`).
- **winget:** covered by the Store. Its `msstore` source lists every Store app, so `winget install 9PMW5CR6G3BC`
  installs Hotline AI and `winget upgrade` updates it, with nothing to submit. `winget show` lists its version as
  *Unknown*: the Store source doesn't publish versions for Store apps (same for every msstore app; not fixable here). (`.github/workflows/winget.yml`, for a
  `winget-pkgs` entry of the GitHub builds, stays disabled: winget-pkgs rejects self-signed packages.)

Users then either double-click the `.msixbundle`, or open `Hotline.appinstaller` (downloaded) — same install, plus Windows
checks `releases/latest/download/Hotline.appinstaller` once a day and updates in the background. Updates keep
settings (`%USERPROFILE%\.hotline`) and the Copilot key choice (setup: [copilot-key.md](copilot-key.md)).

Notes: Windows disabled the `ms-appinstaller:` web link by default, so link to the file
itself and let users open it after downloading. The package version (`MAJOR.MINOR.PATCH.0`) must increase every
release: the update check compares versions.

## Still open

- [ ] **Protect `main`** (rulesets need a public repo or GitHub Pro): `scripts/apply-rulesets.sh` applies `.github/rulesets/main.json`
      (changes only through a pull request whose CI check `build-test` passed, no force pushes, no deleting `main`, review threads
      resolved, no approval needed). Edit the JSON and re-run to change it.
- [ ] **Trusted signing** instead of the self-signed certificate (users trust `Hotline.cer` once today): the Store (done: Microsoft
      signs it), Azure Artifact Signing (below) or a regular code-signing `.pfx` in the two `HOTLINE_SIGNING_PFX_*` secrets.
- [ ] **Final icon** (the owner's SVG of an upside-down phone hanging by its cord) via `scripts\make-assets.ps1 -Source`.
- [ ] **README:** fresh screenshots (`hotline://demo`, blur anything personal).

## Microsoft Store

The usual route is the workflow below. To build locally instead: Partner Center › your app › Product management › Product identity
gives four values; the package is unsigned because the Store signs it, and Store versions must end in `.0`:

```bash
scripts/build-msix.sh --store --arch all --version 0.6.0.0 \
  --identity-name "<Package/Identity/Name>" --publisher "<Package/Identity/Publisher>" \
  --publisher-display-name "<Package/Properties/PublisherDisplayName>" --display-name "Hotline AI"
```

Upload the `.msixbundle` it prints (x64 + ARM64; the Store gives each PC the right one) on the submission's **Packages** page.

Store images in every language: install the build to capture (`scripts/install.sh`), run `scripts/make-store-media.sh --no-video`
(shows the panel on screen for about 40 seconds: don't use the PC), then `scripts/make-store-localized.sh` (headless). Pictures for
each language land in `artifacts/store/<lang>/`; upload them on that language's listing. Captions come from
`store/listings/<lang>.md`, poster/box text from `store/art/taglines.json`.
`--display-name` must be a name reserved for the app; the panel and tray still say "Hotline".

### Store workflow (`.github/workflows/store.yml`)

**How Store updates are released: by hand.** Run **Actions › Store › Run workflow** with a version (`0.6.0` →
package `0.6.0.0`) and *Submit* off (or push a tag `vX.Y.Z`). It builds the Store `.msixbundle` with the identity
above and attaches it to the run; download it from the run's *Artifacts* and upload it in a Partner Center
submission (**Packages** page), then paste the listing text from `store/listing.md` and `store/listings/` (17 languages).
Store ID: `9PMW5CR6G3BC`. The 0.6.0.0 bundle is built and waits for the owner's upload.

Automatic submission is **not set up, on purpose**: Partner Center only adds an Entra app from a work account
(a personal Microsoft account is refused), which means a separate admin user in an Entra tenant just for this. If
that ever becomes worth it, the one-time setup is below (msstore only updates a published app and can't submit
while another submission is in certification):

1. **Entra app:** [Entra admin center](https://entra.microsoft.com) › *App registrations › New registration* (e.g.
   "hotline-store"). Copy the *Application (client) ID* and the *Directory (tenant) ID*. Under *Certificates & secrets*
   create a client secret and copy its **value** (shown once; it expires, so note the date).
2. **Partner Center:** *Account settings › User management › Microsoft Entra applications › Add* that app with the
   **Manager** role. *Account settings › Legal info / Developer settings* shows the **Seller ID**. The app's **Store ID**
   (`9N…`) is under *Product identity*. (If Partner Center has no tenant yet, associate one first: *Account settings ›
   Tenants*.)
3. **GitHub:** repository *Settings › Secrets and variables › Actions*: secrets `AZURE_AD_TENANT_ID`,
   `AZURE_AD_APPLICATION_CLIENT_ID`, `AZURE_AD_APPLICATION_SECRET`, `SELLER_ID`; variables `STORE_PRODUCT_ID` (the Store
   ID) and `HOTLINE_STORE_SUBMIT` = `true`.

The submission copies the live listing and only replaces the package. For an update that also changes the listing
(text, screenshots, "What's new") or the declarations, e.g. the first update with donations on, run the workflow with
*Submit* off and upload the artifact by hand in a Partner Center submission where you make those changes too.

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
with `Get-AuthenticodeSignature Hotline_*.msixbundle` (Status *Valid*, signer = your name).

**Identity change:** the `Publisher` is part of the package identity, so the first trusted build installs *next to* a
self-signed one rather than updating it. Uninstall the old Hotline first (settings in `%USERPROFILE%\.hotline` stay)
and pick Hotline for the Copilot key again. Local dev builds keep the dev publisher (`CN=pmarc14 Hotline Dev`).
