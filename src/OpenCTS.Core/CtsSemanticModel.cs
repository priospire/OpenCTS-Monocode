namespace OpenCTS.Core;

public sealed class CtsSemanticModel
{
    public CtsSemanticModel(
        CtsCompilationUnit compilationUnit,
        IReadOnlyDictionary<string, CtsValue> constants,
        IReadOnlyDictionary<string, CtsStructDeclaration> structs,
        IReadOnlyList<CtsDiagnostic> diagnostics)
    {
        CompilationUnit = compilationUnit;
        Constants = constants;
        Structs = structs;
        Diagnostics = diagnostics;
    }

    public CtsCompilationUnit CompilationUnit { get; }

    public IReadOnlyDictionary<string, CtsValue> Constants { get; }

    public IReadOnlyDictionary<string, CtsStructDeclaration> Structs { get; }

    public IReadOnlyList<CtsDiagnostic> Diagnostics { get; }
}
