# Monocode Language

The OpenCTS custom language is now named Monocode.

- Monocode source files use `.mono` exclusively.
- The full language reference is [docs/monocode.md](monocode.md).

Smoke command:

```powershell
dotnet run --project src/OpenCTS.App -- samples/hello.mono artifacts/hello-from-monocode.sb3
```
