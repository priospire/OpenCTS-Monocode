namespace OpenCTS.Core;

public static class ScratchCategoryColors
{
    public const string Motion = "#4C97FF";
    public const string Looks = "#9966FF";
    public const string Sound = "#CF63CF";
    public const string Events = "#FFBF00";
    public const string Control = "#FFAB19";
    public const string Sensing = "#5CB1D6";
    public const string Operators = "#59C059";
    public const string Variables = "#FF8C1A";
    public const string Lists = "#FF661A";
    public const string MyBlocks = "#FF6680";
    public const string Extensions = "#0FBD8C";
    public const string RawSyntax = "#455A64";
    public const string NeutralKeyword = "#555555";
    public const string StringLiteral = "#0B6E4F";
    public const string NumberLiteral = "#7A3E9D";
    public const string Comment = "#6A737D";

    public static IReadOnlyDictionary<string, string> CategoryPalette { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["motion"] = Motion,
            ["looks"] = Looks,
            ["sound"] = Sound,
            ["events"] = Events,
            ["control"] = Control,
            ["sensing"] = Sensing,
            ["operators"] = Operators,
            ["variables"] = Variables,
            ["lists"] = Lists,
            ["myBlocks"] = MyBlocks,
            ["extensions"] = Extensions
        };

    public static IReadOnlyDictionary<string, string> SyntaxPalette { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rawSyntax"] = RawSyntax,
            ["neutralKeyword"] = NeutralKeyword,
            ["stringLiteral"] = StringLiteral,
            ["numberLiteral"] = NumberLiteral,
            ["comment"] = Comment
        };

    public static IReadOnlyDictionary<string, string> ExtensionPalette { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pen"] = Extensions,
            ["music"] = Extensions,
            ["videoSensing"] = Extensions,
            ["text2speech"] = Extensions,
            ["translate"] = Extensions,
            ["speech2text"] = Extensions,
            ["faceSensing"] = Extensions,
            ["makeymakey"] = Extensions,
            ["microbit"] = Extensions,
            ["ev3"] = Extensions,
            ["wedo2"] = Extensions,
            ["gdxfor"] = Extensions,
            ["boost"] = Extensions
        };

    public static string GetExtensionColor(string? extensionId)
    {
        return extensionId is not null && ExtensionPalette.TryGetValue(extensionId, out string? color)
            ? color
            : Extensions;
    }
}
