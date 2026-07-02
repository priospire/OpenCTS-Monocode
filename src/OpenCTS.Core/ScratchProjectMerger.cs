using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCTS.Core;

internal sealed record ScratchMergeOutput(JsonObject Project, IReadOnlyDictionary<string, byte[]> Entries);

internal static class ScratchProjectMerger
{
    private static readonly HashSet<string> StandardBlockProperties = new(StringComparer.Ordinal)
    {
        "opcode", "next", "parent", "inputs", "fields", "shadow", "topLevel", "x", "y", "mutation"
    };

    private static readonly string[] StateProperties =
    [
        "volume", "layerOrder", "tempo", "videoTransparency", "videoState", "textToSpeechLanguage",
        "visible", "x", "y", "size", "direction", "draggable", "rotationStyle"
    ];

    public static ScratchMergeOutput? Merge(
        ScratchArchiveSnapshot baseline,
        CtsCompileResult compiled,
        string source,
        ScratchAsmOriginMap originMap,
        List<ValidationIssue> issues)
    {
        if (JsonNode.Parse(compiled.ProjectJsonBytes) is not JsonObject compiledProject)
        {
            issues.Add(Error("CTS3010", "Compiled ScratchASM did not produce a project object."));
            return null;
        }

        JsonObject merged = baseline.Project.DeepClone().AsObject();
        JsonArray baselineTargets = baseline.Project["targets"] as JsonArray ?? [];
        JsonArray compiledTargets = compiledProject["targets"] as JsonArray ?? [];
        JsonArray outputTargets = [];

        foreach (JsonNode? targetNode in compiledTargets)
        {
            if (targetNode is not JsonObject compiledTarget)
            {
                continue;
            }

            bool isStage = compiledTarget["isStage"]?.GetValue<bool>() == true;
            string name = compiledTarget["name"]?.GetValue<string>() ?? (isStage ? "Stage" : string.Empty);
            JsonObject? baselineTarget = baselineTargets.OfType<JsonObject>().FirstOrDefault(target =>
                target["isStage"]?.GetValue<bool>() == isStage &&
                (isStage || string.Equals(target["name"]?.GetValue<string>(), name, StringComparison.Ordinal)));
            ScratchTargetOrigin? origin = originMap.Targets.FirstOrDefault(target =>
                target.IsStage == isStage && (isStage || target.Name == name));

            outputTargets.Add(baselineTarget is null || origin is null
                ? compiledTarget.DeepClone()
                : MergeTarget(baselineTarget, compiledTarget, origin));
        }

        merged["targets"] = outputTargets;
        merged["extensions"] = MergeExtensions(baseline.Project["extensions"] as JsonArray, compiledProject["extensions"] as JsonArray);
        JsonObject meta = merged["meta"] as JsonObject ?? [];
        meta["agent"] = ScratchAsmLanguage.DisplayName;
        meta["scratchasm"] = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["languageVersion"] = "0.2.0",
            ["sourceSha256"] = Sha256(Encoding.UTF8.GetBytes(source)),
            ["graphSha256"] = Sha256(JsonSerializer.SerializeToUtf8Bytes(outputTargets))
        };
        merged["meta"] = meta;

        Dictionary<string, byte[]> entries = baseline.Entries
            .Where(static pair => pair.Key != "project.json")
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        foreach ((string name, byte[] bytes) in compiled.Assets)
        {
            if (entries.TryGetValue(name, out byte[]? existing) && !existing.AsSpan().SequenceEqual(bytes))
            {
                issues.Add(Error("CTS3011", $"Generated asset collides with a different baseline archive entry: {name}"));
                return null;
            }

            entries[name] = bytes;
        }

        return new ScratchMergeOutput(merged, entries);
    }

    public static void WriteArchive(ScratchMergeOutput output, string outputPath, bool overwrite)
    {
        string fullPath = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".sb3", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Edited output path must end with .sb3.");
        }

        if (File.Exists(fullPath) && !overwrite)
        {
            throw new IOException($"Output file already exists: {fullPath}");
        }

        string directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (ZipArchive archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "project.json", Encoding.UTF8.GetBytes(output.Project.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true
                })));
                foreach ((string name, byte[] bytes) in output.Entries.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    WriteEntry(archive, name, bytes);
                }
            }

            File.Move(tempPath, fullPath, overwrite);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static JsonObject MergeTarget(JsonObject baseline, JsonObject compiled, ScratchTargetOrigin origin)
    {
        JsonObject output = baseline.DeepClone().AsObject();
        Dictionary<string, string> dataIds = new(StringComparer.Ordinal);
        JsonObject variables = RemapData(compiled["variables"] as JsonObject ?? [], origin.Variables, dataIds);
        JsonObject lists = RemapData(compiled["lists"] as JsonObject ?? [], origin.Lists, dataIds);
        JsonObject broadcasts = RemapData(compiled["broadcasts"] as JsonObject ?? [], origin.Broadcasts, dataIds);
        JsonObject compiledBlocks = compiled["blocks"] as JsonObject ?? [];
        RemapDataReferences(compiledBlocks, dataIds);
        JsonObject blocks = PreserveBlockIdentity(baseline["blocks"] as JsonObject ?? [], compiledBlocks, origin.BlockOrder);

        output["isStage"] = compiled["isStage"]?.DeepClone();
        output["name"] = compiled["name"]?.DeepClone();
        output["variables"] = variables;
        output["lists"] = lists;
        output["broadcasts"] = broadcasts;
        output["blocks"] = blocks;
        foreach (string property in StateProperties)
        {
            if (compiled[property] is JsonNode value)
            {
                output[property] = value.DeepClone();
            }
        }

        return output;
    }

    private static JsonObject RemapData(
        JsonObject compiled,
        IReadOnlyDictionary<string, ScratchDataOrigin> origins,
        Dictionary<string, string> idMap)
    {
        JsonObject output = [];
        foreach ((string compiledId, JsonNode? node) in compiled)
        {
            JsonNode? value = node?.DeepClone();
            string? alias = value is JsonArray tuple && tuple.Count > 0 ? NodeString(tuple[0]) : null;
            if (alias is not null && origins.TryGetValue(alias, out ScratchDataOrigin? origin))
            {
                idMap[compiledId] = origin.Id;
                if (value is JsonArray mappedTuple)
                {
                    mappedTuple[0] = origin.Name;
                }

                output[origin.Id] = value;
            }
            else
            {
                output[compiledId] = value;
            }
        }

        return output;
    }

    private static void RemapDataReferences(JsonObject blocks, IReadOnlyDictionary<string, string> dataIds)
    {
        foreach (JsonObject block in blocks.Select(static pair => pair.Value).OfType<JsonObject>())
        {
            if (block["fields"] is JsonObject fields)
            {
                foreach (string name in fields.Select(static pair => pair.Key).ToArray())
                {
                    if (fields[name] is JsonArray field && field.Count >= 2 && NodeString(field[1]) is string id &&
                        dataIds.TryGetValue(id, out string? mapped))
                    {
                        field[1] = mapped;
                    }
                }
            }

            if (block["inputs"] is JsonObject inputs)
            {
                foreach (JsonNode? input in inputs.Select(static pair => pair.Value))
                {
                    RemapPrimitiveDataId(input, dataIds);
                }
            }
        }
    }

    private static void RemapPrimitiveDataId(JsonNode? node, IReadOnlyDictionary<string, string> dataIds)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        if (array.Count >= 3 && Number(array[0]) is 11 or 12 or 13 && NodeString(array[2]) is string id &&
            dataIds.TryGetValue(id, out string? mapped))
        {
            array[2] = mapped;
            return;
        }

        foreach (JsonNode? child in array)
        {
            RemapPrimitiveDataId(child, dataIds);
        }
    }

    private static JsonObject PreserveBlockIdentity(JsonObject baseline, JsonObject compiled, IReadOnlyList<string> baselineOrder)
    {
        Dictionary<string, Queue<string>> baselineByOpcode = new(StringComparer.Ordinal);
        foreach (string id in baselineOrder)
        {
            if (baseline[id] is not JsonObject block || NodeString(block["opcode"]) is not string opcode)
            {
                continue;
            }

            if (!baselineByOpcode.TryGetValue(opcode, out Queue<string>? queue))
            {
                queue = new Queue<string>();
                baselineByOpcode[opcode] = queue;
            }

            queue.Enqueue(id);
        }

        Dictionary<string, string> idMap = new(StringComparer.Ordinal);
        foreach ((string compiledId, JsonNode? node) in compiled)
        {
            if (node is JsonObject block && NodeString(block["opcode"]) is string opcode &&
                baselineByOpcode.TryGetValue(opcode, out Queue<string>? queue) && queue.Count > 0)
            {
                idMap[compiledId] = queue.Dequeue();
            }
        }

        JsonObject output = [];
        foreach ((string compiledId, JsonNode? node) in compiled)
        {
            if (node is not JsonObject source)
            {
                continue;
            }

            JsonObject block = source.DeepClone().AsObject();
            RemapBlockReferences(block, idMap);
            string outputId = idMap.GetValueOrDefault(compiledId, compiledId);
            if (baseline[outputId] is JsonObject original)
            {
                foreach ((string name, JsonNode? value) in original)
                {
                    if (!StandardBlockProperties.Contains(name) && !block.ContainsKey(name))
                    {
                        block[name] = value?.DeepClone();
                    }
                }
            }

            output[outputId] = block;
        }

        return output;
    }

    private static void RemapBlockReferences(JsonObject block, IReadOnlyDictionary<string, string> idMap)
    {
        foreach (string property in new[] { "next", "parent" })
        {
            if (NodeString(block[property]) is string id && idMap.TryGetValue(id, out string? mapped))
            {
                block[property] = mapped;
            }
        }

        if (block["inputs"] is not JsonObject inputs)
        {
            return;
        }

        foreach (JsonNode? input in inputs.Select(static pair => pair.Value))
        {
            if (input is not JsonArray tuple)
            {
                continue;
            }

            for (int index = 1; index < Math.Min(tuple.Count, 3); index++)
            {
                if (NodeString(tuple[index]) is string id && idMap.TryGetValue(id, out string? mapped))
                {
                    tuple[index] = mapped;
                }
            }
        }
    }

    private static JsonArray MergeExtensions(JsonArray? baseline, JsonArray? compiled)
    {
        SortedSet<string> values = new(StringComparer.Ordinal);
        foreach (JsonNode? node in (baseline ?? []).Concat(compiled ?? []))
        {
            if (NodeString(node) is string value)
            {
                values.Add(value);
            }
        }

        return new JsonArray(values.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray());
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        stream.Write(bytes);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string? NodeString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static double Number(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue(out int number))
        {
            return number;
        }

        return -1;
    }

    private static ValidationIssue Error(string code, string message) =>
        new(message, "$", null, DiagnosticSeverity.Error, code);
}
