namespace OpenCTS.Core;

public sealed class CtsParseResult
{
    public CtsParseResult(CtsCompilationUnit compilationUnit, IReadOnlyList<CtsDiagnostic> diagnostics)
    {
        CompilationUnit = compilationUnit;
        Diagnostics = diagnostics;
    }

    public CtsCompilationUnit CompilationUnit { get; }

    public IReadOnlyList<CtsDiagnostic> Diagnostics { get; }
}

public sealed record CtsCompilationUnit(IReadOnlyList<CtsTargetDeclaration> Targets, SourceSpan Span);

public sealed record CtsTargetDeclaration(
    bool IsStage,
    string Name,
    IReadOnlyList<CtsTargetMember> Members,
    IReadOnlyList<CtsScript> Scripts,
    SourceSpan Span);

public abstract record CtsTargetMember(SourceSpan Span);

public sealed record CtsVariableDeclaration(
    string Name,
    CtsValue InitialValue,
    bool IsCloud,
    SourceSpan Span) : CtsTargetMember(Span);

public sealed record CtsListDeclaration(
    string Name,
    IReadOnlyList<CtsValue> Items,
    SourceSpan Span) : CtsTargetMember(Span);

public sealed record CtsBroadcastDeclaration(
    string Name,
    CtsValue Message,
    SourceSpan Span) : CtsTargetMember(Span);

public sealed record CtsExtensionDeclaration(
    string Name,
    SourceSpan Span) : CtsTargetMember(Span);

public sealed record CtsStateDeclaration(
    IReadOnlyDictionary<string, CtsValue> Properties,
    SourceSpan Span) : CtsTargetMember(Span);

public sealed record CtsRotationStyleDeclaration(
    string Value,
    SourceSpan Span) : CtsTargetMember(Span);

public sealed record CtsCostumeDeclaration(
    string Name,
    int Width,
    int Height,
    double RotationCenterX,
    double RotationCenterY,
    IReadOnlyList<CtsSvgShape> Shapes,
    SourceSpan Span) : CtsTargetMember(Span);

public sealed record CtsSvgShape(
    string Kind,
    IReadOnlyList<CtsValue> Arguments,
    IReadOnlyDictionary<string, CtsValue> Attributes,
    SourceSpan Span);

public abstract record CtsScript(SourceSpan Span);

public sealed record CtsHatScript(
    string HatName,
    string? HatArgument,
    IReadOnlyList<CtsStatement> Statements,
    SourceSpan Span) : CtsScript(Span);

public sealed record CtsGenericHatScript(
    string Opcode,
    IReadOnlyList<CtsRawInput> Inputs,
    IReadOnlyList<CtsRawField> Fields,
    IReadOnlyDictionary<string, CtsValue> Mutation,
    IReadOnlyList<CtsStatement> Statements,
    SourceSpan Span) : CtsScript(Span);

public sealed record CtsAliasHatScript(
    string HatName,
    IReadOnlyList<CtsValue> Arguments,
    IReadOnlyList<CtsStatement> Statements,
    SourceSpan Span) : CtsScript(Span);

public sealed record CtsProcedureDefinition(
    string Name,
    IReadOnlyList<CtsProcedureParameter> Parameters,
    string? DisplaySignature,
    bool Warp,
    IReadOnlyList<CtsStatement> Statements,
    SourceSpan Span) : CtsScript(Span);

public sealed record CtsProcedureParameter(
    string Name,
    CtsParameterType Type,
    CtsValue? DefaultValue,
    SourceSpan Span);

public enum CtsParameterType
{
    Number,
    String,
    Boolean
}

public abstract record CtsStatement(SourceSpan Span);

public sealed record CtsAliasStatement(
    string CommandName,
    IReadOnlyList<CtsValue> Arguments,
    SourceSpan Span) : CtsStatement(Span);

public sealed record CtsStructuredStatement(
    string CommandName,
    IReadOnlyList<CtsValue> Arguments,
    IReadOnlyDictionary<string, IReadOnlyList<CtsStatement>> Substacks,
    SourceSpan Span) : CtsStatement(Span);

public sealed record CtsVariableOperationStatement(
    string VariableName,
    bool IsChange,
    CtsValue Value,
    SourceSpan Span) : CtsStatement(Span);

public sealed record CtsListOperationStatement(
    string ListName,
    string Operation,
    IReadOnlyList<CtsValue> Arguments,
    SourceSpan Span) : CtsStatement(Span);

public sealed record CtsCallStatement(
    string ProcedureName,
    IReadOnlyList<CtsValue> Arguments,
    SourceSpan Span) : CtsStatement(Span);

public sealed record CtsRawStatement(
    string Opcode,
    IReadOnlyList<CtsRawInput> Inputs,
    IReadOnlyList<CtsRawField> Fields,
    IReadOnlyDictionary<string, CtsValue> Mutation,
    SourceSpan Span) : CtsStatement(Span);

public sealed record CtsBlockStatement(
    string Opcode,
    IReadOnlyList<CtsRawInput> Inputs,
    IReadOnlyList<CtsRawField> Fields,
    IReadOnlyDictionary<string, CtsValue> Mutation,
    IReadOnlyList<CtsStatement> Statements,
    IReadOnlyDictionary<string, IReadOnlyList<CtsStatement>> NamedSubstacks,
    SourceSpan Span) : CtsStatement(Span);

public sealed record CtsRawInput(string Name, CtsValue Value);

public sealed record CtsRawField(string Name, CtsValue Value, CtsValue? Id);

public abstract record CtsValue(SourceSpan Span);

public sealed record CtsNumberValue(double Number, string Text, SourceSpan Span) : CtsValue(Span);

public sealed record CtsStringValue(string Text, SourceSpan Span) : CtsValue(Span);

public sealed record CtsIdentifierValue(string Name, SourceSpan Span) : CtsValue(Span);

public sealed record CtsUnaryValue(
    string Operator,
    CtsValue Operand,
    SourceSpan Span) : CtsValue(Span);

public sealed record CtsBinaryValue(
    string Operator,
    CtsValue Left,
    CtsValue Right,
    SourceSpan Span) : CtsValue(Span);

public sealed record CtsFunctionValue(
    string Name,
    IReadOnlyList<CtsValue> Arguments,
    SourceSpan Span) : CtsValue(Span);

public sealed record CtsBlockValue(
    string Opcode,
    IReadOnlyList<CtsRawInput> Inputs,
    IReadOnlyList<CtsRawField> Fields,
    IReadOnlyDictionary<string, CtsValue> Mutation,
    bool IsShadow,
    SourceSpan Span) : CtsValue(Span);
