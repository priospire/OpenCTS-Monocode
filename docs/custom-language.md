# ScratchASM Language

The OpenCTS custom language is now named ScratchASM.

- ScratchASM source files use `.sasm`. Legacy `.mono` files are accepted for compatibility.
- The full language reference is [docs/scratchasm.md](scratchasm.md).

Smoke command:

```powershell
dotnet run --project src/OpenCTS.App -- samples/hello.sasm artifacts/hello-from-scratchasm.sb3
```
