namespace RayBork.Internal;

/// <summary>
/// One validated table directory record: its tag and the exact slice of the face it covers.
/// </summary>
/// <remarks>
/// <see cref="Offset"/> is relative to the start of the face payload. <see cref="Data"/> is a slice of
/// the retained face bytes, so a consumer reads table content without copying it.
/// </remarks>
internal readonly struct FontTable
{
    /// <summary>Initializes a table record.</summary>
    /// <param name="tag">The table tag.</param>
    /// <param name="offset">The table offset from the start of the face.</param>
    /// <param name="data">The table bytes.</param>
    internal FontTable(FontTableTag tag, int offset, ReadOnlyMemory<byte> data)
    {
        Tag = tag;
        Offset = offset;
        Data = data;
    }

    /// <summary>Gets the table tag.</summary>
    internal FontTableTag Tag { get; }

    /// <summary>Gets the table offset from the start of the face.</summary>
    internal int Offset { get; }

    /// <summary>Gets the table byte length.</summary>
    internal int Length => Data.Length;

    /// <summary>Gets the table bytes.</summary>
    internal ReadOnlyMemory<byte> Data { get; }
}
