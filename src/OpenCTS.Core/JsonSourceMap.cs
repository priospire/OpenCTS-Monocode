using System.Text.Json;

namespace OpenCTS.Core;

internal sealed class JsonSourceMap
{
    public static readonly JsonSourceMap Empty = new(new Dictionary<string, SourceLocation>(StringComparer.Ordinal), Array.Empty<int>());

    private readonly Dictionary<string, SourceLocation> _locations;
    private readonly int[] _lineStarts;

    private JsonSourceMap(Dictionary<string, SourceLocation> locations, int[] lineStarts)
    {
        _locations = locations;
        _lineStarts = lineStarts;
    }

    public static JsonSourceMap Create(byte[] utf8Json)
    {
        List<int> lineStarts = [0];
        for (int i = 0; i < utf8Json.Length; i++)
        {
            if (utf8Json[i] == (byte)'\n')
            {
                lineStarts.Add(i + 1);
            }
        }

        Dictionary<string, SourceLocation> locations = new(StringComparer.Ordinal)
        {
            ["$"] = new SourceLocation(1, 1)
        };

        Utf8JsonReader reader = new(utf8Json, isFinalBlock: true, state: default);
        List<ContainerContext> stack = [];
        string? pendingPropertyPath = null;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                {
                    string propertyName = reader.GetString() ?? string.Empty;
                    string parentPath = stack.Count == 0 ? "$" : stack[^1].Path;
                    pendingPropertyPath = AppendProperty(parentPath, propertyName);
                    locations.TryAdd(pendingPropertyPath, ToLocation(reader.TokenStartIndex, lineStarts));
                    break;
                }

                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                {
                    string path = ConsumeValuePath(stack, ref pendingPropertyPath, out bool isArrayElement);
                    locations.TryAdd(path, ToLocation(reader.TokenStartIndex, lineStarts));
                    stack.Add(new ContainerContext(path, reader.TokenType, isArrayElement));
                    break;
                }

                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                {
                    if (stack.Count == 0)
                    {
                        break;
                    }

                    ContainerContext ended = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    if (ended.IsArrayElement && stack.Count > 0 && stack[^1].Kind == JsonTokenType.StartArray)
                    {
                        stack[^1].NextArrayIndex++;
                    }

                    break;
                }

                default:
                {
                    string path = ConsumeValuePath(stack, ref pendingPropertyPath, out bool isArrayElement);
                    locations.TryAdd(path, ToLocation(reader.TokenStartIndex, lineStarts));
                    if (isArrayElement && stack.Count > 0 && stack[^1].Kind == JsonTokenType.StartArray)
                    {
                        stack[^1].NextArrayIndex++;
                    }

                    break;
                }
            }
        }

        return new JsonSourceMap(locations, lineStarts.ToArray());
    }

    public SourceLocation? GetLocation(string path)
    {
        if (_locations.TryGetValue(path, out SourceLocation? location))
        {
            return location;
        }

        return _locations.TryGetValue("$", out SourceLocation? rootLocation) ? rootLocation : null;
    }

    private static string ConsumeValuePath(List<ContainerContext> stack, ref string? pendingPropertyPath, out bool isArrayElement)
    {
        if (pendingPropertyPath is not null)
        {
            string path = pendingPropertyPath;
            pendingPropertyPath = null;
            isArrayElement = false;
            return path;
        }

        if (stack.Count > 0 && stack[^1].Kind == JsonTokenType.StartArray)
        {
            isArrayElement = true;
            return $"{stack[^1].Path}[{stack[^1].NextArrayIndex}]";
        }

        isArrayElement = false;
        return "$";
    }

    private static SourceLocation ToLocation(long byteOffset, List<int> lineStarts)
    {
        int offset = checked((int)byteOffset);
        int lineIndex = lineStarts.BinarySearch(offset);
        if (lineIndex < 0)
        {
            lineIndex = ~lineIndex - 1;
        }

        if (lineIndex < 0)
        {
            lineIndex = 0;
        }

        return new SourceLocation(lineIndex + 1, offset - lineStarts[lineIndex] + 1);
    }

    private static string AppendProperty(string parentPath, string propertyName)
    {
        return IsSimplePropertyName(propertyName)
            ? $"{parentPath}.{propertyName}"
            : $"{parentPath}['{propertyName.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}']";
    }

    private static bool IsSimplePropertyName(string value)
    {
        if (value.Length == 0 || (!char.IsAsciiLetter(value[0]) && value[0] != '_'))
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private sealed class ContainerContext(string path, JsonTokenType kind, bool isArrayElement)
    {
        public string Path { get; } = path;
        public JsonTokenType Kind { get; } = kind;
        public bool IsArrayElement { get; } = isArrayElement;
        public int NextArrayIndex { get; set; }
    }
}
