namespace RayBork.Internal;

/// <summary>A controlled failure for a valid font feature outside the subset.</summary>
/// <remarks>
/// The face is not corrupt: it uses an unsupported feature or exceeds a documented defensive limit,
/// and <see cref="Reason"/> names the violated feature or limit. <see cref="FontAssetFormat"/> surfaces
/// it as an <see cref="AssetLoadException"/> with the <c>RB1011</c> font diagnostic.
/// </remarks>
internal sealed class FontUnsupportedException : Exception
{
    /// <summary>Initializes an unsupported-feature failure with an actionable reason.</summary>
    internal FontUnsupportedException(string reason)
        : base($"The font face uses an unsupported feature: {reason}.")
    {
        Reason = reason;
    }

    /// <summary>Gets the actionable cause, without the message prefix.</summary>
    internal string Reason { get; }
}
