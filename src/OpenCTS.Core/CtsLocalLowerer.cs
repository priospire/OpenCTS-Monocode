namespace OpenCTS.Core;

public sealed class CtsLocalLoweringResult
{
    public CtsLocalLoweringResult(CtsCompilationUnit compilationUnit, IReadOnlyList<CtsDiagnostic> diagnostics)
    {
        CompilationUnit = compilationUnit;
        Diagnostics = diagnostics;
    }

    public CtsCompilationUnit CompilationUnit { get; }

    public IReadOnlyList<CtsDiagnostic> Diagnostics { get; }
}

public static class CtsLocalLowerer
{
    public static CtsLocalLoweringResult Lower(CtsSemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Lower(model.CompilationUnit);
    }

    public static CtsLocalLoweringResult Lower(CtsCompilationUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        Lowerer lowerer = new(unit);
        return lowerer.Lower();
    }

    private sealed class Lowerer
    {
        private readonly CtsCompilationUnit _unit;
        private readonly List<CtsDiagnostic> _diagnostics = [];

        public Lowerer(CtsCompilationUnit unit)
        {
            _unit = unit;
        }

        public CtsLocalLoweringResult Lower()
        {
            foreach (CtsTargetDeclaration target in _unit.Targets)
            {
                ValidateTarget(target);
            }

            if (_diagnostics.Count > 0)
            {
                return new CtsLocalLoweringResult(_unit, _diagnostics);
            }

            CtsTargetDeclaration[] targets = _unit.Targets.Select(LowerTarget).ToArray();
            CtsCompilationUnit lowered = new(targets, _unit.Span)
            {
                FileDeclarations = _unit.FileDeclarations
            };
            return new CtsLocalLoweringResult(lowered, _diagnostics);
        }

        private void ValidateTarget(CtsTargetDeclaration target)
        {
            Dictionary<string, CtsProcedureDefinition> procedures = target.Scripts
                .OfType<CtsProcedureDefinition>()
                .GroupBy(procedure => procedure.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            Dictionary<string, UnsafeFlow?> directUnsafe = procedures.ToDictionary(
                pair => pair.Key,
                pair => FindUnsafeFlow(pair.Value.Statements),
                StringComparer.Ordinal);
            Dictionary<string, IReadOnlySet<string>> calls = procedures.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlySet<string>)CollectCalls(pair.Value.Statements),
                StringComparer.Ordinal);

            foreach (CtsProcedureDefinition procedure in procedures.Values)
            {
                CtsLocalVariableDeclaration[] locals = LeadingLocals(procedure).ToArray();
                if (locals.Length == 0)
                {
                    continue;
                }

                HashSet<string> localNames = new(procedure.Parameters.Select(parameter => parameter.Name), StringComparer.Ordinal);
                foreach (CtsLocalVariableDeclaration local in locals)
                {
                    if (!localNames.Add(local.Name))
                    {
                        AddError("CTS1043", $"Local variable '{local.Name}' duplicates a parameter or local in procedure '{procedure.Name}'.", local.Span);
                    }
                }

                UnsafeFlow? unsafeFlow = FindReachableUnsafe(procedure.Name, procedure.Name, procedures, directUnsafe, calls, []);
                if (unsafeFlow is not null)
                {
                    string message = unsafeFlow.ProcedureName == procedure.Name
                        ? $"Local-bearing procedure '{procedure.Name}' contains unsafe {unsafeFlow.Reason}."
                        : $"Local-bearing procedure '{procedure.Name}' has an unsafe transitive call to '{unsafeFlow.ProcedureName}' containing {unsafeFlow.Reason}.";
                    AddError("CTS1044", message, unsafeFlow.Span);
                }
            }
        }

        private CtsTargetDeclaration LowerTarget(CtsTargetDeclaration target)
        {
            CtsProcedureDefinition[] localProcedures = target.Scripts
                .OfType<CtsProcedureDefinition>()
                .Where(procedure => LeadingLocals(procedure).Any())
                .ToArray();
            if (localProcedures.Length == 0)
            {
                return target;
            }

            ScratchIdAllocator dataNames = new(target.Members.Select(GetMemberName).Where(name => name is not null)!);
            ScratchIdAllocator procedureNames = new(target.Scripts.OfType<CtsProcedureDefinition>().Select(procedure => procedure.Name));
            string frameCounterName = dataNames.Allocate("__sasm_frame_next");

            List<CtsTargetMember> members = [.. target.Members];
            members.Add(new CtsVariableDeclaration(frameCounterName, Number(0, target.Span), false, target.Span));

            Dictionary<string, ProcedureLowering> lowerings = new(StringComparer.Ordinal);
            foreach (CtsProcedureDefinition procedure in localProcedures)
            {
                string safeProcedure = ScratchIdAllocator.Sanitize(procedure.Name);
                string internalName = procedureNames.Allocate($"__sasm_{safeProcedure}_body");
                string frameListName = dataNames.Allocate($"__sasm_{safeProcedure}_frames");
                members.Add(new CtsListDeclaration(frameListName, [], procedure.Span));

                Dictionary<string, string> valueLists = new(StringComparer.Ordinal);
                foreach (CtsLocalVariableDeclaration local in LeadingLocals(procedure))
                {
                    string valueListName = dataNames.Allocate($"__sasm_{safeProcedure}_{ScratchIdAllocator.Sanitize(local.Name)}_values");
                    valueLists[local.Name] = valueListName;
                    members.Add(new CtsListDeclaration(valueListName, [], local.Span));
                }

                string frameParameterName = AllocateParameterName(procedure, "__sasm_frame_id");
                lowerings[procedure.Name] = new ProcedureLowering(
                    internalName,
                    frameListName,
                    frameParameterName,
                    valueLists);
            }

            List<CtsScript> scripts = [];
            foreach (CtsScript script in target.Scripts)
            {
                if (script is not CtsProcedureDefinition procedure || !lowerings.TryGetValue(procedure.Name, out ProcedureLowering? lowering))
                {
                    scripts.Add(script);
                    continue;
                }

                scripts.Add(CreateWrapper(procedure, lowering, frameCounterName));
                scripts.Add(CreateInternal(procedure, lowering));
            }

            return target with { Members = members, Scripts = scripts };
        }

        private static CtsProcedureDefinition CreateWrapper(
            CtsProcedureDefinition procedure,
            ProcedureLowering lowering,
            string frameCounterName)
        {
            CtsIdentifierValue counter = new(frameCounterName, procedure.Span);
            List<CtsStatement> statements =
            [
                new CtsVariableOperationStatement(frameCounterName, true, Number(1, procedure.Span), procedure.Span),
                new CtsListOperationStatement(lowering.FrameListName, "add", [counter], procedure.Span)
            ];

            IReadOnlyList<CtsLocalVariableDeclaration> locals = LeadingLocals(procedure).ToArray();
            foreach (CtsLocalVariableDeclaration local in locals)
            {
                CtsValue initializer = RewriteValue(local.InitialValue, lowering, counter);
                statements.Add(new CtsListOperationStatement(lowering.ValueLists[local.Name], "add", [initializer], local.Span));
            }

            List<CtsValue> arguments = procedure.Parameters
                .Select(parameter => (CtsValue)new CtsIdentifierValue(parameter.Name, parameter.Span))
                .ToList();
            arguments.Add(counter);
            statements.Add(new CtsCallStatement(lowering.InternalProcedureName, arguments, procedure.Span));

            return procedure with
            {
                Statements = statements,
                DeclaredReturnType = null
            };
        }

        private static CtsProcedureDefinition CreateInternal(
            CtsProcedureDefinition procedure,
            ProcedureLowering lowering)
        {
            CtsProcedureParameter frameParameter = new(
                lowering.FrameParameterName,
                CtsParameterType.Number,
                Number(0, procedure.Span),
                procedure.Span,
                "num");
            CtsIdentifierValue frameReference = new(lowering.FrameParameterName, procedure.Span);
            List<CtsStatement> statements = procedure.Statements
                .SkipWhile(statement => statement is CtsLocalVariableDeclaration)
                .Select(statement => RewriteStatement(statement, lowering, frameReference))
                .ToList();

            foreach (string valueList in lowering.ValueLists.Values)
            {
                statements.Add(new CtsListOperationStatement(
                    valueList,
                    "delete",
                    [FrameIndex(lowering, frameReference, procedure.Span)],
                    procedure.Span));
            }

            statements.Add(new CtsListOperationStatement(
                lowering.FrameListName,
                "delete",
                [FrameIndex(lowering, frameReference, procedure.Span)],
                procedure.Span));

            return new CtsProcedureDefinition(
                lowering.InternalProcedureName,
                [.. procedure.Parameters, frameParameter],
                null,
                procedure.Warp,
                statements,
                procedure.Span);
        }

        private static CtsStatement RewriteStatement(
            CtsStatement statement,
            ProcedureLowering lowering,
            CtsValue frameReference)
        {
            switch (statement)
            {
                case CtsVariableOperationStatement variable when lowering.ValueLists.TryGetValue(variable.VariableName, out string? valueList):
                {
                    CtsValue value = RewriteValue(variable.Value, lowering, frameReference);
                    if (variable.IsChange)
                    {
                        value = new CtsBinaryValue(
                            "+",
                            LocalRead(variable.VariableName, lowering, frameReference, variable.Span),
                            value,
                            variable.Span);
                    }

                    return new CtsListOperationStatement(
                        valueList,
                        "replace",
                        [FrameIndex(lowering, frameReference, variable.Span), value],
                        variable.Span);
                }
                case CtsVariableOperationStatement variable:
                    return variable with { Value = RewriteValue(variable.Value, lowering, frameReference) };
                case CtsListOperationStatement list:
                    return list with { Arguments = list.Arguments.Select(value => RewriteValue(value, lowering, frameReference)).ToArray() };
                case CtsCallStatement call:
                    return call with { Arguments = call.Arguments.Select(value => RewriteValue(value, lowering, frameReference)).ToArray() };
                case CtsAliasStatement alias:
                    return alias with { Arguments = alias.Arguments.Select(value => RewriteValue(value, lowering, frameReference)).ToArray() };
                case CtsStructuredStatement structured:
                    return structured with
                    {
                        Arguments = structured.Arguments.Select(value => RewriteValue(value, lowering, frameReference)).ToArray(),
                        Substacks = structured.Substacks.ToDictionary(
                            pair => pair.Key,
                            pair => (IReadOnlyList<CtsStatement>)pair.Value.Select(item => RewriteStatement(item, lowering, frameReference)).ToArray(),
                            StringComparer.Ordinal)
                    };
                default:
                    return statement;
            }
        }

        private static CtsValue RewriteValue(CtsValue value, ProcedureLowering lowering, CtsValue frameReference)
        {
            return value switch
            {
                CtsIdentifierValue identifier when lowering.ValueLists.ContainsKey(identifier.Name) =>
                    LocalRead(identifier.Name, lowering, frameReference, identifier.Span),
                CtsUnaryValue unary => unary with { Operand = RewriteValue(unary.Operand, lowering, frameReference) },
                CtsBinaryValue binary => binary with
                {
                    Left = RewriteValue(binary.Left, lowering, frameReference),
                    Right = RewriteValue(binary.Right, lowering, frameReference)
                },
                CtsFunctionValue function => function with
                {
                    Arguments = function.Arguments.Select(argument => RewriteValue(argument, lowering, frameReference)).ToArray()
                },
                CtsBlockValue block => block with
                {
                    Inputs = block.Inputs.Select(input => input with
                    {
                        Value = RewriteValue(input.Value, lowering, frameReference)
                    }).ToArray()
                },
                _ => value
            };
        }

        private static CtsValue LocalRead(
            string localName,
            ProcedureLowering lowering,
            CtsValue frameReference,
            SourceSpan span)
        {
            return new CtsFunctionValue(
                $"{lowering.ValueLists[localName]}.item",
                [FrameIndex(lowering, frameReference, span)],
                span);
        }

        private static CtsValue FrameIndex(ProcedureLowering lowering, CtsValue frameReference, SourceSpan span)
        {
            return new CtsFunctionValue($"{lowering.FrameListName}.index", [frameReference], span);
        }

        private static IEnumerable<CtsLocalVariableDeclaration> LeadingLocals(CtsProcedureDefinition procedure)
        {
            return procedure.Statements.TakeWhile(statement => statement is CtsLocalVariableDeclaration)
                .Cast<CtsLocalVariableDeclaration>();
        }

        private static UnsafeFlow? FindReachableUnsafe(
            string rootName,
            string procedureName,
            IReadOnlyDictionary<string, CtsProcedureDefinition> procedures,
            IReadOnlyDictionary<string, UnsafeFlow?> directUnsafe,
            IReadOnlyDictionary<string, IReadOnlySet<string>> calls,
            HashSet<string> visited)
        {
            if (!visited.Add(procedureName))
            {
                return null;
            }

            if (directUnsafe.TryGetValue(procedureName, out UnsafeFlow? flow) && flow is not null)
            {
                return flow with { ProcedureName = procedureName };
            }

            if (!calls.TryGetValue(procedureName, out IReadOnlySet<string>? callees))
            {
                return null;
            }

            foreach (string callee in callees)
            {
                if (!procedures.ContainsKey(callee))
                {
                    continue;
                }

                UnsafeFlow? reachable = FindReachableUnsafe(rootName, callee, procedures, directUnsafe, calls, visited);
                if (reachable is not null)
                {
                    return reachable;
                }
            }

            return null;
        }

        private static UnsafeFlow? FindUnsafeFlow(IReadOnlyList<CtsStatement> statements)
        {
            foreach (CtsStatement statement in statements)
            {
                switch (statement)
                {
                    case CtsRawStatement raw:
                        return new UnsafeFlow(string.Empty, "raw % opcode flow", raw.Span);
                    case CtsBlockStatement block:
                        return new UnsafeFlow(string.Empty, "unknown-flow generic block", block.Span);
                    case CtsAliasStatement alias when IsTerminalAlias(alias.CommandName, alias.Arguments):
                        return new UnsafeFlow(string.Empty, "terminal block", alias.Span);
                    case CtsStructuredStatement structured:
                        if (IsTerminalAlias(structured.CommandName, structured.Arguments))
                        {
                            return new UnsafeFlow(string.Empty, "terminal block", structured.Span);
                        }

                        foreach (IReadOnlyList<CtsStatement> substack in structured.Substacks.Values)
                        {
                            UnsafeFlow? nested = FindUnsafeFlow(substack);
                            if (nested is not null)
                            {
                                return nested;
                            }
                        }

                        break;
                }
            }

            return null;
        }

        private static bool IsTerminalAlias(string name, IReadOnlyList<CtsValue> arguments)
        {
            if (!CtsBlockRegistry.TryResolve(name, arguments.Count, out CtsAliasDefinition definition))
            {
                return false;
            }

            return definition.TerminalPolicy == CtsTerminalPolicy.AlwaysCaps ||
                definition.TerminalPolicy == CtsTerminalPolicy.CapsUnlessOtherScripts &&
                (arguments.Count == 0 || !string.Equals(ValueText(arguments[0]), "other scripts in sprite", StringComparison.OrdinalIgnoreCase));
        }

        private static HashSet<string> CollectCalls(IReadOnlyList<CtsStatement> statements)
        {
            HashSet<string> calls = new(StringComparer.Ordinal);
            foreach (CtsStatement statement in statements)
            {
                switch (statement)
                {
                    case CtsCallStatement call:
                        calls.Add(call.ProcedureName);
                        break;
                    case CtsStructuredStatement structured:
                        foreach (IReadOnlyList<CtsStatement> substack in structured.Substacks.Values)
                        {
                            calls.UnionWith(CollectCalls(substack));
                        }

                        break;
                    case CtsBlockStatement block:
                        calls.UnionWith(CollectCalls(block.Statements));
                        foreach (IReadOnlyList<CtsStatement> substack in block.NamedSubstacks.Values)
                        {
                            calls.UnionWith(CollectCalls(substack));
                        }

                        break;
                }
            }

            return calls;
        }

        private static string AllocateParameterName(CtsProcedureDefinition procedure, string preferred)
        {
            ScratchIdAllocator allocator = new(procedure.Parameters.Select(parameter => parameter.Name));
            return allocator.Allocate(preferred);
        }

        private static string? GetMemberName(CtsTargetMember member) => member switch
        {
            CtsVariableDeclaration variable => variable.Name,
            CtsListDeclaration list => list.Name,
            CtsBroadcastDeclaration broadcast => broadcast.Name,
            _ => null
        };

        private static string ValueText(CtsValue value) => value switch
        {
            CtsStringValue text => text.Text,
            CtsIdentifierValue identifier => identifier.Name,
            CtsNumberValue number => number.Text,
            _ => string.Empty
        };

        private static CtsNumberValue Number(double value, SourceSpan span) =>
            new(value, value.ToString(System.Globalization.CultureInfo.InvariantCulture), span);

        private void AddError(string code, string message, SourceSpan span)
        {
            _diagnostics.Add(new CtsDiagnostic(code, DiagnosticSeverity.Error, message, span));
        }

        private sealed record ProcedureLowering(
            string InternalProcedureName,
            string FrameListName,
            string FrameParameterName,
            IReadOnlyDictionary<string, string> ValueLists);

        private sealed record UnsafeFlow(string ProcedureName, string Reason, SourceSpan Span);
    }
}
