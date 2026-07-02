using OpenCTS.Core;

namespace OpenCTS.LanguageServices;

public sealed class WorkspaceService
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".vscode", "bin", "obj", "artifacts", "node_modules", "TestResults"
    };

    private readonly WorkspacePathPolicy _policy;

    public WorkspaceService(string rootPath)
    {
        _policy = new WorkspacePathPolicy(rootPath);
    }

    public IReadOnlyList<string> FindSourceFiles()
    {
        List<string> files = [];
        Stack<string> pending = new();
        pending.Push(_policy.RootPath);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(child)) &&
                    (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(child);
                }
            }

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if (ScratchAsmLanguage.IsSupportedSourceName(file))
                {
                    files.Add(Path.GetFullPath(file));
                }
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }
}
