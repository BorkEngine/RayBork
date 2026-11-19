namespace RayBork;

/// <summary>The base class for asset pipeline failures that carry a diagnostic code.</summary>
/// <remarks>Lifecycle and argument errors use the standard BCL exception types.</remarks>
public class AssetException : RayBorkException
{
    /// <summary>Initializes a new instance of <see cref="AssetException"/>.</summary>
    public AssetException() { }

    /// <summary>Initializes a new instance of <see cref="AssetException"/>.</summary>
    public AssetException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of <see cref="AssetException"/>.</summary>
    public AssetException(string message, Exception? innerException)
        : base(message, innerException) { }

    internal AssetException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) { }
}
