namespace RayBork;

/// <summary>Thrown when a requested asset is not in any mounted pack.</summary>
public sealed class AssetNotFoundException : AssetException
{
    /// <summary>Initializes a new instance of <see cref="AssetNotFoundException"/>.</summary>
    public AssetNotFoundException() { }

    /// <summary>Initializes a new instance of <see cref="AssetNotFoundException"/>.</summary>
    public AssetNotFoundException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of <see cref="AssetNotFoundException"/>.</summary>
    public AssetNotFoundException(string message, Exception? innerException)
        : base(message, innerException) { }

    internal AssetNotFoundException(
        string message,
        string? diagnosticCode,
        Exception? innerException
    )
        : base(message, diagnosticCode, innerException) { }
}
