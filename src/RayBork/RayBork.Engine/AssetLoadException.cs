namespace RayBork;

/// <summary>Thrown when an asset payload is corrupt or cannot be decoded or uploaded.</summary>
public sealed class AssetLoadException : AssetException
{
    /// <summary>Initializes a new instance of <see cref="AssetLoadException"/>.</summary>
    public AssetLoadException() { }

    /// <summary>Initializes a new instance of <see cref="AssetLoadException"/>.</summary>
    public AssetLoadException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of <see cref="AssetLoadException"/>.</summary>
    public AssetLoadException(string message, Exception? innerException)
        : base(message, innerException) { }

    internal AssetLoadException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) { }
}
