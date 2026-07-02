# OpenCTS Context

## Current State

OpenCTS is a .NET 10 Windows utility and ScratchASM IDE for Scratch 3 projects. It validates Scratch project sources, compiles ScratchASM `.sasm` source, accepts legacy `.mono`, decompiles `.sb3` projects into editable ScratchASM for display/editing, attempts safe repair for supported inputs, and writes Scratch-readable `.sb3` archives.

The canonical repository is `https://github.com/priospire/OpenCTS-Monocode`.

## Key Paths

- `ScratchASM.exe`: root self-contained Windows IDE/CLI executable tracked through Git LFS.
- `ScratchASM.LanguageHost.exe`: root self-contained LSP/MCP executable tracked through Git LFS.
- `src/OpenCTS.Core`: package loading, validation, repair, ScratchASM parsing/compilation, generated assets, decompile/edit/merge, and ZIP output.
- `src/OpenCTS.Core/ScratchAsmCatalogExporter.cs`: deterministic all-alias `.sasm` and JSON catalog generator.
- `src/OpenCTS.Core/ScratchAsmSourceRepairer.cs`: source repair for safe Scratch-like phrase rewrites and cleanup.
- `src/OpenCTS.Core/ScratchProjectRepairer.cs`: conservative Scratch project/package repair implementation.
- `src/OpenCTS.LanguageServices`: diagnostics, symbols, completions, color spans, workspace path policy, and catalog lookup.
- `src/ScratchASM.LanguageHost`: LSP and MCP JSON-RPC host.
- `src/OpenCTS.App`: WinForms ScratchASM IDE and CLI entry point.
- `editors/vscode-scratchasm`: VS Code syntax highlighting, diagnostics, completions, themes, and extension glue.
- `docs/scratchasm.md`: complete ScratchASM language/tooling reference.
- `docs/monocode.md`: compatibility notice for the old language name.
- `samples/hello.sasm`: primary smoke sample.
- `samples/all-aliases.sasm`: generated compile-safe use of every registered alias.
- `samples/all-aliases.json`: generated schema-v1 alias, binding, shape, extension, and color catalog.

## Inputs And CLI

Accepted conversion inputs:

- `.sasm` ScratchASM source.
- legacy `.mono` source, with warning `CTS2003`.
- `.sb3` archive with root `project.json`.
- folder containing `project.json` and root asset files.
- `project.json` with asset files beside it.

Commands:

```powershell
.\ScratchASM.exe
.\ScratchASM.exe <input.sasm|input.mono|input.sb3|project.json|folder> <output.sb3>
.\ScratchASM.exe --repair <input.sasm|input.mono|input.sb3|project.json|folder> <output.sb3>
.\ScratchASM.exe --emit-aliases <output-folder>
.\ScratchASM.LanguageHost.exe --lsp
.\ScratchASM.LanguageHost.exe --mcp --workspace <folder>
```

## ScratchASM Surface

ScratchASM supports:

- targets: `stage { ... }`, `sprite "Name" { ... }`
- declarations: `var`, `cloud var`, `sprite var`, `global var`, `list`, `broadcast`, `extension`, `state`, `rotationStyle`
- `const`, explicit enums, flat structs, sprite-only variables, stage-global variables
- function-scoped procedure locals lowered through Scratch-compatible frame lists
- generated SVG costumes with `line`, `rect`, `circle`, `ellipse`, `path`, and `text`
- native hats such as `@greenflag:`, `@event.received message:`, and bundled-extension hats
- all cataloged native stack, reporter, Boolean, C-block, cap, legacy, and extension aliases
- structured `repeat`, `forever`, `if`/`else`, `repeatuntil`, and `waituntil`
- variable operations: `score = 5`, `score += 3`; safe repair can rewrite `set score to 5` and `change score by 3`
- list methods: `.add`, `.delete`, `.delete_all`, `.insert`, `.replace`, `.show`, `.hide`; safe repair can rewrite common Scratch-like list phrases
- list reporters: `.item()`, `.index()`, `.length()`, `.contains()`, `.contents()`
- expressions with `+ - * / % ^ < > <= >= == != and or not`, plus `&& || !`
- Scratch reporter functions including random/string operators, rounding, trig, `ln`, `log10`, `exp`/`e^`, and `pow10`/`10^`
- custom procedures with `num`, `str`, `bool` parameters, defaults, display signatures, warp mode, and calls
- generic `block` opcode escape for exact fields, inputs, mutations, shadows, and substacks

The QoL `^` operator is lowered only when reliable. The exponent must be an integer literal from `-64` through `64`, and the base must be stable. Positive powers become repeated multiplication, zero becomes one, and negative powers use reciprocal division. Dynamic/fractional exponents and nondeterministic bases produce `CTS1020`.

## IDE, VS Code, And MCP

The WinForms IDE has opaque modern styling, dark/light mode, file/folder browsing, source editing, `.sb3` decompile-to-edit display, Scratch category color coding, warning/error diagnostics, double-click navigation from diagnostics to source spans, compile, repair, and save-source actions.

The VS Code extension in `editors/vscode-scratchasm` contributes `.sasm`/`.mono` language support, TextMate Scratch-category coloring, ScratchASM dark/light themes, diagnostics, and completions through `ScratchASM.LanguageHost`.

The MCP host uses workspace-relative paths and rejects rooted, UNC, device, escaping, and reparse-point paths. MCP tools:

- `analyze_source`
- `compile_to_sb3`
- `decompile_sb3`
- `merge_edited_source`
- `repair_input`
- `lookup_catalog`
- `get_project_info`

## Safe Repair

Repair never modifies the source input and writes output only after validation succeeds.

Supported repair inputs:

- `.sasm`/`.mono`: removes BOM, normalizes line endings, expands tabs, replaces smart quotes/non-breaking spaces, rewrites safe Scratch-like variable/list phrases, wraps an implicit stage, and appends missing closing braces when brace balance is clearly positive.
- `.sb3`: validates safe root ZIP entries, rejects duplicate/unsafe archive paths, repairs readable `project.json`, restores root/target defaults, adds/moves the single required stage, demotes extra stages, repairs block containers/links, and generates a default SVG costume when needed.
- `project.json`/folders: applies readable JSON repair, including UTF-8 BOM removal in repair mode, and generated-asset behavior.

Malformed JSON that cannot be parsed after BOM cleanup, unreadable ZIP data, unsafe ZIP paths, unknown semantics, and source that still fails after safe rewrites remain errors.

## Verification Status

Final verification completed successfully on 2026-07-02:

```powershell
dotnet restore OpenCTS.slnx
dotnet build OpenCTS.slnx --no-restore --verbosity:minimal
dotnet test OpenCTS.slnx --no-restore --no-build --verbosity:minimal
dotnet publish src/OpenCTS.App/OpenCTS.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o artifacts/publish/app
dotnet publish src/ScratchASM.LanguageHost/ScratchASM.LanguageHost.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o artifacts/publish/host
.\ScratchASM.exe samples\hello.sasm artifacts\root-exe-smoke.sb3
.\ScratchASM.exe samples\all-aliases.sasm artifacts\all-aliases-smoke.sb3
.\ScratchASM.exe --repair artifacts\damaged-project artifacts\root-repaired-folder-smoke.sb3
```

Results:

- clean solution build: 0 warnings, 0 errors
- tests: 77 passed, 0 failed
- VS Code extension JSON files parse successfully
- `ScratchASM.exe` wrote `artifacts/root-exe-smoke.sb3`
- `ScratchASM.exe` wrote `artifacts/all-aliases-smoke.sb3`
- `ScratchASM.exe --repair` repaired a BOM-prefixed damaged folder `project.json` and wrote `artifacts/root-repaired-folder-smoke.sb3`
- `ScratchASM.LanguageHost.exe --mcp --workspace .` returned a valid MCP initialize response

Published root artifacts:

- `ScratchASM.exe`
- `ScratchASM.LanguageHost.exe`

