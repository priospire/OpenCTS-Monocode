namespace OpenCTS.LanguageServices;

public sealed class WorkspacePathPolicy
{
    private readonly string _rootPrefix;

    public WorkspacePathPolicy(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(RootPath))
        {
            throw new DirectoryNotFoundException($"Workspace root was not found: {RootPath}");
        }

        RejectReparsePoint(RootPath);
        _rootPrefix = RootPath + Path.DirectorySeparatorChar;
    }

    public string RootPath { get; }

    public string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException("Workspace-relative path is required.");
        }

        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("\\\\", StringComparison.Ordinal) ||
            relativePath.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException("Rooted, UNC, and device paths are not allowed.");
        }

        string resolved = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        if (!resolved.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Path escapes the configured workspace root.");
        }

        CheckExistingSegments(resolved);
        return resolved;
    }

    private void CheckExistingSegments(string resolved)
    {
        string relative = Path.GetRelativePath(RootPath, resolved);
        string current = RootPath;
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current))
            {
                RejectReparsePoint(current);
            }
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"Reparse points are not allowed in workspace paths: {path}");
        }
    }
}

