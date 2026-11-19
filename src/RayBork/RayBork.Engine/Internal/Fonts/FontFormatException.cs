namespace RayBork.Internal;

/// <summary>A controlled failure for a malformed font face.</summary>
/// <remarks>
/// A malformed, truncated, or inconsistent face is reported through this type, never as a framework
/// exception; a valid face outside the supported subset is a <see cref="FontUnsupportedException"/>.
/// <see cref="FontAssetFormat"/> surfaces it as an <see cref="AssetLoadException"/> with the
/// <c>RB1011</c> font diagnostic.
/// </remarks>
internal sealed class FontFormatException : Exception
{
    /// <summary>Initializes a font format failure with an actionable reason.</summary>
    internal FontFormatException(string reason)
        : base($"The font face is invalid: {reason}.")
    {
        Reason = reason;
    }

    /// <summary>Gets the actionable cause, without the message prefix.</summary>
    internal string Reason { get; }
}
