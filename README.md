# OpenCTS

OpenCTS is a Windows .NET utility that validates Scratch 3 project sources and writes Scratch-readable `.sb3` files.

It can also compile Monocode `.mono` source files into Scratch `project.json` and package them as `.sb3`.

## Run

Start the UI:

```powershell
.\Monocode.exe
```

For development builds:

```powershell
dotnet run --project src/OpenCTS.App
```

Run from the command line:

```powershell
.\Monocode.exe samples\hello.mono artifacts\hello-from-monocode.sb3
dotnet run --project src/OpenCTS.App -- samples/minimal-project artifacts/minimal-project.sb3
dotnet run --project src/OpenCTS.App -- samples/hello.mono artifacts/hello-from-monocode.sb3
.\Monocode.exe --repair artifacts\damaged.sb3 artifacts\repaired.sb3
.\Monocode.exe --emit-aliases samples
```

The input can be:

- A `.mono` Monocode file.
- A `.sb3` file.
- A folder containing `project.json` and asset files.
- A `project.json` file with asset files beside it.

The output path must end in `.sb3`.

## Validation

OpenCTS validates actual Scratch `.sb3` project structure. It does not translate Rust, Python, JavaScript, or other source languages into Scratch.

For `.mono` input, OpenCTS parses Monocode, emits Scratch block JSON and generated SVG costume assets, validates the generated project, and reports language diagnostics with severity, code, line, and column. The editor colors aliases and contextual syntax with their Scratch category colors; warnings and errors use distinct diagnostic colors and can be double-clicked to select their source location. Monocode includes native aliases for every cataloged Scratch core and bundled-extension block, structured control, expressions, variable/list operations, and generic opcode forms. Warnings allow output; errors block output.

For readable but structurally damaged `.sb3` files, opt-in safe repair can restore missing containers/defaults, add a stage, and replace unusable costume references with a generated SVG. It never mutates the source and never writes output unless the repaired project validates.

It reports:

- JSON syntax errors with line and column.
- Missing required Scratch fields with JSON paths.
- Incorrect core field types.
- Missing referenced asset files.
- Asset `md5ext` values that look like paths instead of root zip file names.
- Monocode language errors and raw opcode warnings.

## Build And Test

```powershell
dotnet build OpenCTS.slnx
dotnet test OpenCTS.slnx
```

## Project Layout

- `src/OpenCTS.Core` contains input loading, validation, source location mapping, and `.sb3` writing.
- `src/OpenCTS.App` contains the WinForms UI and CLI entry point.
- `tests/OpenCTS.Tests` contains focused conversion and diagnostic tests.
- `samples/minimal-project` contains a valid folder-style Scratch input.
- `samples/hello.mono` contains the primary Monocode smoke sample.
- `samples/all-aliases.mono` compiles every registered alias, including legacy blocks, and demonstrates custom blocks.
- `samples/all-aliases.json` is the schema-v1 machine-readable alias, binding, shape, extension, and color catalog.

For Scratch formatting details, see [docs/sb3-format.md](docs/sb3-format.md).
For Monocode, see [docs/monocode.md](docs/monocode.md).
