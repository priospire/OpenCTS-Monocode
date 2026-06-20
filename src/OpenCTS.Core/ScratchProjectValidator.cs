using System.Text.Json;

namespace OpenCTS.Core;

internal static class ScratchProjectValidator
{
    public static IReadOnlyList<ScratchAssetReference> Validate(
        JsonElement root,
        JsonSourceMap sourceMap,
        ScratchInputPackage package,
        List<ValidationIssue> issues)
    {
        List<ScratchAssetReference> assetReferences = [];

        if (root.ValueKind != JsonValueKind.Object)
        {
            AddIssue(issues, "Scratch project.json root must be an object.", "$", sourceMap);
            return assetReferences;
        }

        if (RequireProperty(root, "targets", JsonValueKind.Array, "$", sourceMap, issues, out JsonElement targets))
        {
            ValidateTargets(targets, sourceMap, package, issues, assetReferences);
        }

        RequireProperty(root, "monitors", JsonValueKind.Array, "$", sourceMap, issues, out _);
        RequireProperty(root, "extensions", JsonValueKind.Array, "$", sourceMap, issues, out _);
        RequireProperty(root, "meta", JsonValueKind.Object, "$", sourceMap, issues, out _);

        return assetReferences;
    }

    private static void ValidateTargets(
        JsonElement targets,
        JsonSourceMap sourceMap,
        ScratchInputPackage package,
        List<ValidationIssue> issues,
        List<ScratchAssetReference> assetReferences)
    {
        if (targets.GetArrayLength() == 0)
        {
            AddIssue(issues, "Scratch project must contain at least one target.", "$.targets", sourceMap);
            return;
        }

        int stageCount = 0;
        int targetIndex = 0;
        foreach (JsonElement target in targets.EnumerateArray())
        {
            string targetPath = $"$.targets[{targetIndex}]";
            if (target.ValueKind != JsonValueKind.Object)
            {
                AddIssue(issues, "Each Scratch target must be an object.", targetPath, sourceMap);
                targetIndex++;
                continue;
            }

            bool targetIsStage = false;
            if (RequireProperty(target, "isStage", JsonValueKind.True, JsonValueKind.False, targetPath, sourceMap, issues, out JsonElement isStage))
            {
                targetIsStage = isStage.GetBoolean();
                if (targetIsStage)
                {
                    stageCount++;
                }

                if (targetIndex == 0 && !targetIsStage)
                {
                    AddIssue(issues, "Scratch requires the first target to be the stage.", $"{targetPath}.isStage", sourceMap);
                }
                else if (targetIndex > 0 && targetIsStage)
                {
                    AddIssue(issues, "Scratch allows only the first target to be the stage.", $"{targetPath}.isStage", sourceMap);
                }
            }

            if (RequireProperty(target, "name", JsonValueKind.String, targetPath, sourceMap, issues, out JsonElement name) &&
                targetIsStage && !string.Equals(name.GetString(), "Stage", StringComparison.Ordinal))
            {
                AddIssue(issues, "Scratch requires the stage target name to be 'Stage'.", $"{targetPath}.name", sourceMap);
            }
            RequireProperty(target, "variables", JsonValueKind.Object, targetPath, sourceMap, issues, out _);
            RequireProperty(target, "lists", JsonValueKind.Object, targetPath, sourceMap, issues, out _);
            RequireProperty(target, "broadcasts", JsonValueKind.Object, targetPath, sourceMap, issues, out _);
            if (RequireProperty(target, "blocks", JsonValueKind.Object, targetPath, sourceMap, issues, out JsonElement blocks))
            {
                ValidateBlocks(blocks, $"{targetPath}.blocks", sourceMap, issues);
            }

            RequireProperty(target, "comments", JsonValueKind.Object, targetPath, sourceMap, issues, out _);
            if (RequireProperty(target, "costumes", JsonValueKind.Array, targetPath, sourceMap, issues, out JsonElement costumes))
            {
                ValidateAssets(costumes, $"{targetPath}.costumes", sourceMap, package, issues, assetReferences);
            }

            if (RequireProperty(target, "sounds", JsonValueKind.Array, targetPath, sourceMap, issues, out JsonElement sounds))
            {
                ValidateAssets(sounds, $"{targetPath}.sounds", sourceMap, package, issues, assetReferences);
            }

            targetIndex++;
        }

        if (stageCount == 0)
        {
            AddIssue(issues, "Scratch project must contain a stage target where isStage is true.", "$.targets", sourceMap);
        }
        else if (stageCount > 1)
        {
            AddIssue(issues, "Scratch project must contain exactly one stage target.", "$.targets", sourceMap);
        }
    }

    private static void ValidateAssets(
        JsonElement assets,
        string assetsPath,
        JsonSourceMap sourceMap,
        ScratchInputPackage package,
        List<ValidationIssue> issues,
        List<ScratchAssetReference> assetReferences)
    {
        int assetIndex = 0;
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string assetPath = $"{assetsPath}[{assetIndex}]";
            if (asset.ValueKind != JsonValueKind.Object)
            {
                AddIssue(issues, "Scratch asset entries must be objects.", assetPath, sourceMap);
                assetIndex++;
                continue;
            }

            RequireNonEmptyString(asset, "assetId", assetPath, sourceMap, issues, out _);
            RequireNonEmptyString(asset, "name", assetPath, sourceMap, issues, out _);
            bool hasMd5Ext = RequireNonEmptyString(asset, "md5ext", assetPath, sourceMap, issues, out string md5Ext);
            bool hasDataFormat = RequireNonEmptyString(asset, "dataFormat", assetPath, sourceMap, issues, out string dataFormat);

            if (hasMd5Ext && !IsSafeAssetFileName(md5Ext))
            {
                AddIssue(issues, "Asset md5ext must be a file name, not a path.", $"{assetPath}.md5ext", sourceMap);
            }

            if (hasMd5Ext && hasDataFormat && !md5Ext.EndsWith("." + dataFormat, StringComparison.OrdinalIgnoreCase))
            {
                AddIssue(issues, "Asset md5ext extension must match dataFormat.", $"{assetPath}.md5ext", sourceMap);
            }

            if (hasMd5Ext && IsSafeAssetFileName(md5Ext))
            {
                if (!package.HasAsset(md5Ext))
                {
                    AddIssue(issues, $"Referenced asset file is missing: {md5Ext}", $"{assetPath}.md5ext", sourceMap);
                }
                else
                {
                    assetReferences.Add(new ScratchAssetReference(md5Ext));
                }
            }

            assetIndex++;
        }
    }

    private static void ValidateBlocks(
        JsonElement blocks,
        string blocksPath,
        JsonSourceMap sourceMap,
        List<ValidationIssue> issues)
    {
        foreach (JsonProperty blockProperty in blocks.EnumerateObject())
        {
            string blockPath = $"{blocksPath}.{blockProperty.Name}";
            JsonElement block = blockProperty.Value;
            if (block.ValueKind != JsonValueKind.Object)
            {
                AddIssue(issues, "Scratch block entries must be objects.", blockPath, sourceMap);
                continue;
            }

            RequireProperty(block, "opcode", JsonValueKind.String, blockPath, sourceMap, issues, out _);
            RequireStringOrNull(block, "next", blockPath, sourceMap, issues);
            RequireStringOrNull(block, "parent", blockPath, sourceMap, issues);
            RequireProperty(block, "inputs", JsonValueKind.Object, blockPath, sourceMap, issues, out _);
            RequireProperty(block, "fields", JsonValueKind.Object, blockPath, sourceMap, issues, out _);
            RequireProperty(block, "shadow", JsonValueKind.True, JsonValueKind.False, blockPath, sourceMap, issues, out _);
            RequireProperty(block, "topLevel", JsonValueKind.True, JsonValueKind.False, blockPath, sourceMap, issues, out _);
        }
    }

    private static bool RequireNonEmptyString(
        JsonElement parent,
        string propertyName,
        string parentPath,
        JsonSourceMap sourceMap,
        List<ValidationIssue> issues,
        out string value)
    {
        value = string.Empty;
        if (!RequireProperty(parent, propertyName, JsonValueKind.String, parentPath, sourceMap, issues, out JsonElement element))
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        if (value.Length == 0)
        {
            AddIssue(issues, $"Required Scratch property '{propertyName}' must not be empty.", $"{parentPath}.{propertyName}", sourceMap);
            return false;
        }

        return true;
    }

    private static bool RequireStringOrNull(
        JsonElement parent,
        string propertyName,
        string parentPath,
        JsonSourceMap sourceMap,
        List<ValidationIssue> issues)
    {
        string propertyPath = $"{parentPath}.{propertyName}";
        if (!parent.TryGetProperty(propertyName, out JsonElement value))
        {
            AddIssue(issues, $"Missing required Scratch property '{propertyName}'.", propertyPath, sourceMap.GetLocation(parentPath));
            return false;
        }

        if (value.ValueKind is JsonValueKind.String or JsonValueKind.Null)
        {
            return true;
        }

        AddIssue(issues, $"Expected {propertyPath} to be string or null, but found {Describe(value.ValueKind)}.", propertyPath, sourceMap);
        return false;
    }

    private static bool RequireProperty(
        JsonElement parent,
        string propertyName,
        JsonValueKind expectedKind,
        string parentPath,
        JsonSourceMap sourceMap,
        List<ValidationIssue> issues,
        out JsonElement value)
    {
        string propertyPath = $"{parentPath}.{propertyName}";
        if (!parent.TryGetProperty(propertyName, out value))
        {
            AddIssue(issues, $"Missing required Scratch property '{propertyName}'.", propertyPath, sourceMap.GetLocation(parentPath));
            return false;
        }

        if (value.ValueKind == expectedKind)
        {
            return true;
        }

        AddIssue(issues, $"Expected {propertyPath} to be {Describe(expectedKind)}, but found {Describe(value.ValueKind)}.", propertyPath, sourceMap);
        return false;
    }

    private static bool RequireProperty(
        JsonElement parent,
        string propertyName,
        JsonValueKind expectedKindA,
        JsonValueKind expectedKindB,
        string parentPath,
        JsonSourceMap sourceMap,
        List<ValidationIssue> issues,
        out JsonElement value)
    {
        string propertyPath = $"{parentPath}.{propertyName}";
        if (!parent.TryGetProperty(propertyName, out value))
        {
            AddIssue(issues, $"Missing required Scratch property '{propertyName}'.", propertyPath, sourceMap.GetLocation(parentPath));
            return false;
        }

        if (value.ValueKind == expectedKindA || value.ValueKind == expectedKindB)
        {
            return true;
        }

        AddIssue(issues, $"Expected {propertyPath} to be boolean, but found {Describe(value.ValueKind)}.", propertyPath, sourceMap);
        return false;
    }

    private static void AddIssue(List<ValidationIssue> issues, string message, string jsonPath, JsonSourceMap sourceMap)
    {
        AddIssue(issues, message, jsonPath, sourceMap.GetLocation(jsonPath));
    }

    private static void AddIssue(List<ValidationIssue> issues, string message, string jsonPath, SourceLocation? location)
    {
        issues.Add(new ValidationIssue(message, jsonPath, location));
    }

    private static string Describe(JsonValueKind kind)
    {
        return kind switch
        {
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            _ => kind.ToString()
        };
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
}

internal sealed record ScratchAssetReference(string FileName);
