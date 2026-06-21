using System.Text;

namespace OpenCTS.Core;

public sealed class ScratchIdAllocator
{
    private readonly HashSet<string> _used;

    public ScratchIdAllocator(IEnumerable<string>? usedIds = null)
    {
        _used = usedIds is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(usedIds, StringComparer.Ordinal);
    }

    public string Allocate(string preferredId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferredId);
        if (_used.Add(preferredId))
        {
            return preferredId;
        }

        int suffix = 2;
        while (!_used.Add($"{preferredId}_{suffix}"))
        {
            suffix++;
        }

        return $"{preferredId}_{suffix}";
    }

    public string AllocateSanitized(string prefix, string logicalName)
    {
        return Allocate($"{prefix}{Sanitize(logicalName)}");
    }

    public static string Sanitize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        StringBuilder builder = new();
        foreach (char character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
        }

        return builder.Length == 0 ? "id" : builder.ToString();
    }
}
