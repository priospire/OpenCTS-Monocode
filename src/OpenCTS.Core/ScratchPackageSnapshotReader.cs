using System.IO.Compression;
using System.Text.Json.Nodes;

namespace OpenCTS.Core;

internal static class ScratchPackageSnapshotReader
{
    private const int MaximumEntries = 4096;
    private const long MaximumEntryBytes = 128L * 1024 * 1024;
    private const long MaximumArchiveBytes = 512L * 1024 * 1024;

    public static ScratchArchiveSnapshot Read(string inputPath)
    {
        string fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Input .sb3 was not found: {fullPath}", fullPath);
        }

        Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);
        long totalBytes = 0;
        using (ZipArchive archive = ZipFile.OpenRead(fullPath))
        {
            if (archive.Entries.Count > MaximumEntries)
            {
                throw new InvalidDataException($"Input .sb3 contains more than {MaximumEntries} ZIP entries.");
            }

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!IsSafeRootEntry(entry))
                {
                    throw new InvalidDataException($"Input .sb3 contains an unsafe ZIP entry path: {entry.FullName}");
                }

                if (entry.Length > MaximumEntryBytes)
                {
                    throw new InvalidDataException($"ZIP entry is too large: {entry.FullName}");
                }

                totalBytes += entry.Length;
                if (totalBytes > MaximumArchiveBytes)
                {
                    throw new InvalidDataException("Input .sb3 expands beyond the supported size limit.");
                }

                if (!entries.TryAdd(entry.FullName, ReadEntry(entry)))
                {
                    throw new InvalidDataException($"Input .sb3 contains a duplicate ZIP entry: {entry.FullName}");
                }
            }
        }

        if (!entries.TryGetValue("project.json", out byte[]? projectBytes))
        {
            throw new InvalidDataException("Input .sb3 does not contain project.json at the archive root.");
        }

        JsonNode? parsed = JsonNode.Parse(projectBytes);
        if (parsed is not JsonObject project)
        {
            throw new InvalidDataException("project.json must contain a JSON object.");
        }

        return new ScratchArchiveSnapshot(fullPath, project, entries);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static bool IsSafeRootEntry(ZipArchiveEntry entry)
    {
        string path = entry.FullName;
        return path.Length > 0 &&
            path == entry.Name &&
            path != "." &&
            path != ".." &&
            path.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            !path.Contains('/', StringComparison.Ordinal) &&
            !path.Contains('\\', StringComparison.Ordinal);
    }
}
