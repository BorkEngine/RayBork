namespace RayBork.Bake.Toml;

/// <summary>
/// One value being bound: the TOML DOM value, its segment-aware path, and the member path relative to
/// the bound root.
/// </summary>
internal readonly record struct BakeBindNode {
    /// <summary>Initializes a bound node.</summary>
    /// <param name="value">The TOML DOM value.</param>
    /// <param name="path">The segment-aware TOML path.</param>
    /// <param name="memberPath">The member path relative to the bound root.</param>
    internal BakeBindNode(object? value, TomlPath path, string memberPath) {
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

    /// <summary>Creates the root node for a parsed document or sidecar table.</summary>
    /// <param name="root">The TOML value.</param>
    /// <param name="path">The syntax path of the value.</param>
    /// <param name="memberPath">The diagnostic member prefix.</param>
    /// <returns>The root node.</returns>
    internal static BakeBindNode Root(object root, TomlPath path, string memberPath = "")
        => new(root, path, memberPath);

    /// <summary>Creates a child node for a table key.</summary>
    /// <param name="key">The decoded key segment.</param>
    /// <param name="value">The TOML DOM value.</param>
    /// <returns>The child node.</returns>
    internal BakeBindNode Child(string key, object? value)
        => new(
            value,
            Path.Child(key),
            MemberPath.Length == 0 ? key : MemberPath + "." + key);

    /// <summary>Creates a child node for an array element.</summary>
    /// <param name="index">The zero-based element index.</param>
    /// <param name="value">The TOML DOM value.</param>
    /// <returns>The element node.</returns>
    internal BakeBindNode Element(int index, object? value)
        => new(
            value,
            Path.Element(index),
            MemberPath + "[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]");

    /// <summary>Creates a child node for a dictionary value.</summary>
    /// <param name="key">The dictionary key.</param>
    /// <param name="value">The TOML DOM value.</param>
    /// <returns>The value node.</returns>
    internal BakeBindNode DictionaryValue(string key, object? value)
        => new(value, Path.Child(key), MemberPath + "[" + key + "]");
}
