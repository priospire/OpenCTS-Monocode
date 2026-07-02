# ScratchASM VS Code Extension

This extension adds ScratchASM language support for `.sasm` and legacy `.mono` files.

Features:

- Scratch category color coding through the bundled TextMate grammar and ScratchASM light/dark themes.
- Diagnostics and completions through `ScratchASM.LanguageHost`.
- Command: `ScratchASM: Restart Language Host`.

Development use:

1. Build the repo with `dotnet build OpenCTS.slnx`.
2. Open this folder in VS Code extension development mode.
3. If the host is not found automatically, set `scratchasm.languageHostPath` to `ScratchASM.LanguageHost.exe` or the built `ScratchASM.LanguageHost.dll`.

