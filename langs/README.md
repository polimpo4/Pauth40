# Pauth40 translations

Every text shown by Pauth40 lives in a plain JSON file in this folder. You can
fix a typo, improve a translation or add a completely new language **without
touching the C# code and without rebuilding the application**.

The reference language is **European Portuguese (`pt-PT`)**. `en-US` is the
built-in fallback.

## Files

| File | Language |
| --- | --- |
| `pt-PT.json` | European Portuguese (reference / base language) |
| `en-US.json` | US English |

When the app starts it loads the languages from:

1. the files embedded in `Pauth40.exe` (always available, used as a safety net);
2. the `langs` folder next to `Pauth40.exe`;
3. `%LOCALAPPDATA%\Pauth40\langs` (or `%PAUTH40_DATA_DIR%\langs` when that
   environment variable is set).

A file on disk replaces the embedded file with the same `code`, so editing a
JSON file is enough to change the interface. Files 2 and 3 only need to contain
the language you are working on; they can be copied to an existing installation.

## File format

```json
{
  "code": "pt-PT",
  "name": "Portuguese (Portugal)",
  "nativeName": "Português (Portugal)",
  "strings": {
    "app_title": "Pauth40 - Autenticador de dois fatores",
    "main_copy": "Copiar código para a área de transferência"
  }
}
```

| Field | Meaning |
| --- | --- |
| `code` | BCP-47 language tag. It must be unique: it is the identifier saved in the user preference and the name used to pick the language. Also used for `CultureInfo`, so prefer a real tag such as `fr-FR`, `de-DE` or `es-ES`. |
| `name` | Language name in English, shown in the language selector and in the README. |
| `nativeName` | Language name in the language itself, shown in the selector. This is what users see, so keep it accurate (`Français (France)`, not `French`). |
| `strings` | Flat key/value dictionary. Keys are stable identifiers: **never translate or rename a key**. |

Use UTF-8. Indentation, key order and blank lines are free; comments and
trailing commas are also accepted, although keeping the files plain JSON makes
them easier to validate with other tools.

## Keys

Keys are lowercase and use underscores, grouped by prefix:

| Prefix | Used for |
| --- | --- |
| `app_` | Application title. |
| `common_` | Shared labels and window buttons (Close, Save, Password, Port, ...). |
| `main_` | Main window: code labels, Copy/Capture buttons, empty state. |
| `toast_` | Temporary notifications at the bottom of the window. |
| `account_` | Authenticator name and account subtitle. |
| `security_` | First-run setup, unlock and password recovery screens. |
| `settings_` | Settings window and the export/import dialogs. |
| `email_` | Subject and body of recovery/test e-mails. |
| `error_` | Service errors: vault, password, backup, QR parsing, SMTP. |
| `update_` | Mandatory update window, progress and updater errors. |

The complete list is the `strings` object of `pt-PT.json` (currently 188 keys).
Use it as the source of truth.

## Placeholders

Some strings contain numbered placeholders such as `{0}` and `{1}`. They are
replaced at runtime and **must be kept** in the translation, in any order:

```json
"toast_account_added": "Added {0}.",
"account_subtitle": "TOTP  ·  {0} digits  ·  {1}s"
```

- Do not translate `{0}` or `{1}`.
- Do not add `{2}` unless the base string already has it.
- A broken placeholder will not crash the app (the raw text is shown instead),
  but the value will be missing from the sentence.

`security_status_sent` uses two placeholders (masked e-mail and seconds) and
`toast_no_qr_in_source` uses one (the source folder or file name).

## Fallback behaviour

Missing keys never leave a blank label. For every key the app tries, in order:

1. the selected language;
2. European Portuguese (`pt-PT`);
3. US English (`en-US`);
4. the key itself (so an untranslated string is easy to spot).

This means a new language does not have to be complete on day one. Start with
the most visible keys and fill in the rest over time.

## Adding a language

1. Copy `en-US.json` (or `pt-PT.json`) to `<code>.json`, for example
   `fr-FR.json`.
2. Change `code`, `name` and `nativeName`.
3. Translate the values, keeping the keys and placeholders untouched.
4. Drop the file in `langs` next to `Pauth40.exe` and restart the app. It
   appears in the selector immediately.

Do not rename `pt-PT.json` or `en-US.json`: they are the reference and fallback
languages.

## Choosing the language

- On first run the app follows the Windows UI language: Portuguese Windows gets
  `pt-PT`, anything else gets `en-US` when available.
- The selector is available in the security screen at startup (setup, unlock and
  recovery) and later in **Settings → Language**.
- The choice is saved to `%LOCALAPPDATA%\Pauth40\language.json` (or
  `%PAUTH40_DATA_DIR%\language.json`) and applied on the next start.
- Switching language updates open windows immediately; no restart is required.

## Contributing

1. Fork the repository and create a branch.
2. Edit an existing file or add a new `<code>.json`.
3. Check that the file is valid JSON and that both files still have the same
   keys. With PowerShell:

   ```powershell
   $pt = (Get-Content .\langs\pt-PT.json -Raw | ConvertFrom-Json).strings
   $xx = (Get-Content .\langs\fr-FR.json -Raw | ConvertFrom-Json).strings
   Compare-Object $pt.PSObject.Properties.Name $xx.PSObject.Properties.Name
   ```

   No output means the key lists match. Missing keys are still allowed thanks to
   the fallback, but the comparison makes omissions explicit.
4. Run the app and check the main window, the security screen and Settings in the
   new language.
5. Open a pull request describing the language and the strings you changed.

Small fixes (typos, wording) are welcome as a pull request too. Please keep the
tone consistent with the rest of the interface: short, direct and neutral.
