namespace RayBork;

/// <summary>Thrown when a pack is missing, truncated, corrupt, or has a bad index.</summary>
/// <remarks>
/// A pack passed to the app builder is validated at startup; a corrupt entry in a valid pack
/// throws <see cref="AssetLoadException"/> for that path.
/// </remarks>
public sealed class AssetPackException : AssetException
{
    /// <summary>Initializes a new instance of <see cref="AssetPackException"/>.</summary>
    public AssetPackException() { }

    /// <summary>Initializes a new instance of <see cref="AssetPackException"/>.</summary>
    public AssetPackException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of <see cref="AssetPackException"/>.</summary>
    public AssetPackException(string message, Exception? innerException)
        : base(message, innerException) { }

    internal AssetPackException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) { }
}
