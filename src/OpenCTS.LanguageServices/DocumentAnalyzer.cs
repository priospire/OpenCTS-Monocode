using OpenCTS.Core;

namespace OpenCTS.LanguageServices;

public sealed class DocumentAnalyzer
{
    public DocumentAnalysis Analyze(string source, string sourceName = "document.sasm", int version = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        CtsCompileResult compile = CtsCompiler.Compile(source, sourceName);
        StructuredDiagnostic[] diagnostics = compile.Diagnostics.Select(diagnostic => new StructuredDiagnostic(
            diagnostic.Code,
            diagnostic.Severity.ToString().ToLowerInvariant(),
            diagnostic.Message,
            sourceName,
            new ScratchAsmTextRange(diagnostic.Span.Start, diagnostic.Span.End))).ToArray();

        return new DocumentAnalysis(
            version,
            sourceName,
            CtsSyntaxClassifier.Classify(source),
            diagnostics,
            SymbolIndex.Create(source));
    }
}

