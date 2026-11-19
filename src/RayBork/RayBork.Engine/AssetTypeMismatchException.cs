namespace RayBork;

/// <summary>Thrown when an asset path was baked as a different type.</summary>
public sealed class AssetTypeMismatchException : AssetException
{
    /// <summary>Initializes a new instance of <see cref="AssetTypeMismatchException"/>.</summary>
    public AssetTypeMismatchException() { }

    /// <summary>Initializes a new instance of <see cref="AssetTypeMismatchException"/>.</summary>
    public AssetTypeMismatchException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of <see cref="AssetTypeMismatchException"/>.</summary>
    public AssetTypeMismatchException(string message, Exception? innerException)
        : base(message, innerException) { }

    internal AssetTypeMismatchException(
        string message,
        string? diagnosticCode,
        Exception? innerException
    )
        : base(message, diagnosticCode, innerException) { }
}
