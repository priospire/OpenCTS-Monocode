namespace OpenCTS.Core;

public sealed class ScratchProjectEditSession
{
    private readonly ScratchArchiveSnapshot _baseline;
    private readonly ScratchAsmOriginMap _originMap;

    private ScratchProjectEditSession(
        ScratchArchiveSnapshot baseline,
        string sourceText,
        IReadOnlyList<ValidationIssue> issues,
        ScratchAsmOriginMap originMap)
    {
        _baseline = baseline;
        SourceText = sourceText;
        Issues = issues;
        _originMap = originMap;
    }

    public string InputPath => _baseline.SourcePath;

    public string SourceText { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool CanEdit => !Issues.Any(static issue => issue.Severity == DiagnosticSeverity.Error);

    public static ScratchProjectEditSession Open(string inputPath)
    {
        ScratchArchiveSnapshot snapshot = ScratchPackageSnapshotReader.Read(inputPath);
        ScratchProjectDecompilation decompilation = ScratchProjectDecompiler.Decompile(snapshot.Project);
        return new ScratchProjectEditSession(snapshot, decompilation.SourceText, decompilation.Issues, decompilation.OriginMap);
    }

    public ConversionResult WriteEdited(string sourceText, string outputPath, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        List<ValidationIssue> issues = [.. Issues];
        if (issues.Any(static issue => issue.Severity == DiagnosticSeverity.Error))
        {
            return Failure(issues);
        }

        CtsCompileResult compiled = CtsCompiler.Compile(sourceText, "edited.sasm");
        issues.AddRange(compiled.Diagnostics.Select(ToIssue));
        if (issues.Any(static issue => issue.Severity == DiagnosticSeverity.Error))
        {
            return Failure(issues);
        }

        try
        {
            ScratchMergeOutput? merged = ScratchProjectMerger.Merge(_baseline, compiled, sourceText, _originMap, issues);
            if (merged is null || issues.Any(static issue => issue.Severity == DiagnosticSeverity.Error))
            {
                return Failure(issues);
            }

            ScratchProjectMerger.WriteArchive(merged, outputPath, overwrite);
            return new ConversionResult
            {
                Success = true,
                OutputPath = Path.GetFullPath(outputPath),
                Issues = issues
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            issues.Add(new ValidationIssue(ex.Message, "$", null));
            return Failure(issues);
        }
    }

    private static ValidationIssue ToIssue(CtsDiagnostic diagnostic) => new(
        diagnostic.Message,
        "$",
        diagnostic.Span.Start,
        diagnostic.Severity,
        diagnostic.Code,
        diagnostic.Span);

    private static ConversionResult Failure(IReadOnlyList<ValidationIssue> issues) => new()
    {
        Success = false,
        Issues = issues
    };
}
