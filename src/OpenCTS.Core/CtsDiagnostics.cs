namespace OpenCTS.Core;

public sealed record CtsDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    SourceSpan Span)
{
    public override string ToString()
    {
        return $"{Severity} {Code} line {Span.Start.Line}, column {Span.Start.Column}: {Message}";
    }
}

public static class CtsSourcePosition
{
    public static int GetOffset(string source, SourceLocation location)
    {
        ArgumentNullException.ThrowIfNull(source);

        int targetLine = Math.Max(1, location.Line);
        int targetColumn = Math.Max(1, location.Column);
        int line = 1;
        int offset = 0;
        while (offset < source.Length && line < targetLine)
        {
            if (source[offset] == '\r')
            {
                offset++;
                if (offset < source.Length && source[offset] == '\n')
                {
                    offset++;
                }

                line++;
            }
            else if (source[offset] == '\n')
            {
                offset++;
                line++;
            }
            else
            {
                offset++;
            }
        }

        if (line < targetLine)
        {
            return source.Length;
        }

        int remainingColumns = targetColumn - 1;
        while (offset < source.Length && remainingColumns > 0 && source[offset] is not '\r' and not '\n')
        {
            offset++;
            remainingColumns--;
        }

        return offset;
    }
}
