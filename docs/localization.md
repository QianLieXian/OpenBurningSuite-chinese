# Localization and the Windows installer

The application supports the language codes `en`, `ru`, `zh-Hans` and `ja`.
Product names, disc-format identifiers, file extensions and device data are not translated.
User paths, passwords, keys and volume labels must retain their exact values.

## Resource model

`OpenBurningSuite/Localization/Resources/en.json` is the source catalog. English source
strings are stable keys; numbered `{0}` slots capture runtime values. Every translated
catalog must contain exactly the same keys and slots. The explicitly reviewed invariant
list is `OpenBurningSuite/Localization/invariant.json`; do not add English prose merely
to silence the coverage check.

`PresentationLocalization` translates final Avalonia text presenters, window titles,
native menu captions, watermarks and read-only operation logs. It preserves the original
source text when the user changes language. Editable inputs and file-name/title/path data presenters marked `KeepOriginal` are excluded. ComboBox
items, selected values, persisted settings and arguments passed to native disc operations
retain their original IDs; only their visible presenters are translated. Native file picker
titles and file-type descriptions use `Localizer.Text` explicitly.

`Localizer` loads embedded catalogs. It handles literal messages, runtime slots and
timestamped log lines without interpreting braces, dollar signs or paths supplied by a
user as formatting instructions. Resource regexes have bounded execution time.
Unrecognized external diagnostics are passed through; file paths, sector values and native identifiers are preserved.

## Selecting a language

1. A saved per-user `language` value in `settings.json` takes precedence.
2. On Windows, the installer writes the initial language to the 64-bit registry key
   `HKLM\Software\OpenBurningSuite\UILanguage`.
   Inno Setup uses `zh` for Simplified Chinese; the app normalizes it to `zh-Hans`.
3. Without either preference, the system UI language is normalized to a supported code.
4. Unsupported languages fall back to English.

Settings → General → Interface language → Save Settings applies a change immediately.
The command-line option `--language ja` temporarily overrides the preference without
rewriting user settings. Numeric/date cultures are not changed, preserving compatibility
with existing device and format parsers.

The splash window is closed only after showing the main window. This prevents
Avalonia's default last-window-close behavior from shutting down during cold startup.

## Updating translations

The maintenance script uses offline Argos/CTranslate2 models from the official
[Argos package index](https://github.com/argosopentech/argospm-index).
The shipped application has no model dependency and does not make translation requests.
Hand-reviewed entries are preserved by the generator. Public English source strings were also cross-checked with Google translation during catalog preparation; navigation, settings, action labels and optical-disc terminology were reviewed. User files and credentials are never sent for translation by the application. Generated text still requires
terminology and visual review; model output alone is not proof of translation quality.

```sh
python -m pip install ctranslate2==4.8.4 sentencepiece==0.2.2
python scripts/translate-catalog.py ru --models-dir /path/to/models --download
python scripts/translate-catalog.py zh-Hans --models-dir /path/to/models --download
python scripts/translate-catalog.py ja --models-dir /path/to/models --download
python scripts/check-localization.py
dotnet run --project tests/Localization.Tests -c Release
```

Model downloads need HTTPS access to `argos-net.com`. Respect the environment's network
policy; do not disable TLS validation or proxy settings. Downloaded hashes are printed
for provenance tracking. Do not include the models in the installer.

The integrity check rejects missing keys, duplicate keys, empty translations, damaged
runtime slots, untranslated prose and source strings missing from the source catalog.
The Roslyn-based scanner under `tools/LocalizationCatalog` scans C# literals and
interpolations; the Python check also scans XAML labels, tooltips and item captions.
It intentionally collects a conservative superset of presentation strings.

## Release

`build/installer.iss` defines all four Inno Setup languages and persists the installer
selection. Windows CI publishes `win-x64` as a self-contained application, packages it,
with the SHA-256-verified official Inno Setup 7.1.1 compiler, then verifies silent installation, saved language, application startup and uninstallation
for each supported language. Tests must pass before publishing. A `v` tag matching the
project version publishes one `windows-amd64-Setup.exe` asset. No ARM64 or portable ZIP
asset is produced by this release workflow. Other platform workflows remain available
for manual source builds.
