# Working on Hotline

Start with `docs/HANDOFF.md` (state, next work, architecture, rules, hard-won facts). Machine notes, if any, are in
`docs/local/` (git-ignored).

## Rules

- **Branches:** never commit to `main` directly. Branch from `origin/main`, push as you go, merge through a PR.
- **Commits:** author `pmarc14 <16502495+PMARC14@users.noreply.github.com>`; no co-author trailer or generated-by footer
  unless asked on that item.
- **Tests:** TDD in `src/Hotline.Core`; run `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj` and,
  for app changes, `powershell -File tests\smoke\smoke.ps1 -Install` (needs the dev certificate in `certs/`; see README).
- **Testing on the owner's PC:** leave the panel and windows closed afterwards; never send keystrokes, use the clipboard or
  open visible windows without asking. Prefer `hotline://selftest` (off-screen).
- **Settings are files** under `%USERPROFILE%\.hotline`, applied live; never overwrite one without a backup set.
- **Security:** API keys only in Credential Locker and only over https or loopback; dangerous CLI flags only through
  `approveAllTools`; sanitize anything from a model before opening it outside Hotline.
- **Claude API work:** load the `claude-api` skill first; the Anthropic backend uses the official C# SDK.
- **Reviews:** after a feature, an adversarial review by another vendor's model; verify every finding against the code and fix
  the real ones with a test.
- **Public repo:** no personal details (names, emails, machine paths, private folders) in tracked files; those go in
  `docs/local/`.
