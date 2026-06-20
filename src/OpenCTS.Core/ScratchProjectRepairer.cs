using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCTS.Core;

internal static class ScratchProjectRepairer
{
    private static readonly byte[] DefaultSvgBytes = Encoding.UTF8.GetBytes(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"480\" height=\"360\" viewBox=\"0 0 480 360\"><rect width=\"480\" height=\"360\" fill=\"#ffffff\"/></svg>");

    private static readonly string DefaultAssetId = Convert.ToHexString(MD5.HashData(DefaultSvgBytes)).ToLowerInvariant();

    public static ScratchRepairResult Repair(ScratchInputPackage package)
    {
        JsonNode? parsed = JsonNode.Parse(package.ProjectJsonBytes);
        if (parsed is not JsonObject root)
        {
            return new ScratchRepairResult(package.ProjectJsonBytes, new Dictionary<string, byte[]>(), []);
        }

        List<ValidationIssue> actions = [];
        Dictionary<string, byte[]> generatedAssets = new(StringComparer.Ordinal);

        JsonArray? targets = EnsureRootArray(root, "targets", actions);
        EnsureRootArray(root, "monitors", actions);
        EnsureRootArray(root, "extensions", actions);
        EnsureMeta(root, actions);

        if (targets is not null)
        {
            RepairTargets(targets, package, generatedAssets, actions);
        }

        return new ScratchRepairResult(
            JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true }),
            generatedAssets,
            actions);
    }

    private static JsonArray? EnsureRootArray(JsonObject root, string propertyName, List<ValidationIssue> actions)
    {
        if (root.TryGetPropertyValue(propertyName, out JsonNode? existing))
        {
            return existing as JsonArray;
        }

        JsonArray value = [];
        root[propertyName] = value;
        AddAction(actions, $"Added missing root array '{propertyName}'.", $"$.{propertyName}");
        return value;
    }

    private static void EnsureMeta(JsonObject root, List<ValidationIssue> actions)
    {
        if (root.ContainsKey("meta"))
        {
            return;
        }

        root["meta"] = new JsonObject
        {
            ["semver"] = "3.0.0",
            ["vm"] = "0.2.0",
            ["agent"] = "OpenCTS"
        };
        AddAction(actions, "Added missing root metadata.", "$.meta");
    }

    private static void RepairTargets(
        JsonArray targets,
        ScratchInputPackage package,
        Dictionary<string, byte[]> generatedAssets,
        List<ValidationIssue> actions)
    {
        int stageIndex = -1;
        for (int index = 0; index < targets.Count; index++)
        {
            if (targets[index] is not JsonObject target)
            {
                continue;
            }

            string targetPath = $"$.targets[{index}]";
            bool isStage = GetBoolean(target["isStage"]);
            EnsureTargetDefaults(target, targetPath, isStage, isStage ? 0 : index + 1, actions);
            isStage = GetBoolean(target["isStage"]);
            if (isStage && stageIndex < 0)
            {
                stageIndex = index;
                if (!string.Equals(GetString(target["name"]), "Stage", StringComparison.Ordinal))
                {
                    target["name"] = "Stage";
                    AddAction(actions, "Restored the stage target name to 'Stage'.", $"{targetPath}.name");
                }
            }
            else if (isStage)
            {
                target["isStage"] = false;
                if (string.Equals(GetString(target["name"]), "Stage", StringComparison.Ordinal))
                {
                    target["name"] = $"Sprite{index}";
                }

                EnsureTargetDefaults(target, targetPath, isStage: false, index + 1, actions);
                AddAction(actions, "Demoted an extra stage target to a sprite.", targetPath);
                isStage = false;
            }

            if (target["blocks"] is JsonObject blocks)
            {
                RepairBlocks(blocks, $"{targetPath}.blocks", actions);
            }
            else
            {
                target["blocks"] = new JsonObject();
                AddAction(actions, "Replaced malformed block collection with an empty object.", $"{targetPath}.blocks");
            }

            if (target["costumes"] is JsonArray costumes)
            {
                RepairCostumes(costumes, $"{targetPath}.costumes", isStage, package, generatedAssets, actions);
            }
        }

        if (stageIndex < 0)
        {
            JsonObject stage = CreateTarget(isStage: true, layerOrder: 0);
            RepairCostumes(
                (JsonArray)stage["costumes"]!,
                "$.targets[0].costumes",
                isStage: true,
                package,
                generatedAssets,
                actions);
            targets.Insert(0, stage);
            AddAction(actions, "Added a missing stage target at the first target position.", "$.targets");
        }
        else if (stageIndex > 0)
        {
            JsonNode? stage = targets[stageIndex];
            targets.RemoveAt(stageIndex);
            targets.Insert(0, stage);
            AddAction(actions, "Moved the stage to the first target position required by Scratch.", "$.targets");
        }
    }

    private static void RepairBlocks(JsonObject blocks, string blocksPath, List<ValidationIssue> actions)
    {
        foreach (KeyValuePair<string, JsonNode?> entry in blocks.ToArray())
        {
            string blockPath = $"{blocksPath}.{entry.Key}";
            if (entry.Value is not JsonObject block || GetNonEmptyString(block["opcode"]) is null)
            {
                blocks.Remove(entry.Key);
                AddAction(actions, "Removed an unusable block entry with no valid opcode.", blockPath);
                continue;
            }

            EnsureStringOrNull(block, "next", blockPath, actions);
            EnsureStringOrNull(block, "parent", blockPath, actions);
            EnsureObject(block, "inputs", blockPath, actions);
            EnsureObject(block, "fields", blockPath, actions);
            EnsureBoolean(block, "shadow", false, blockPath, actions);

            bool topLevelDefault = block["parent"] is null;
            EnsureBoolean(block, "topLevel", topLevelDefault, blockPath, actions);
            if (GetBoolean(block["topLevel"]))
            {
                EnsureNumber(block, "x", 0, blockPath, actions);
                EnsureNumber(block, "y", 0, blockPath, actions);
            }
        }

        foreach (KeyValuePair<string, JsonNode?> entry in blocks)
        {
            if (entry.Value is not JsonObject block)
            {
                continue;
            }

            string blockPath = $"{blocksPath}.{entry.Key}";
            string? next = GetString(block["next"]);
            if (next is not null && !blocks.ContainsKey(next))
            {
                block["next"] = null;
                AddAction(actions, "Cleared a block link to a missing next block.", $"{blockPath}.next");
            }

            string? parent = GetString(block["parent"]);
            if (parent is not null && !blocks.ContainsKey(parent))
            {
                block["parent"] = null;
                block["topLevel"] = true;
                EnsureNumber(block, "x", 0, blockPath, actions);
                EnsureNumber(block, "y", 0, blockPath, actions);
                AddAction(actions, "Promoted a block whose parent was missing to a top-level block.", blockPath);
            }
        }
    }

    private static void EnsureStringOrNull(
        JsonObject parent,
        string propertyName,
        string parentPath,
        List<ValidationIssue> actions)
    {
        if (parent.ContainsKey(propertyName) &&
            (parent[propertyName] is null || GetString(parent[propertyName]) is not null))
        {
            return;
        }

        parent[propertyName] = null;
        AddAction(actions, $"Restored block property '{propertyName}' to null.", $"{parentPath}.{propertyName}");
    }

    private static void EnsureObject(
        JsonObject parent,
        string propertyName,
        string parentPath,
        List<ValidationIssue> actions)
    {
        if (parent[propertyName] is JsonObject)
        {
            return;
        }

        parent[propertyName] = new JsonObject();
        AddAction(actions, $"Restored block property '{propertyName}' to an empty object.", $"{parentPath}.{propertyName}");
    }

    private static void EnsureBoolean(
        JsonObject parent,
        string propertyName,
        bool defaultValue,
        string parentPath,
        List<ValidationIssue> actions)
    {
        if (parent[propertyName] is JsonValue value && value.TryGetValue(out bool _))
        {
            return;
        }

        parent[propertyName] = defaultValue;
        AddAction(actions, $"Restored block property '{propertyName}' to {defaultValue.ToString().ToLowerInvariant()}.", $"{parentPath}.{propertyName}");
    }

    private static void EnsureNumber(
        JsonObject parent,
        string propertyName,
        double defaultValue,
        string parentPath,
        List<ValidationIssue> actions)
    {
        if (parent[propertyName] is JsonValue value && value.GetValueKind() == JsonValueKind.Number)
        {
            return;
        }

        parent[propertyName] = defaultValue;
        AddAction(actions, $"Restored block coordinate '{propertyName}'.", $"{parentPath}.{propertyName}");
    }

    private static void EnsureTargetDefaults(
        JsonObject target,
        string targetPath,
        bool isStage,
        int layerOrder,
        List<ValidationIssue> actions)
    {
        AddMissing(target, "isStage", false, targetPath, actions);
        AddMissing(target, "name", isStage ? "Stage" : "Sprite", targetPath, actions);
        AddMissing(target, "variables", new JsonObject(), targetPath, actions);
        AddMissing(target, "lists", new JsonObject(), targetPath, actions);
        AddMissing(target, "broadcasts", new JsonObject(), targetPath, actions);
        AddMissing(target, "blocks", new JsonObject(), targetPath, actions);
        AddMissing(target, "comments", new JsonObject(), targetPath, actions);
        AddMissing(target, "costumes", new JsonArray(), targetPath, actions);
        AddMissing(target, "sounds", new JsonArray(), targetPath, actions);
        AddMissing(target, "currentCostume", 0, targetPath, actions);
        AddMissing(target, "volume", 100, targetPath, actions);
        AddMissing(target, "layerOrder", layerOrder, targetPath, actions);

        if (isStage)
        {
            AddMissing(target, "tempo", 60, targetPath, actions);
            AddMissing(target, "videoTransparency", 50, targetPath, actions);
            AddMissing(target, "videoState", "on", targetPath, actions);
            AddMissing(target, "textToSpeechLanguage", null, targetPath, actions);
            return;
        }

        AddMissing(target, "visible", true, targetPath, actions);
        AddMissing(target, "x", 0, targetPath, actions);
        AddMissing(target, "y", 0, targetPath, actions);
        AddMissing(target, "size", 100, targetPath, actions);
        AddMissing(target, "direction", 90, targetPath, actions);
        AddMissing(target, "draggable", false, targetPath, actions);
        AddMissing(target, "rotationStyle", "all around", targetPath, actions);
    }

    private static JsonObject CreateTarget(bool isStage, int layerOrder)
    {
        JsonObject target = new();
        List<ValidationIssue> ignoredActions = [];
        target["isStage"] = isStage;
        EnsureTargetDefaults(target, "$", isStage, layerOrder, ignoredActions);
        return target;
    }

    private static void RepairCostumes(
        JsonArray costumes,
        string costumesPath,
        bool isStage,
        ScratchInputPackage package,
        Dictionary<string, byte[]> generatedAssets,
        List<ValidationIssue> actions)
    {
        if (costumes.Count == 0)
        {
            costumes.Add(CreateDefaultCostume(isStage ? "backdrop1" : "costume1"));
            generatedAssets.TryAdd(DefaultAssetId + ".svg", DefaultSvgBytes);
            AddAction(actions, "Added a generated default SVG because the target had no costumes.", costumesPath);
            return;
        }

        for (int index = 0; index < costumes.Count; index++)
        {
            string costumePath = $"{costumesPath}[{index}]";
            JsonObject? costume = costumes[index] as JsonObject;
            if (IsUsableCostume(costume, package))
            {
                continue;
            }

            string name = GetNonEmptyString(costume?["name"])
                ?? (isStage ? "backdrop1" : "costume1");
            costumes[index] = CreateDefaultCostume(name);
            generatedAssets.TryAdd(DefaultAssetId + ".svg", DefaultSvgBytes);
            AddAction(actions, "Replaced malformed or missing costume reference with a generated default SVG.", costumePath);
        }
    }

    private static bool IsUsableCostume(JsonObject? costume, ScratchInputPackage package)
    {
        if (costume is null ||
            GetNonEmptyString(costume["assetId"]) is null ||
            GetNonEmptyString(costume["name"]) is null ||
            GetNonEmptyString(costume["md5ext"]) is not string md5Ext ||
            GetNonEmptyString(costume["dataFormat"]) is not string dataFormat)
        {
            return false;
        }

        return IsSafeAssetFileName(md5Ext) &&
            md5Ext.EndsWith("." + dataFormat, StringComparison.OrdinalIgnoreCase) &&
            package.HasAsset(md5Ext);
    }

    private static JsonObject CreateDefaultCostume(string name)
    {
        return new JsonObject
        {
            ["assetId"] = DefaultAssetId,
            ["name"] = name,
            ["md5ext"] = DefaultAssetId + ".svg",
            ["dataFormat"] = "svg",
            ["bitmapResolution"] = 1,
            ["rotationCenterX"] = 240,
            ["rotationCenterY"] = 180
        };
    }

    private static void AddMissing(
        JsonObject parent,
        string propertyName,
        JsonNode? value,
        string parentPath,
        List<ValidationIssue> actions)
    {
        if (parent.ContainsKey(propertyName))
        {
            return;
        }

        parent[propertyName] = value;
        AddAction(actions, $"Added missing target property '{propertyName}'.", $"{parentPath}.{propertyName}");
    }

    private static bool GetBoolean(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue(out bool result) && result;
    }

    private static string? GetNonEmptyString(JsonNode? node)
    {
        if (node is not JsonValue value || !value.TryGetValue(out string? result) || string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        return result;
    }

    private static string? GetString(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue(out string? result)
            ? result
            : null;
    }

    private static bool IsSafeAssetFileName(string value)
    {
        return value.Length > 0 &&
            value != "." &&
            value != ".." &&
            value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            !value.Contains('/', StringComparison.Ordinal) &&
            !value.Contains('\\', StringComparison.Ordinal);
    }

    private static void AddAction(List<ValidationIssue> actions, string message, string jsonPath)
    {
        actions.Add(new ValidationIssue(
            message,
            jsonPath,
            null,
            DiagnosticSeverity.Warning,
            "REPAIR100"));
    }
}

internal sealed record ScratchRepairResult(
    byte[] ProjectJsonBytes,
    IReadOnlyDictionary<string, byte[]> GeneratedAssets,
    IReadOnlyList<ValidationIssue> Issues);
