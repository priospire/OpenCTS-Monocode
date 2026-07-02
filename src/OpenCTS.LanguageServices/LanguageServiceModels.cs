using OpenCTS.Core;

namespace OpenCTS.LanguageServices;

public sealed record ScratchAsmTextRange(SourceLocation Start, SourceLocation End);

public sealed record StructuredDiagnostic(
    string Code,
    string Severity,
    string Message,
    string SourceName,
    ScratchAsmTextRange Range,
    string? JsonPath = null);

public enum ScratchAsmSymbolKind
{
    Constant,
    Enum,
    EnumMember,
    Struct,
    Field,
    Target,
    Variable,
    List,
    Broadcast,
    Extension,
    Procedure,
    Parameter,
    Local
}

public sealed record ScratchAsmSymbol(
    string Name,
    ScratchAsmSymbolKind Kind,
    ScratchAsmTextRange Range,
    string? Container = null,
    string? Detail = null);

public sealed record ScratchAsmCompletion(string Label, string InsertText, string Kind, string? Detail = null);

public sealed record ScratchAsmSignature(string Label, IReadOnlyList<string> Parameters, string? Documentation = null);

public sealed record ScratchAsmCatalogItem(
    string Alias,
    string Opcode,
    string Shape,
    string Category,
    string Color,
    IReadOnlyList<string> Arguments,
    string? Extension,
    bool Legacy);

public sealed record DocumentAnalysis(
    int Version,
    string SourceName,
    IReadOnlyList<CtsColorSpan> ColorSpans,
    IReadOnlyList<StructuredDiagnostic> Diagnostics,
    IReadOnlyList<ScratchAsmSymbol> Symbols);

