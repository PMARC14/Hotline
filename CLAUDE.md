# Working on Hotline

Start with `docs/HANDOFF.md` (state, next work, architecture, hard-won facts). Machine-specific notes, if any, are in
`docs/local/` (git-ignored).

## Rules

- **Branches:** never commit to `main` directly. Branch from `origin/main`, push as you go, merge through a PR.
- **Commits:** author `pmarc14 <16502495+PMARC14@users.noreply.github.com>`; end messages with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (or the current model's trailer).
- **Tests:** TDD in `src/Hotline.Core`; run
  `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj` and, for app changes,
  `powershell -File tests\smoke\smoke.ps1 -Install` (needs the dev certificate; see README).
- **Testing on the user's PC:** leave the Hotline panel and windows **closed** afterwards; don't send keystrokes, use
  the clipboard or open visible windows without asking. Prefer `hotline://selftest` (off-screen rendering).
- **Settings are files** under `%USERPROFILE%\.hotline`, applied live; never overwrite a user's file without a backup.
- **Security:** API keys only in Credential Locker and only over https/loopback; dangerous CLI flags only through the
  explicit `approveAllTools` setting; sanitize anything from a model before opening it outside Hotline.
- **Claude API work:** load the `claude-api` skill first; the Anthropic backend uses the official C# SDK.
- **Reviews:** after a feature, run an adversarial review (Antigravity MCP with a diff file in `.superpowers/review/`,
  or a Sonnet subagent), verify each finding against the code, fix the real ones.
