namespace RayBork;

/// <summary>
/// Thrown when an asset payload is corrupt, cannot be decoded, fails a shape deserialization, or
/// cannot be uploaded to the GPU.
/// </summary>
public sealed class AssetLoadException : AssetException {
    /// <summary>
    /// Initializes a new instance of the <see cref="AssetLoadException"/> class.
    /// </summary>
    public AssetLoadException() {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetLoadException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public AssetLoadException(string message) : base(message) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetLoadException"/> class with a message and an
    /// inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public AssetLoadException(string message, Exception? innerException) : base(message, innerException) {
    }

    internal AssetLoadException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) {
    }
}
