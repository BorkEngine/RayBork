namespace RayBork.Bake.Toml;

/// <summary>One value being bound with its TOML path and diagnostic member path.</summary>
internal readonly record struct BakeBindNode
{
    /// <summary>Initializes a bound node.</summary>
    internal BakeBindNode(object? value, TomlPath path, string memberPath)
    {
        Value = value;
        Path = path;
        MemberPath = memberPath;
    }

    /// <summary>Gets the TOML DOM value.</summary>
    internal object? Value { get; }

    /// <summary>Gets the segment-aware path used for span lookups.</summary>
    internal TomlPath Path { get; }

    /// <summary>Gets the member path relative to the bound root.</summary>
    internal string MemberPath { get; }

    /// <summary>Creates the root node for a document or sidecar table.</summary>
    internal static BakeBindNode Root(object root, TomlPath path, string memberPath = "") =>
        new(root, path, memberPath);

    /// <summary>Creates a child node for a table key.</summary>
    internal BakeBindNode Child(string key, object? value) =>
        new(value, Path.Child(key), MemberPath.Length == 0 ? key : MemberPath + "." + key);

    /// <summary>Creates a child node for an array element.</summary>
    internal BakeBindNode Element(int index, object? value) =>
        new(
            value,
            Path.Element(index),
            MemberPath
                + "["
                + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "]"
        );

    /// <summary>Creates a child node for a dictionary value.</summary>
    internal BakeBindNode DictionaryValue(string key, object? value) =>
        new(value, Path.Child(key), MemberPath + "[" + key + "]");
}
