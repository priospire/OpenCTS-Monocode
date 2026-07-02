# OpenCTS

OpenCTS is a Windows .NET utility that validates Scratch 3 project sources and writes Scratch-readable `.sb3` files.

It can compile ScratchASM `.sasm` source files into Scratch `project.json`, package them as `.sb3`, repair recoverable Scratch package damage, and decompile `.sb3` files into editable ScratchASM for display/editing in the UI. Legacy `.mono` files are still accepted.

## Run

Start the UI:

```powershell
.\ScratchASM.exe
```

The UI opens the ScratchASM IDE with dark/light mode, browsing, editing, compile, repair, diagnostics, and `.sb3` decompile-to-edit display.

For development builds:

```powershell
dotnet run --project src/OpenCTS.App
```

Run from the command line:

```powershell
.\ScratchASM.exe samples\hello.sasm artifacts\hello-from-scratchasm.sb3
dotnet run --project src/OpenCTS.App -- samples/minimal-project artifacts/minimal-project.sb3
dotnet run --project src/OpenCTS.App -- samples/hello.sasm artifacts/hello-from-scratchasm.sb3
.\ScratchASM.exe --repair artifacts\damaged.sb3 artifacts\repaired.sb3
.\ScratchASM.exe --emit-aliases samples
```

The input can be:

- A `.sasm` ScratchASM file, or a legacy `.mono` file.
- A `.sb3` file.
- A folder containing `project.json` and asset files.
- A `project.json` file with asset files beside it.

The output path must end in `.sb3`.

## Editor And MCP Support

- VS Code support is in `editors/vscode-scratchasm`; it provides Scratch-colored syntax, diagnostics, completions, and ScratchASM light/dark themes.
- `ScratchASM.LanguageHost.exe` supports `--lsp` for editors and `--mcp --workspace <folder>` for MCP clients.
- MCP tools include analysis, compile, decompile, merge edited source, repair, catalog lookup, and project info.

## Validation

OpenCTS validates actual Scratch `.sb3` project structure. It does not translate Rust, Python, JavaScript, or other source languages into Scratch.

For ScratchASM input, OpenCTS emits Scratch block JSON and generated SVG costume assets, validates the generated project, and reports language diagnostics with severity, code, line, and column. The editor colors aliases and contextual syntax with their Scratch category colors; warnings and errors use distinct diagnostic colors and can be double-clicked to select their source location. ScratchASM includes native aliases for every cataloged Scratch core and bundled-extension block, structured control, expressions, variable/list operations, local procedure variables, structs, enums, sprite-only variables, and generic opcode forms. Warnings allow output; errors block output.

For readable but structurally damaged `.sb3`, `project.json`, or folder inputs, opt-in safe repair can restore missing containers/defaults, add a stage, and replace unusable costume references with a generated SVG. For ScratchASM source, repair can normalize line endings, replace unsafe text characters, rewrite common Scratch-like variable/list phrases, wrap an implicit stage, and add missing closing braces. Repair never mutates the source input and never writes output unless the repaired project validates.

It reports:

- JSON syntax errors with line and column.
- Missing required Scratch fields with JSON paths.
- Incorrect core field types.
- Missing referenced asset files.
- Asset `md5ext` values that look like paths instead of root zip file names.
- ScratchASM language errors and raw opcode warnings.

## Build And Test

```powershell
dotnet build OpenCTS.slnx
dotnet test OpenCTS.slnx
```

## Project Layout

- `src/OpenCTS.Core` contains input loading, validation, source location mapping, and `.sb3` writing.
- `src/OpenCTS.App` contains the WinForms UI and CLI entry point.
- `src/ScratchASM.LanguageHost` contains the LSP/MCP-compatible ScratchASM language host.
- `editors/vscode-scratchasm` contains VS Code syntax highlighting, diagnostics, completions, and themes.
- `tests/OpenCTS.Tests` contains focused conversion and diagnostic tests.
- `tests/OpenCTS.LanguageServices.Tests` contains language service tests.
- `tests/ScratchASM.LanguageHost.Tests` contains LSP/MCP protocol tests.
- `samples/minimal-project` contains a valid folder-style Scratch input.
- `samples/hello.sasm` contains the primary ScratchASM smoke sample.
- `samples/all-aliases.sasm` compiles every registered alias, including legacy blocks, and demonstrates custom blocks.
- `samples/all-aliases.json` is the schema-v1 machine-readable alias, binding, shape, extension, and color catalog.

For Scratch formatting details, see [docs/sb3-format.md](docs/sb3-format.md).
For ScratchASM, see [docs/scratchasm.md](docs/scratchasm.md).
