# Releasing Hotline

## Today: signed MSIX (sideloaded)

1. Merge to `main` through a PR; CI (`.github/workflows/ci.yml`) builds and runs the unit tests (manual runs any time;
   automatic once the repository variable `HOTLINE_ACTIONS_ENABLED=true`).
2. Store the signing certificate as secrets (`HOTLINE_SIGNING_PFX_BASE64`, `HOTLINE_SIGNING_PFX_PASSWORD`). Its subject
   must equal the manifest `Publisher` in `src/Hotline.App/Package.appxmanifest`.
3. Push a tag `vX.Y.Z` (or run **Release** manually). The workflow tests, builds a signed `.msix`, and publishes a
   GitHub Release with the `.msix` and `Hotline.cer`.
4. Users of a self-signed build trust `Hotline.cer` (Local Machine → Trusted People) once, then double-click the
   `.msix`. Then: Settings → Personalization → Text input → Customize Copilot key → Custom → Hotline.

## Before the first public release

- [ ] **Signing users won't have to trust by hand:** Azure Trusted Signing (or a code-signing certificate) — change the
      manifest `Publisher` to the certificate subject, update the secrets. Alternatively the Microsoft Store (free
      signing, automatic updates; Store policies apply to the Copilot-key extension and full trust).
- [ ] **Installer that feels normal (sparse package):** keep package identity — required for the Copilot key picker —
      without a full MSIX install: ship a regular installer (e.g. Inno Setup / WiX) that copies the app to
      `%LOCALAPPDATA%\Programs\Hotline` and registers a *sparse package* ("packaged with external location": an
      identity-only `.msix` with `uap10:AllowExternalContent`, registered via `PackageManager.AddPackageByUriAsync`
      with `ExternalLocationUri`). The manifest keeps the `com.microsoft.windows.copilotkeyprovider` extension, the
      `hotline:` protocol and the startup task. Unpackaged → sparse migration: the data folder (`%USERPROFILE%\.hotline`)
      doesn't change; credentials in Credential Locker keep working.
- [ ] **Final icon** (the user's SVG of an upside-down phone hanging by its cord) → `scripts\make-assets.ps1 -Source`.
- [ ] **Versioning:** `vMAJOR.MINOR.PATCH` tags; the package version is `MAJOR.MINOR.PATCH.0`.
- [ ] **README:** fresh screenshots (`hotline://demo`, blur anything personal), install steps for the installer.
- [x] **License check:** Apache-2.0 for Hotline; third-party: Windows App SDK (Microsoft license, see its license.txt), Markdig (BSD-2), Anthropic SDK
      (MIT), ModelContextProtocol SDK (Apache-2.0), Fluent Emoji placeholder icon (MIT) — listed in `THIRD-PARTY-NOTICES.md`.
- [ ] Turn on `HOTLINE_ACTIONS_ENABLED` so every PR is built and tested.
