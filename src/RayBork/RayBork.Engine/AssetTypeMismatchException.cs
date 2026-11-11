namespace RayBork;

/// <summary>
/// Thrown when an asset path exists but was baked as a different asset type.
/// </summary>
public sealed class AssetTypeMismatchException : AssetException {
    /// <summary>
    /// Initializes a new instance of the <see cref="AssetTypeMismatchException"/> class.
    /// </summary>
    public AssetTypeMismatchException() {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetTypeMismatchException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public AssetTypeMismatchException(string message) : base(message) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetTypeMismatchException"/> class with a message
    /// and an inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public AssetTypeMismatchException(string message, Exception? innerException) : base(message, innerException) {
    }

    internal AssetTypeMismatchException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) {
    }
}
