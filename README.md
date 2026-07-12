# GrammarFixer

> OS-level system-wide grammar & typo checker for Windows 10/11 — like Grammarly but fully local-first and open-source.

![Build](https://github.com/mr-yeswise/grammar-fixer/actions/workflows/build.yml/badge.svg)
![License](https://img.shields.io/badge/license-MIT-blue.svg)

---

## Features

- **System-wide text capture** via UI Automation (UIA) `TextPattern`/`ValuePattern` with Win32 keyboard hook + clipboard fallback
- **Two correction engines** (user-toggleable):
  - 🔒 **Static/Offline** — local typo dictionary, capitalization, double spaces, contractions, repeated words, `i`→`I`
  - ⚡ **AI mode** — Groq API (`llama-3.1-8b-instant`, ~560 t/s) for sub-second rewrites
- **Two UX modes**:
  - **One-click rewrite** — global hotkey `Ctrl+Alt+G` replaces focused field instantly
  - **Review suggestions** — floating always-on-top overlay near caret with accept/reject chips
- **System tray** with enable/disable, mode switch, settings, autostart
- **App allow/deny list** — configure which apps activate the checker
- **No admin rights required** to install or run
- **Privacy-first** — static mode is 100% offline; AI mode sends text to Groq only

---

## Privacy

| Mode | Data sent externally | Logged | Telemetry |
|---|---|---|---|
| Static | ❌ Nothing | ❌ | ❌ |
| AI (Groq) | ✅ Text sent to Groq API | ❌ | ❌ |

Your Groq API key is stored in **Windows Credential Manager** (DPAPI-encrypted). It is never written to disk in plaintext.

---

## Requirements

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (build only)
- No admin rights needed

---

## Quick Start

```bash
git clone https://github.com/mr-yeswise/grammar-fixer.git
cd grammar-fixer
dotnet publish src/GrammarFixer/GrammarFixer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
./publish/GrammarFixer.exe
```

---

## Hotkeys

| Hotkey | Action |
|---|---|
| `Ctrl+Alt+G` | Trigger correction on focused field |

---

## UIA Fallback Matrix

| App Category | Examples | Primary | Fallback |
|---|---|---|---|
| Win32 Edit | Notepad, legacy | `ValuePattern.SetValue` | `SendKeys` |
| Chromium | Chrome, Edge, Brave | `ValuePattern` via accessibility DOM | Clipboard `Ctrl+A/V` |
| Electron | VS Code, Slack, Discord, WhatsApp | `TextPattern` read + clipboard write | Clipboard `Ctrl+A/V` |
| UWP | Mail, Settings | `ValuePattern` | Clipboard |
| WPF | Hermes Agent | `ValuePattern.SetValue` | None needed |
| Office | Word, Excel | `ValuePattern` / COM | Clipboard |
| Java Swing | IntelliJ | Partial UIA via Java Access Bridge | Clipboard |

---

## Configuration

Settings stored at `%APPDATA%\GrammarFixer\settings.json`.

```json
{
  "Enabled": true,
  "Mode": "Static",
  "UxMode": "OneClickRewrite",
  "HotkeyTrigger": "Ctrl+Alt+G",
  "DebounceMs": 400,
  "HotkeyOnlyMode": false,
  "AllowedApps": ["chrome","msedge","WhatsApp","slack","Discord","notepad","WINWORD","Code"],
  "GroqModel": "llama-3.1-8b-instant"
}
```

---

## Build & Release (GitHub Actions)

See `.github/workflows/build.yml` — produces:
- Portable single-file `GrammarFixer.exe`
- MSIX installer (self-signed for dev; replace cert in CI secrets for production)

---

## Known Limitations

| App | Inline Replace | Method | Notes |
|---|---|---|---|
| Chrome / Edge | ✅ | Clipboard | Must enable accessibility in flags |
| VS Code | ✅ | Clipboard | Electron partial UIA |
| Notepad | ✅ | ValuePattern | Full inline |
| Word | ✅ | ValuePattern | COM fallback available |
| Slack | ✅ | Clipboard | Electron |
| Discord | ✅ | Clipboard | Electron |
| WhatsApp Desktop | ✅ | Clipboard | Electron |
| Terminal / ConHost | ⚠️ | Clipboard only | UIA limited |
| Java apps | ⚠️ | Clipboard | Requires Java Access Bridge |

---

## License

MIT — see [LICENSE](LICENSE)

---

## Publish to GitHub Release

```bash
git init
git add .
git commit -m "feat: initial release v1.0.0"
git remote add origin https://github.com/mr-yeswise/grammar-fixer.git
git push -u origin main
gh release create v1.0.0 ./publish/GrammarFixer.exe --title "GrammarFixer v1.0.0" --notes "Initial release"
```
