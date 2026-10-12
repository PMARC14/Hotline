# Translating Hotline

Hotline's interface ships in 17 languages. English is the source; the other 16 are **model-drafted and not yet
reviewed by native speakers**, so corrections are very welcome.

| Language | Tag | Status |
|---|---|---|
| English | `en-US` | source |
| Deutsch | `de` | needs native review |
| Español | `es` | needs native review |
| Français | `fr` | needs native review |
| Italiano | `it` | needs native review |
| Português (Brasil) | `pt-BR` | needs native review |
| Nederlands | `nl` | needs native review |
| Polski | `pl` | needs native review |
| Українська | `uk` | needs native review |
| Türkçe | `tr` | needs native review |
| 日本語 | `ja` | needs native review |
| 한국어 | `ko` | needs native review |
| 简体中文 | `zh-Hans` | needs native review |
| 繁體中文 | `zh-Hant` | needs native review |
| हिन्दी | `hi` | needs native review |
| Bahasa Indonesia | `id` | needs native review |
| Tiếng Việt | `vi` | needs native review |

Right-to-left languages (Arabic, Hebrew) aren't shipped yet: the panel's layout needs a mirrored check first.

## Where the text is

- `src/Hotline.Core/Text/Localization/Strings.resx`: every English text, one `<data name="Key">` each.
  `Strings.<tag>.resx` holds the same keys for one language.
- `src/Hotline.App/Strings/<tag>/Resources.resw`: the one-line description in the Start menu (`AppDescription`).
  Its folders also tell Windows and the Store which languages the package has.
- `store/listings/<tag>.md`: the Microsoft Store listing text to paste into Partner Center.

## Fixing a translation

Edit the `<value>` in `Strings.<tag>.resx` and open a pull request. Keep:

- every `{0}`, `{1}`, `{1:g}` placeholder (the tests fail if one is missing or extra);
- `\n` line breaks where the English has them;
- names and commands as they are: Hotline, Claude Code, agy, `/remember`, `mcp.json`, `settings.json`, key
  names such as Ctrl+Alt+H (the settings parse them in English);
- Windows' own wording for its settings pages (for example the path to *Online speech recognition*), so people can
  find them.

`dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj` checks that every language has every key,
the same placeholders, and formats.

## Adding a language

1. Copy `Strings.resx` to `Strings.<tag>.resx` and translate every `<value>`.
2. Add `src/Hotline.App/Strings/<tag>/Resources.resw` with `AppDescription`.
3. Add the tag to `Languages.Supported` (`src/Hotline.Core/Text/Localization/Languages.cs`) and to the table above.
4. Optionally add `store/listings/<tag>.md`.

## Checking the layout

`scripts/pseudo-loc.sh` writes a pseudo-language (accented, ~40% longer, in brackets). Rebuild, set
`"general": { "language": "qps-ploc" }` in settings.json and restart Hotline: plain English is text that was missed,
and a cut-off `]` is text that doesn't fit. `scripts/pseudo-loc.sh --remove` deletes it again.
