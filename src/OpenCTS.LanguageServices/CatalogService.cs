using OpenCTS.Core;

namespace OpenCTS.LanguageServices;

public sealed class CatalogService
{
    public IReadOnlyList<ScratchAsmCatalogItem> Search(
        string query,
        string? category = null,
        string? shape = null,
        int limit = 20)
    {
        string normalized = query?.Trim() ?? string.Empty;
        return CtsBlockRegistry.Definitions
            .Where(definition => normalized.Length == 0 ||
                definition.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                definition.Opcode.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Where(definition => category is null ||
                CtsBlockRegistry.GetCategoryName(definition).Contains(category, StringComparison.OrdinalIgnoreCase))
            .Where(definition => shape is null || definition.Shape.ToString().Equals(shape, StringComparison.OrdinalIgnoreCase))
            .OrderBy(definition => definition.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(static definition => definition.Name, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(definition => new ScratchAsmCatalogItem(
                definition.Name,
                definition.Opcode,
                definition.Shape.ToString(),
                CtsBlockRegistry.GetCategoryName(definition),
                definition.CategoryColor,
                definition.Bindings.Select(binding => binding.Name).ToArray(),
                definition.ExtensionId,
                definition.IsLegacy))
            .ToArray();
    }
}

