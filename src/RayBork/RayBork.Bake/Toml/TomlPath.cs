using System.Globalization;

namespace RayBork.Bake.Toml;

/// <summary>A segment-aware key path into a TOML document, used for span lookups.</summary>
/// <remarks>
/// Segments are length-prefixed so quoted keys with dots or separators cannot collide; array
/// elements use a <c>#index</c> suffix.
/// </remarks>
internal readonly record struct TomlPath
{
    private TomlPath(string key) => Key = key;

    /// <summary>Gets the encoded dictionary key.</summary>
    internal string Key { get; }

    /// <summary>Gets the root path.</summary>
    internal static TomlPath Root => new(string.Empty);

    /// <summary>Returns a child path for a key segment.</summary>
    internal TomlPath Child(string name) =>
        new(Key + name.Length.ToString(CultureInfo.InvariantCulture) + ":" + name);

    /// <summary>Returns an element path for an array item.</summary>
    internal TomlPath Element(int index) =>
        new(Key + "#" + index.ToString(CultureInfo.InvariantCulture));
}
